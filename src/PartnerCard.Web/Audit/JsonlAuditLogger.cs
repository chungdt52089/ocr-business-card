using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartnerCard.Web.Audit;

/// <summary>
/// Nhật ký audit trên file JSON Lines — SPEC mục 12: <c>logs/audit-{yyyyMMdd}.jsonl</c>, mỗi dòng một
/// <see cref="AuditEntry"/>, chỉ ghi thêm.
///
/// **Ghi thẳng, không hàng đợi.** <see cref="Log"/> ghi trên chính luồng của người gọi; khi nó trả về thì
/// dòng đã nằm trong file. Không bộ đệm, không luồng nền, không có gì phải xả lúc tắt server.
///
/// **Cái khoá không phải hàng đợi trá hình.** Giao diện Blazor và tool MCP dùng chung instance singleton
/// này, nên có thể ghi cùng lúc vào cùng một file — mà <c>File.AppendAllText</c> mở file với
/// <c>FileShare.Read</c>, luồng thứ hai sẽ dính sharing violation và mất dòng. Khoá biến tranh chấp thành
/// chờ, và chỉ bọc đúng một lần append: người chờ bị chặn tại chỗ chứ không giao việc cho ai, không có sức
/// chứa, không hứa thứ tự.
///
/// **Không bao giờ ném.** <c>CardPipeline</c> gọi <see cref="Log"/> bên trong khối <c>try</c> của nó: ném ở
/// nhánh trích xuất thì một lần đọc thành công thành <c>extract_failed</c>; ném ở nhánh lưu — sau
/// <c>UpsertAsync</c> — thì hồ sơ đã vào kho mà người dùng nhận <c>save_failed</c>, bấm lại, và gặp cảnh báo
/// trùng với chính hồ sơ vừa lưu. Đĩa đầy, file bị khoá, không có quyền: mất dòng đó, báo qua
/// <see cref="ILogger"/>, lời gọi tool đi tiếp. Không thử lại và không giữ dòng lỗi để ghi sau — giữ lại để
/// ghi sau chính là một hàng đợi.
/// </summary>
public sealed class JsonlAuditLogger(
    string directory,
    TimeProvider clock,
    ILogger<JsonlAuditLogger> logger) : IAuditLogger
{
    /// <summary>
    /// Tính theo <c>ContentRootPath</c> bằng đúng hàm giải <c>DataDirectory</c> (SPEC mục 13), nên trỏ tới
    /// <c>Code\logs\</c>.
    ///
    /// Cố ý là hằng, không phải khoá cấu hình: SPEC mục 13 không có khoá này, và thêm khoá thì phải thêm ở
    /// cả <c>appsettings.json</c>, <c>PartnerCardOptions</c> lẫn SPEC — lệch một nơi là cái bẫy đã cắn dự án
    /// này một lần với <c>Model</c>.
    /// </summary>
    public const string DefaultDirectory = "../../logs";

    /// <summary>
    /// <c>UnsafeRelaxedJsonEscaping</c> để chữ Nhật và tiếng Việt nằm nguyên trong file. Để mặc định thì
    /// <c>Nguyễn</c> thành <c>Nguy\u1EC5n</c>, và phép thử <c>findstr /C:"Nguyễn" logs\*.jsonl</c> của SPEC
    /// mục 12 **luôn xanh giả** kể cả khi dữ liệu đã lọt. Ký tự điều khiển vẫn bị escape, nên một
    /// <c>X-Session-Id</c> chứa xuống dòng không bẻ được một dòng thành hai.
    /// </summary>
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Lock _appendLock = new();

    public void Log(AuditEntry entry)
    {
        try
        {
            // Giờ máy chứ không phải UTC: ts mang +07:00 như ví dụ SPEC mục 12, và file đổi ngày lúc nửa
            // đêm ở chỗ người đọc chứ không phải lúc 7 giờ sáng.
            var local = TimeZoneInfo.ConvertTime(entry.Timestamp, clock.LocalTimeZone);
            var path = Path.Combine(
                directory, $"audit-{local.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.jsonl");

            // Tuần tự hoá nằm ngoài khoá: khoá chỉ bọc đúng lần append.
            var line = JsonSerializer.Serialize(AuditLine.From(entry, local), LineOptions) + "\n";

            lock (_appendLock)
            {
                // Mỗi lần ghi một lần: rẻ, idempotent, và bao cả ca thư mục bị xoá khi server đang chạy.
                Directory.CreateDirectory(directory);
                File.AppendAllText(path, line);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Không ghi được dòng audit cho tool {Tool} (mức {Level}); lời gọi tool vẫn tiếp tục.",
                entry.Tool,
                entry.Level);
        }
    }

    /// <summary>
    /// Hình dạng một dòng — **danh sách trắng ánh xạ tay** từ <see cref="AuditEntry"/>, đúng thứ tự khoá ở
    /// ví dụ SPEC mục 12. Không serialize thẳng <see cref="AuditEntry"/>: ai thêm trường vào record thì
    /// trường đó không tự lọt vào file (A-03).
    /// </summary>
    private sealed record AuditLine(
        DateTimeOffset Ts,
        string SessionId,
        string Tool,
        string? PartnerId,
        string? ImageSha256,
        bool? IsBusinessCard,
        int FieldsFilled,
        double AvgConfidence,
        int Warnings,
        string? Model,
        string? PromptVersion,
        int TokensIn,
        int TokensOut,
        int LatencyMs,
        string Level,
        // Hai mã chỉ có mặt khi có giá trị — một dòng không bao giờ mang cả hai.
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BlockCode,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ErrorCode)
    {
        public static AuditLine From(AuditEntry entry, DateTimeOffset localTimestamp) => new(
            Ts: localTimestamp,
            SessionId: entry.SessionId,
            Tool: entry.Tool,
            PartnerId: entry.PartnerId,
            ImageSha256: entry.ImageSha256,
            IsBusinessCard: entry.IsBusinessCard,
            FieldsFilled: entry.FieldsFilled,
            AvgConfidence: entry.AvgConfidence,
            Warnings: entry.Warnings,
            Model: entry.Model,
            PromptVersion: entry.PromptVersion,
            TokensIn: entry.TokensIn,
            TokensOut: entry.TokensOut,
            LatencyMs: entry.LatencyMs,
            Level: entry.Level.ToString(),
            BlockCode: entry.BlockCode,
            ErrorCode: entry.ErrorCode);
    }
}
