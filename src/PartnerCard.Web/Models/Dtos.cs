namespace PartnerCard.Web.Models;

/// <summary>
/// Tiêu chí tìm kiếm — SPEC mục 3.3, hình dạng lấy từ PRD mục 2 (<c>search_partners</c>).
/// </summary>
/// <param name="Keyword">
/// Từ khoá. Ở T-02 mới khớp chuỗi con không phân biệt hoa thường; tìm đủ sáu trường
/// và tìm không dấu thuộc T-14, vì cả hai cần <c>Normalizer</c> của T-05.
/// </param>
/// <param name="Company">Lọc thêm theo tên công ty.</param>
/// <param name="Take">Số bản ghi tối đa. Xem <see cref="QueryLimits"/>.</param>
public sealed record PartnerQuery(
    string? Keyword = null,
    string? Company = null,
    int Take = QueryLimits.DefaultTake);

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
