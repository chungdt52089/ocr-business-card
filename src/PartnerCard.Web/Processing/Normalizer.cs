using System.Text.RegularExpressions;
using PartnerCard.Web.Models;

namespace PartnerCard.Web.Processing;

/// <summary>
/// Kết quả chuẩn hoá: thẻ đã đưa về dạng nhất quán, kèm các khoá so sánh sinh lúc chạy.
/// Các khoá **không** được ghi vào hồ sơ (SPEC mục 5.3, ca N-13).
/// </summary>
public sealed record NormalizedCard(
    CardExtractionResult Card,
    string NameKey,
    string CompanyKey,
    IReadOnlyList<string> PhoneKeys,
    IReadOnlyList<string> EmailKeys,
    IReadOnlyList<string> UnnormalizedPhones);

/// <summary>
/// Chuẩn hoá — SPEC mục 5. Không sửa nội dung, chỉ đưa về dạng nhất quán để so sánh và hiển thị.
/// </summary>
public static partial class Normalizer
{
    /// <summary>
    /// Ghi chú cho số không có mã quốc gia. **Là ghi chú thông tin, không trừ điểm tin cậy**
    /// (SPEC mục 5.1 và mục 6): `03-5550-1284` được đọc chính xác tuyệt đối, nó thiếu `+81`
    /// vì tấm thẻ vốn không in `+81`.
    /// </summary>
    public const string UnnormalizedPhoneWarning = "unnormalizedPhone";

    /// <summary>
    /// Giữ nguyên như in, **chỉ bỏ khoảng trắng, dấu chấm, gạch nối và ngoặc** (SPEC mục 5.1).
    ///
    /// Không thêm mã quốc gia cho số không có sẵn. Phạm vi Anh–Nhật không có luật suy đoán nào
    /// an toàn: người Nhật vẫn có danh thiếp tiếng Anh, và người Anh vẫn có số bắt đầu bằng 0.
    /// Ký tự lạ được giữ nguyên ở đây — dọn chúng là việc của SG-5.
    /// </summary>
    public static string Phone(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : SeparatorPattern().Replace(raw, string.Empty);

    /// <summary>Chữ thường, cắt khoảng trắng. Kiểm định dạng là việc của SG-4 và Confidence.</summary>
    public static string Email(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();

    /// <summary>Thiếu scheme thì thêm <c>https://</c>; bỏ <c>www.</c>; bỏ <c>/</c> cuối; giữ đường dẫn con.</summary>
    public static string Website(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Trim();

        foreach (var scheme in (string[])["https://", "http://"])
        {
            if (value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                value = value[scheme.Length..];
                break;
            }
        }

        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }

        value = value.TrimEnd('/');

        return value.Length == 0 ? string.Empty : "https://" + value;
    }

    /// <summary>
    /// Gộp nhiều khoảng trắng thành một, bỏ xuống dòng. **Không** tách phường/quận/tỉnh —
    /// đó là bài toán riêng, ngoài phạm vi (SPEC mục 5.2).
    /// </summary>
    public static string Address(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? string.Empty : TextKeys.Collapse(raw);

    /// <summary>
    /// Chạy ngay sau khi trích xuất. Trả về thẻ đã chuẩn hoá cộng các khoá so sánh.
    /// </summary>
    public static NormalizedCard Normalize(CardExtractionResult card)
    {
        var phones = card.Phones.Select(Phone).Where(p => p.Length > 0).ToList();
        var emails = card.Emails.Select(Email).Where(e => e.Length > 0).ToList();

        // Số không có mã quốc gia: ghi chú lại để màn hình xác nhận nói được "số này thiếu +xx",
        // nhưng không đụng tới điểm tin cậy.
        var unnormalized = phones.Where(p => !p.StartsWith('+')).ToList();

        var warnings = unnormalized.Count > 0
            ? card.Warnings.Append(UnnormalizedPhoneWarning).Distinct().ToList()
            : card.Warnings;

        var normalized = card with
        {
            Phones = phones,
            Emails = emails,
            Website = Website(card.Website),
            Address = Address(card.Address),
            Warnings = warnings,
        };

        return new NormalizedCard(
            Card: normalized,
            NameKey: TextKeys.NameKey(card.FullName),
            CompanyKey: TextKeys.CompanyKey(card.Company),
            PhoneKeys: phones,
            EmailKeys: emails,
            UnnormalizedPhones: unnormalized);
    }

    /// <summary>Khoảng trắng, dấu chấm, gạch nối và ngoặc — đúng bốn loại SPEC mục 5.1 cho phép bỏ.</summary>
    [GeneratedRegex(@"[\s.\-()]")]
    private static partial Regex SeparatorPattern();
}
