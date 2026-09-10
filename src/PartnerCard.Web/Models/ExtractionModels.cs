using System.Text.Json.Serialization;

namespace PartnerCard.Web.Models;

/// <summary>
/// Kết quả đọc một tấm danh thiếp — hình dạng theo schema ở SPEC mục 4.4.
///
/// <see cref="Warnings"/> **không** thuộc schema đó: mô hình không bao giờ trả nó về.
/// Nó do <c>Normalizer</c> và <c>SchemaGuard</c> thêm vào **sau** khi guard đã soi JSON thô,
/// nên SG-1 không bao giờ nhìn thấy nó như một khoá lạ (SPEC mục 5.1 và mục 7).
/// </summary>
public sealed record CardExtractionResult(
    bool IsBusinessCard,
    string RejectReason,
    string FullName,
    string JobTitle,
    string Company,
    IReadOnlyList<string> Phones,
    IReadOnlyList<string> Emails,
    string Website,
    string Address,
    string DetectedLanguage,
    string SearchAlias,
    IReadOnlyDictionary<string, double> FieldConfidence)
{
    /// <summary>
    /// Ghi chú thông tin, không phải dữ liệu. Ví dụ <c>unnormalizedPhone</c>.
    ///
    /// <c>[JsonIgnore]</c> vì nó **không thuộc schema mục 4.4**: mô hình không bao giờ gửi nó về,
    /// và nếu ta serialize nó ra thì guard sẽ coi là khoá lạ và SG-1 kêu oan.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Kết quả từ chối: mọi trường rỗng, mọi điểm bằng 0. Đây là hình dạng US-02 bảo đảm —
    /// đã nói không đọc được thì không được đồng thời đưa ra dữ liệu.
    /// </summary>
    public static CardExtractionResult NotACard(string reason) => new(
        IsBusinessCard: false,
        RejectReason: reason,
        FullName: string.Empty,
        JobTitle: string.Empty,
        Company: string.Empty,
        Phones: [],
        Emails: [],
        Website: string.Empty,
        Address: string.Empty,
        DetectedLanguage: string.Empty,
        SearchAlias: string.Empty,
        FieldConfidence: ZeroConfidence());

    private static IReadOnlyDictionary<string, double> ZeroConfidence() =>
        Extraction.CardSchema.ConfidenceRequiredFields.ToDictionary(
            field => field, _ => 0.0, StringComparer.Ordinal);
}
