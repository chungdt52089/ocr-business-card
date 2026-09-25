using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PartnerCard.Web.Observability;

/// <summary>
/// Nguồn span và metric của app (BACKLOG H-02). Một tên duy nhất cho cả hai, để
/// <c>Program.cs</c> đăng ký bằng đúng một hằng.
///
/// **Không gắn nội dung thẻ vào span hay metric** — cùng luật với audit (CLAUDE.md luật cứng 9).
/// Chỉ số đếm, mã lỗi, cờ, tên model. Không tên, không email, không số điện thoại, không công ty,
/// không <c>sourceName</c>, không mã hồ sơ.
///
/// Không có listener (không đặt <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>) thì <c>StartActivity</c> trả
/// <c>null</c> và mọi <c>span?.SetTag</c> thành không làm gì — app chạy y như trước H-02.
/// </summary>
internal static class Telemetry
{
    public const string Name = "PartnerCard";

    public static readonly ActivitySource Source = new(Name);

    public static readonly Meter Meter = new(Name);

    /// <summary>Thời gian một lượt trích xuất trọn đường ống. Nhãn: <c>gen_ai.request.model</c>, <c>partnercard.outcome</c>.</summary>
    public static readonly Histogram<double> ExtractDuration = Meter.CreateHistogram<double>(
        "partnercard.extract.duration", unit: "ms", description: "Thời gian một lượt trích xuất trọn đường ống.");

    /// <summary>Số cảnh báo guard. Nhãn: <c>partnercard.branch</c> (extract / save).</summary>
    public static readonly Counter<long> GuardWarnings = Meter.CreateCounter<long>(
        "partnercard.guard.warnings", unit: "{warning}", description: "Số cảnh báo SchemaGuard.");

    /// <summary>
    /// Số trường người sửa ở màn xác nhận trước khi lưu — **đo human in the loop**. Chỉ đếm, không ghi
    /// tên trường hay giá trị.
    /// </summary>
    public static readonly Counter<long> HumanEdits = Meter.CreateCounter<long>(
        "partnercard.human.edits", unit: "{field}", description: "Số trường người sửa trước khi lưu.");

    /// <summary>
    /// Mở span gốc của một lượt đường ống.
    ///
    /// Giao diện Blazor Server gọi đường ống trong ngữ cảnh của kết nối <c>/_blazor</c>: khi đó
    /// <see cref="Activity.Current"/> là span Server của request WebSocket (sống suốt phiên) hoặc của
    /// lời gọi hub SignalR. Lồng vào đó thì mọi lần quét dồn chung một trace không bao giờ đóng, và
    /// Aspire không hiện mỗi lần quét thành một trace riêng. Vì vậy **cha kiểu Server thì bỏ** —
    /// span đường ống làm gốc trace mới. Cha kiểu khác (ví dụ test tự mở) thì giữ.
    ///
    /// Gán <see cref="Activity.Current"/> ở đây không rò ra người gọi: nó là <c>AsyncLocal</c>, và
    /// phương thức <c>async</c> gọi hàm này tự khôi phục ngữ cảnh khi trả về.
    ///
    /// Hệ quả phụ: lời gọi tool MCP (<c>POST /mcp</c>, cũng là span Server) cũng tách thành trace riêng.
    /// </summary>
    public static Activity? StartPipelineSpan(string name)
    {
        if (Activity.Current is { Kind: ActivityKind.Server })
        {
            Activity.Current = null;
        }

        return Source.StartActivity(name);
    }
}
