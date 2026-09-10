using PartnerCard.Web.Models;

namespace PartnerCard.Web.Processing;

/// <summary>Bản nháp người dùng xác nhận ở màn hình <c>/review</c>, sẵn sàng để lưu.</summary>
/// <param name="PartnerId">Rỗng hoặc <c>null</c> là hồ sơ mới; có mã là ghi đè.</param>
/// <param name="EditedFields">Trường nào do người sửa — nguyên liệu để cải thiện prompt (US-03).</param>
/// <param name="AllowDuplicate">
/// Người dùng đã thấy cảnh báo trùng và vẫn chọn tạo mới (ca D-04).
/// </param>
public sealed record PartnerDraft(
    string? PartnerId,
    CardExtractionResult Card,
    string SourceImage,
    string ImageSha256,
    IReadOnlyList<string> EditedFields,
    bool AllowDuplicate = false);

/// <summary>Kết quả nhánh trích xuất. **Không có <c>PartnerId</c>** vì nhánh này không lưu gì.</summary>
public sealed record ExtractOutcome(
    bool Ok,
    CardExtractionResult? Card,
    IReadOnlyList<string> ReviewFields,
    string? ErrorCode,
    string? Message,
    IReadOnlyList<GuardWarning> Warnings);

/// <summary>Kết quả nhánh lưu.</summary>
public sealed record SaveOutcome(
    bool Ok,
    string? PartnerId,
    Partner? DuplicateOf,
    string? ErrorCode,
    string? Message,
    IReadOnlyList<GuardWarning> Warnings);
