using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;

namespace PartnerCard.Web.Tools;

// Kết quả của bốn tool MCP. Cả bốn đều mang ErrorCode + Message: thông báo trung tính nhưng KHÔNG giấu
// nguyên nhân (SPEC mục 10.3) — quota_exhausted khác extract_timeout, và cả hai khác một ảnh không phải
// danh thiếp (cái đó là Ok = true với IsBusinessCard = false).

/// <summary>
/// Kết quả <c>extract_business_card</c>. Bọc <see cref="CardExtractionResult"/> thay vì trả thẳng nó, vì
/// thẻ không có chỗ cho mã lỗi.
/// </summary>
/// <param name="Warnings">Cảnh báo của <c>SchemaGuard</c>, ví dụ email sai định dạng đã bị xoá (SG-4).</param>
/// <param name="NormalizationWarnings">
/// Cảnh báo của <c>Normalizer</c>, ví dụ <c>unnormalizedPhone</c>. Tách riêng vì
/// <see cref="CardExtractionResult.Warnings"/> mang <c>[JsonIgnore]</c>: trả thẳng thẻ là kênh này biến
/// mất khi serialize (BACKLOG T-13 — hai kênh cảnh báo).
/// </param>
public sealed record ExtractCardToolResult(
    bool Ok,
    CardExtractionResult? Card,
    IReadOnlyList<string> ReviewFields,
    IReadOnlyList<GuardWarning> Warnings,
    IReadOnlyList<string> NormalizationWarnings,
    string? ErrorCode,
    string? Message)
{
    internal static ExtractCardToolResult From(ExtractOutcome outcome) => new(
        outcome.Ok,
        outcome.Card,
        outcome.ReviewFields,
        outcome.Warnings,
        outcome.Card?.Warnings ?? [],
        outcome.ErrorCode,
        outcome.Message);

    internal static ExtractCardToolResult Failed(string errorCode, string message) =>
        new(false, null, [], [], [], errorCode, message);
}

/// <summary>Kết quả <c>save_partner</c> — PRD mục 2: <c>partnerId</c>, <c>duplicateOf?</c>, <c>warnings[]</c>.</summary>
/// <param name="DuplicateOf">Hồ sơ đang dùng chung email khi <c>ErrorCode = duplicate</c>. Không tự gộp (SPEC mục 8).</param>
public sealed record SavePartnerToolResult(
    bool Ok,
    string? PartnerId,
    PartnerDto? DuplicateOf,
    IReadOnlyList<GuardWarning> Warnings,
    string? ErrorCode,
    string? Message)
{
    internal static SavePartnerToolResult From(SaveOutcome outcome) => new(
        outcome.Ok,
        outcome.PartnerId,
        outcome.DuplicateOf is null ? null : PartnerDto.From(outcome.DuplicateOf),
        outcome.Warnings,
        outcome.ErrorCode,
        outcome.Message);

    internal static SavePartnerToolResult Failed(string errorCode, string message) =>
        new(false, null, null, [], errorCode, message);
}

/// <summary>Kết quả <c>search_partners</c>.</summary>
public sealed record SearchPartnersToolResult(
    bool Ok,
    IReadOnlyList<PartnerDto> Partners,
    string? ErrorCode,
    string? Message)
{
    internal static SearchPartnersToolResult Found(IReadOnlyList<PartnerDto> partners) =>
        new(true, partners, null, null);

    internal static SearchPartnersToolResult Failed(string errorCode, string message) =>
        new(false, [], errorCode, message);
}

/// <summary>Kết quả <c>enrich_partner</c> — luôn là <c>not_implemented</c> (SPEC mục 9, đã cắt 08/09).</summary>
public sealed record EnrichPartnerToolResult(bool Ok, string ErrorCode, string Message);
