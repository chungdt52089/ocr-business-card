using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;

namespace PartnerCard.Eval;

/// <summary>
/// Luật so sánh của SPEC mục 14 và TEST-SPEC mục 12. Không biết gì về IO, nên kiểm thử được thẳng.
///
/// **Chạy <see cref="Normalizer"/> lên cả hai vế rồi mới đối chiếu.** Nhưng <c>Normalizer</c> chỉ
/// chạm tới <c>phones</c>, <c>emails</c>, <c>website</c>, <c>address</c> — nó **không đụng**
/// <c>fullName</c>, <c>jobTitle</c>, <c>company</c>. Nên ba trường chữ ấy còn phải qua thêm một
/// bước gộp khoảng trắng và bỏ dấu chấm cuối ở đây.
/// </summary>
public static class CardScorer
{
    /// <summary>
    /// Bốn trường làm nên 72 điểm: 18 thẻ × 4 = 72 (TEST-SPEC mục 12; BRD mục 9 gọi tên chúng là
    /// họ tên, công ty, điện thoại, email).
    /// </summary>
    public static readonly IReadOnlyList<string> CoreFields =
        ["fullName", "company", "phones", "emails"];

    /// <summary>Báo cáo chứ không gate — chúng không nằm trong 72 điểm.</summary>
    public static readonly IReadOnlyList<string> SecondaryFields =
        ["jobTitle", "website", "address", "detectedLanguage", "searchAlias"];

    /// <summary>Thẻ song ngữ có chữ Nhật nên nằm ở nhóm Nhật — SPEC mục 14 chia "Việt/Anh và CJK".</summary>
    public static CardGroup GroupOf(string cardCode) => cardCode switch
    {
        _ when cardCode.StartsWith("en-", StringComparison.OrdinalIgnoreCase) => CardGroup.English,
        _ when cardCode.StartsWith("ja-", StringComparison.OrdinalIgnoreCase) => CardGroup.Japanese,
        _ when cardCode.StartsWith("bi-", StringComparison.OrdinalIgnoreCase) => CardGroup.Japanese,
        _ => CardGroup.Unknown,
    };

    /// <summary><c>ja-06.jpg</c> → <c>ja-06</c>. Cùng luật <c>FakeExtractor</c> dùng để tra đáp án.</summary>
    public static string CardCodeOf(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/'));

    public static CardRole RoleOf(string cardCode, ExpectedCard? expected) => expected switch
    {
        null => CardRole.NoAnswer,
        _ when cardCode.EndsWith("-partial", StringComparison.OrdinalIgnoreCase) => CardRole.AntiFabrication,
        _ when cardCode.StartsWith("neg-", StringComparison.OrdinalIgnoreCase) => CardRole.Negative,
        _ => CardRole.Core,
    };

    public static IReadOnlyList<FieldMatch> Score(ExpectedCard expected, CardExtractionResult actual)
    {
        var want = Normalizer.Normalize(ToCard(expected)).Card;
        var got = Normalizer.Normalize(actual).Card;

        return
        [
            Text("fullName", want.FullName, got.FullName),
            Text("company", want.Company, got.Company),
            Set("phones", want.Phones, got.Phones),
            Set("emails", want.Emails, got.Emails),
            Text("jobTitle", want.JobTitle, got.JobTitle),
            Exact("website", want.Website, got.Website),
            Text("address", want.Address, got.Address),
            Exact("detectedLanguage", want.DetectedLanguage, got.DetectedLanguage),
            Alias(expected, got.SearchAlias),
        ];
    }

    /// <summary>
    /// <c>ja-01-partial</c>: đạt khi <c>emails</c> **và** <c>website</c> rỗng. Đây là ca chống bịa
    /// quan trọng nhất của bộ đo — mô hình nhìn thấy tên công ty nhưng không hề thấy tên miền, nên
    /// nếu nó suy ra email hay website từ đó thì lộ ngay (TEST-SPEC mục 12).
    /// </summary>
    public static bool AntiFabricationPasses(CardExtractionResult actual) =>
        actual.Emails.Count == 0 && string.IsNullOrWhiteSpace(actual.Website);

    /// <summary>Ca âm tính: đã nói không đọc được thì không được đồng thời đưa ra dữ liệu (US-02).</summary>
    public static bool NegativePasses(CardExtractionResult actual) =>
        !actual.IsBusinessCard
        && string.IsNullOrWhiteSpace(actual.FullName)
        && string.IsNullOrWhiteSpace(actual.JobTitle)
        && string.IsNullOrWhiteSpace(actual.Company)
        && actual.Phones.Count == 0
        && actual.Emails.Count == 0
        && string.IsNullOrWhiteSpace(actual.Website)
        && string.IsNullOrWhiteSpace(actual.Address);

    /// <summary>
    /// Điểm mô hình tự báo, **chỉ lấy của trường có giá trị**. Trường rỗng luôn được 0,0 theo
    /// SPEC mục 4.5, nên gộp chúng vào thì <c>min</c> luôn bằng 0 và cả hai con số mất sạch ý
    /// nghĩa — trong khi thứ chúng sinh ra để phát hiện là "mô hình trả 1.0 cho mọi trường".
    /// </summary>
    public static IReadOnlyList<double> FilledConfidences(CardExtractionResult card)
    {
        var filled = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["fullName"] = !string.IsNullOrWhiteSpace(card.FullName),
            ["jobTitle"] = !string.IsNullOrWhiteSpace(card.JobTitle),
            ["company"] = !string.IsNullOrWhiteSpace(card.Company),
            ["phones"] = card.Phones.Count > 0,
            ["emails"] = card.Emails.Count > 0,
            ["website"] = !string.IsNullOrWhiteSpace(card.Website),
            ["address"] = !string.IsNullOrWhiteSpace(card.Address),
            ["searchAlias"] = !string.IsNullOrWhiteSpace(card.SearchAlias),
        };

        return
        [
            .. card.FieldConfidence
                .Where(entry => filled.TryGetValue(entry.Key, out var hasValue) && hasValue)
                .Select(entry => entry.Value),
        ];
    }

    /// <summary>
    /// Đáp án ở <c>expected.json</c> thành một <see cref="CardExtractionResult"/> để chạy được
    /// <c>Normalizer</c> lên nó. <c>FieldConfidence</c> để rỗng: nó là đầu ra của mô hình, không
    /// phải sự thật trên tấm thẻ, nên không có gì để đối chiếu.
    /// </summary>
    public static CardExtractionResult ToCard(ExpectedCard expected) => new(
        IsBusinessCard: expected.IsBusinessCard,
        RejectReason: expected.RejectReason ?? string.Empty,
        FullName: expected.FullName ?? string.Empty,
        JobTitle: expected.JobTitle ?? string.Empty,
        Company: expected.Company ?? string.Empty,
        Phones: expected.Phones ?? [],
        Emails: expected.Emails ?? [],
        Website: expected.Website ?? string.Empty,
        Address: expected.Address ?? string.Empty,
        DetectedLanguage: expected.DetectedLanguage ?? string.Empty,
        SearchAlias: expected.SearchAlias ?? string.Empty,
        FieldConfidence: new Dictionary<string, double>(StringComparer.Ordinal));

    /// <summary>Gộp khoảng trắng, bỏ dấu chấm cuối — SPEC mục 14.</summary>
    private static FieldMatch Text(string field, string want, string got)
    {
        var a = Tidy(want);
        var b = Tidy(got);

        return new FieldMatch(
            field,
            Matched: string.Equals(a, b, StringComparison.Ordinal),
            // "Khớp sau khi bỏ dấu": NameKey bỏ dấu phụ Latin nhưng giữ dấu đục katakana, nên
            // トヨダ không bị coi là トヨタ — hai công ty khác nhau.
            MatchedLoose: string.Equals(TextKeys.NameKey(a), TextKeys.NameKey(b), StringComparison.Ordinal),
            Expected: want,
            Actual: got);
    }

    private static FieldMatch Exact(string field, string want, string got)
    {
        var matched = string.Equals(want.Trim(), got.Trim(), StringComparison.OrdinalIgnoreCase);

        return new FieldMatch(field, matched, matched, want, got);
    }

    /// <summary>So theo **tập hợp**, không theo thứ tự. Thiếu một phần tử tính là sai (SPEC mục 14).</summary>
    private static FieldMatch Set(string field, IReadOnlyList<string> want, IReadOnlyList<string> got)
    {
        var matched = want.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(got);

        return new FieldMatch(
            field,
            matched,
            matched,
            Expected: string.Join(" · ", want),
            Actual: string.Join(" · ", got));
    }

    /// <summary>
    /// <c>searchAlias</c> đạt khi chứa đủ phiên âm Latin của tên người, tên công ty và tỉnh/quận
    /// (TEST-SPEC mục 12). Thẻ không có khoá Latin nào — mọi thẻ tiếng Anh — thì so như trường chữ,
    /// nên thẻ Anh rỗng ở cả hai vế vẫn khớp.
    /// </summary>
    private static FieldMatch Alias(ExpectedCard expected, string got)
    {
        string[] parts = [expected.FullNameLatin ?? "", expected.CompanyLatin ?? "", expected.CityLatin ?? ""];
        var wanted = parts.Where(part => part.Length > 0).ToList();

        if (wanted.Count == 0)
        {
            return Text("searchAlias", expected.SearchAlias ?? string.Empty, got);
        }

        var matched = wanted.All(part => got.Contains(part, StringComparison.OrdinalIgnoreCase));

        return new FieldMatch(
            "searchAlias",
            matched,
            matched,
            Expected: string.Join(" + ", wanted),
            Actual: got);
    }

    private static string Tidy(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : TextKeys.Collapse(value).TrimEnd('.', ' ');
}
