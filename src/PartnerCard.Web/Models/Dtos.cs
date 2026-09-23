namespace PartnerCard.Web.Models;

/// <summary>
/// Tiêu chí tìm kiếm — SPEC mục 3.3, hình dạng lấy từ PRD mục 2 (<c>search_partners</c>).
/// </summary>
/// <param name="Keyword">
/// Từ khoá, khớp chuỗi con trên sáu trường của PRD US-06 (<c>fullName</c>, <c>company</c>, <c>emails</c>,
/// <c>searchAlias</c>, <c>jobTitle</c>, <c>address</c>). Hai phía cùng qua <c>TextKeys.NameKey</c>: không phân
/// biệt hoa thường, gõ không dấu vẫn khớp có dấu (T-14).
/// </param>
/// <param name="Company">Lọc thêm theo tên công ty, cùng luật so với <paramref name="Keyword"/>.</param>
/// <param name="Take">Số bản ghi tối đa. Xem <see cref="QueryLimits"/>.</param>
public sealed record PartnerQuery(
    string? Keyword = null,
    string? Company = null,
    int Take = QueryLimits.DefaultTake);

/// <summary>
/// Hồ sơ đối tác như agent nhìn thấy — đầu ra của <c>search_partners</c> và <c>duplicateOf</c> của
/// <c>save_partner</c> (PRD mục 2).
///
/// Chỉ các trường liên hệ và trạng thái. Bỏ đường dẫn ảnh, mã băm, điểm tin cậy, trường người sửa, xuất xứ
/// trích xuất và các trường bổ sung đã cắt (SPEC mục 9): agent không cần chúng để tìm lại một người.
/// </summary>
public sealed record PartnerDto(
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
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static PartnerDto From(Partner partner) => new(
        partner.PartnerId,
        partner.FullName,
        partner.JobTitle,
        partner.Company,
        partner.Phones,
        partner.Emails,
        partner.Website,
        partner.Address,
        partner.DetectedLanguage,
        partner.SearchAlias,
        partner.Status,
        partner.CreatedAt,
        partner.UpdatedAt);
}

/// <summary>Trần và mặc định cho <c>take</c> — ca S-07, S-08, S-09.</summary>
public static class QueryLimits
{
    public const int DefaultTake = 20;
    public const int MaxTake = 100;

    /// <summary>Số không hợp lệ về mặc định, số quá lớn bị cắt về trần.</summary>
    public static int Normalize(int take) => take switch
    {
        <= 0 => DefaultTake,
        > MaxTake => MaxTake,
        _ => take,
    };
}
