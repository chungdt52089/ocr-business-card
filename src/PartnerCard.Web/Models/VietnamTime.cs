using System.Globalization;

namespace PartnerCard.Web.Models;

/// <summary>
/// Giờ Việt Nam cho trang Lịch sử và file CSV (T-14). Kho lưu mốc thời gian UTC; máy chủ Cloud Run cũng chạy UTC.
///
/// Offset **cố định <c>+07:00</c>** chứ không tra <see cref="TimeZoneInfo"/>: Việt Nam không có giờ mùa hè, nên
/// không có gì để tra — mà tra thì phụ thuộc cơ sở dữ liệu múi giờ của image, thứ khác nhau giữa Windows và
/// container Linux.
/// </summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    /// <summary><c>22/09 09:39</c> — thời gian quét trên thẻ của trang Lịch sử.</summary>
    public static string Short(DateTimeOffset time) =>
        time.ToOffset(Offset).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

    /// <summary><c>2026-09-22T09:39:12+07:00</c> — đủ để đọc lại, và thấy ngay đó là giờ Việt Nam.</summary>
    public static string Iso(DateTimeOffset time) =>
        time.ToOffset(Offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
