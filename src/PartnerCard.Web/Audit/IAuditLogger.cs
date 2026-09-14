namespace PartnerCard.Web.Audit;

/// <summary>Mức của một dòng nhật ký.</summary>
public enum AuditLevel
{
    Info,

    /// <summary>SchemaGuard đã chặn — ca G-11 đòi đúng mức này kèm mã lý do.</summary>
    GuardBlock,

    /// <summary>
    /// Trích xuất hỏng: hết hạn mức, quá thời gian chờ, khoá sai, hoặc lỗi không đoán được.
    ///
    /// Khác <see cref="GuardBlock"/> ở chỗ **chưa hề có kết quả nào** để mà chặn. Và khác việc
    /// Validate từ chối — cái đó cố ý không ghi vì chưa chạm tới mô hình lẫn kho (SPEC mục 2),
    /// còn tới đây thì đã chạm tới mô hình rồi.
    /// </summary>
    Error,
}

/// <summary>
/// Một dòng nhật ký — SPEC mục 12.
///
/// **Không có trường nào chứa nội dung danh thiếp.** Không tên, không số điện thoại,
/// không email, không địa chỉ. Ảnh chỉ ghi mã băm. Đó là điều ca A-03 và G-12 kiểm.
/// Muốn thêm trường vào đây thì đọc lại câu trên trước.
///
/// <c>BlockCode</c> và <c>ErrorCode</c> là **hai chuyện khác nhau, đừng gộp**:
/// <list type="bullet">
/// <item><c>BlockCode</c> (<c>SG-1</c>…<c>SG-8</c>) — guard đã chặn một kết quả **đã đọc được**.</item>
/// <item><c>ErrorCode</c> (<c>quota_exhausted</c>, <c>extract_unavailable</c>,
/// <c>extract_timeout</c>, <c>extractor_auth</c>, <c>extract_failed</c>) — **chưa hề có kết quả nào** để mà chặn.</item>
/// </list>
/// Một dòng không bao giờ mang cả hai. Gộp lại là xoá mất ranh giới giữa "mô hình trả về thứ
/// không dùng được" và "mô hình không trả về gì cả" — hai chuyện xử lý khác hẳn nhau.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string SessionId,
    string Tool,
    AuditLevel Level,
    string? PartnerId = null,
    string? ImageSha256 = null,
    bool? IsBusinessCard = null,
    int FieldsFilled = 0,
    double AvgConfidence = 0,
    int Warnings = 0,
    string? BlockCode = null,
    int LatencyMs = 0,
    int TokensIn = 0,
    int TokensOut = 0,
    string? Model = null,
    string? PromptVersion = null,
    string? ErrorCode = null);

public interface IAuditLogger
{
    void Log(AuditEntry entry);
}

/// <summary>
/// Bản cài tạm cho Đợt 1: không ghi gì. Bản ghi file JSONL thật là T-11.
/// </summary>
public sealed class NullAuditLogger : IAuditLogger
{
    public void Log(AuditEntry entry)
    {
        // Cố ý rỗng.
    }
}
