namespace PartnerCard.Web.Models;

/// <summary>Hồ sơ đối tác đã lưu — SPEC mục 3.2.</summary>
public sealed record Partner(
    string PartnerId,
    string FullName,
    string JobTitle,
    string Company,
    IReadOnlyList<string> Phones,
    IReadOnlyList<string> Emails,
    string Website,
    string Address,
    string DetectedLanguage,
    string? SearchAlias,
    string? Industry,
    string? ShortDescription,
    IReadOnlyList<string> MainProducts,
    string? EnrichmentSourceUrl,
    DateTimeOffset? EnrichedAt,
    string SourceImage,
    string ImageSha256,
    IReadOnlyDictionary<string, double> FieldConfidence,
    IReadOnlyList<string> EditedFields,
    ExtractionMeta Extraction,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Xuất xứ của một lần trích xuất. Giữ lại để so được kết quả giữa hai phiên bản prompt
/// khi chạy bộ đo (SPEC mục 4.5).
/// </summary>
public sealed record ExtractionMeta(
    string Model,
    string PromptVersion,
    DateTimeOffset ExtractedAt,
    int LatencyMs,
    int? TokensIn = null,
    int? TokensOut = null);

/// <summary>Giá trị hợp lệ của <see cref="Partner.Status"/> — SPEC mục 3.1.</summary>
public static class PartnerStatus
{
    /// <summary>Đã trích xuất, chưa có người xác nhận.</summary>
    public const string Draft = "draft";

    /// <summary>Người dùng đã xem và xác nhận. Chỉ trạng thái này mới xuất ra CSV.</summary>
    public const string Confirmed = "confirmed";
}
