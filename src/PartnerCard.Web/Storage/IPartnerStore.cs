using PartnerCard.Web.Models;

namespace PartnerCard.Web.Storage;

/// <summary>Tầng lưu trữ hồ sơ đối tác — SPEC mục 3.3.</summary>
public interface IPartnerStore
{
    /// <summary>Lấy theo mã, không phân biệt hoa thường (S-03). Không có thì trả <c>null</c> (S-02).</summary>
    Task<Partner?> GetAsync(string partnerId, CancellationToken ct);

    Task<IReadOnlyList<Partner>> SearchAsync(PartnerQuery query, CancellationToken ct);

    /// <summary>
    /// Thêm mới hoặc ghi đè. <see cref="Partner.PartnerId"/> rỗng thì cấp mã mới tăng dần (S-04);
    /// đã có mã thì giữ nguyên <c>CreatedAt</c> và chỉ đổi <c>UpdatedAt</c> (S-05).
    /// </summary>
    Task<Partner> UpsertAsync(Partner partner, CancellationToken ct);

    /// <summary>
    /// Xoá một hồ sơ. Mã đã cấp **không** được cấp lại (S-06) — bộ đếm chỉ tiến, không lùi.
    /// MVP không có tool MCP xoá và không có nút xoá trên giao diện (SPEC mục 10.4);
    /// phương thức này tồn tại vì nó nằm trong đặc tả tầng lưu trữ và S-06 cần nó.
    /// </summary>
    Task<bool> DeleteAsync(string partnerId, string reason, CancellationToken ct);

    /// <summary>
    /// Tìm hồ sơ trùng. **Luật duy nhất: giao nhau ở ít nhất một email**, không phân biệt
    /// hoa thường (SPEC mục 8, rút gọn 08/09). Trùng tên mà khác email thì không tính (D-03).
    /// </summary>
    Task<IReadOnlyList<Partner>> FindDuplicatesAsync(Partner candidate, CancellationToken ct);
}
