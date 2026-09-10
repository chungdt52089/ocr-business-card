namespace PartnerCard.Web.Audit;

/// <summary>Mức của một dòng nhật ký.</summary>
public enum AuditLevel
{
    Info,

    /// <summary>SchemaGuard đã chặn — ca G-11 đòi đúng mức này kèm mã lý do.</summary>
    GuardBlock,
}

/// <summary>
/// Một dòng nhật ký — SPEC mục 12.
///
/// **Không có trường nào chứa nội dung danh thiếp.** Không tên, không số điện thoại,
/// không email, không địa chỉ. Ảnh chỉ ghi mã băm. Đó là điều ca A-03 và G-12 kiểm.
/// Muốn thêm trường vào đây thì đọc lại câu trên trước.
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
    int LatencyMs = 0);

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
