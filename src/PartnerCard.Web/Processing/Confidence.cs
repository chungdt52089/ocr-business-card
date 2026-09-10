using System.Text.RegularExpressions;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Web.Processing;

/// <summary>Thẻ đã chấm điểm, kèm danh sách trường cần người xem lại.</summary>
public sealed record ScoredCard(
    CardExtractionResult Card,
    IReadOnlyDictionary<string, double> FieldConfidence,
    IReadOnlyList<string> ReviewFields);

/// <summary>
/// Điểm tin cậy — SPEC mục 6. Kết hợp hai nguồn và **lấy giá trị nhỏ hơn**:
/// điểm mô hình tự báo, và điểm kiểm định dạng.
///
/// Lấy nhỏ hơn vì mô hình có thể rất tự tin về một email sai cú pháp.
/// </summary>
public static partial class Confidence
{
    public const double DefaultReviewThreshold = 0.9;

    /// <summary>Dưới 8 chữ số thì gần như chắc chắn không phải số điện thoại.</summary>
    private const int MinPhoneDigits = 8;

    /// <summary>Trần của E.164.</summary>
    private const int MaxPhoneDigits = 15;

    public static double ForText(string? value, double modelScore) =>
        Math.Min(modelScore, FormatScoreForText(value));

    public static double ForEmail(string? value, double modelScore) =>
        Math.Min(modelScore, FormatScoreForEmail(value));

    public static double ForPhone(string? value, double modelScore) =>
        Math.Min(modelScore, FormatScoreForPhone(value));

    public static double FormatScoreForText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? 0.0 : 1.0;

    public static double FormatScoreForEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && EmailPattern().IsMatch(value.Trim()) ? 1.0 : 0.0;

    /// <summary>
    /// **Sau khi bỏ ký tự phân cách còn 8–15 chữ số = 1,0, ngoài khoảng đó = 0.**
    ///
    /// Đây là chỗ đã sửa ngày 08/09 — **không còn khái niệm "chuẩn hoá được sang E.164"**.
    /// Luật cũ cho `03-5550-1284` điểm 0,5, tức luôn dưới ngưỡng 0,9, tức 6 trong 8 thẻ Nhật
    /// của bộ mẫu luôn bị tô. Tô lúc nào cũng sáng thì không còn là tín hiệu.
    ///
    /// Số thiếu mã quốc gia được ghi chú qua <c>unnormalizedPhone</c> (SPEC mục 5.1),
    /// **không trừ điểm** — đó là câu hỏi "dữ liệu có đủ không", không phải "có đọc đúng không".
    ///
    /// Ký tự lạ lẫn trong chuỗi là việc của SG-5; tới đây trường đó đã bị dọn về rỗng.
    /// </summary>
    public static double FormatScoreForPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0.0;
        }

        var digits = value.Count(char.IsDigit);

        return digits is >= MinPhoneDigits and <= MaxPhoneDigits ? 1.0 : 0.0;
    }

    /// <summary>Ngưỡng đánh dấu "cần xem lại" là 0,9 (D-4).</summary>
    public static bool NeedsReview(double score, double threshold = DefaultReviewThreshold) =>
        score < threshold;

    /// <summary>
    /// Chấm cả tấm thẻ. Không đụng tới giá trị của bất kỳ trường nào — chỉ tính điểm.
    /// </summary>
    public static ScoredCard Evaluate(CardExtractionResult card, double threshold = DefaultReviewThreshold)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["fullName"] = ForText(card.FullName, Model(card, "fullName")),
            ["jobTitle"] = ForText(card.JobTitle, Model(card, "jobTitle")),
            ["company"] = ForText(card.Company, Model(card, "company")),
            ["website"] = ForText(card.Website, Model(card, "website")),
            ["address"] = ForText(card.Address, Model(card, "address")),
            ["searchAlias"] = ForText(card.SearchAlias, Model(card, "searchAlias")),
            ["phones"] = ForList(card.Phones, Model(card, "phones"), FormatScoreForPhone),
            ["emails"] = ForList(card.Emails, Model(card, "emails"), FormatScoreForEmail),
        };

        var reviewFields = CardSchema.ConfidenceRequiredFields
            .Where(field => NeedsReview(scores[field], threshold))
            .ToList();

        return new ScoredCard(card, scores, reviewFields);
    }

    /// <summary>Phần tử tệ nhất quyết định điểm cả mảng: thiếu một số đúng là thiếu dữ liệu.</summary>
    private static double ForList(
        IReadOnlyList<string> values, double modelScore, Func<string, double> formatScore) =>
        values.Count == 0 ? 0.0 : Math.Min(modelScore, values.Min(formatScore));

    private static double Model(CardExtractionResult card, string field) =>
        card.FieldConfidence.TryGetValue(field, out var score) ? score : 0.0;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
