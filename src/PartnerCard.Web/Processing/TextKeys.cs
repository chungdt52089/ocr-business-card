using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PartnerCard.Web.Processing;

/// <summary>
/// Khoá so sánh — SPEC mục 5.3. **Chỉ dùng để chống trùng và tìm kiếm.**
/// Sinh lúc chạy, không bao giờ ghi vào hồ sơ: hồ sơ luôn giữ giá trị gốc (ca N-13).
///
/// Ở MVP mới có <c>emailKey</c> được gọi (SPEC mục 8 rút chống trùng về chỉ so email).
/// <c>nameKey</c> và <c>companyKey</c> vẫn được xây và kiểm thử vì chúng là nền cho F-05
/// (gộp hồ sơ trùng) và F-16 — **không phải code chết**.
/// </summary>
public static partial class TextKeys
{
    /// <summary>
    /// Chỉ danh pháp nhân, ở dạng đã qua <see cref="NameKey"/> với dấu câu đổi thành khoảng trắng.
    /// Xếp dài trước ngắn để <c>cong ty tnhh</c> được thử trước <c>cty</c>.
    /// </summary>
    private static readonly string[] LegalDesignators =
    [
        // Việt và các tiếng khác gặp trong bộ mẫu
        "cong ty co phan", "cong ty tnhh", "cty",
        "股份有限公司", "有限公司", "주식회사",
        // Nhật — SPEC mục 5.3
        "一般社団法人", "株式会社", "有限会社", "合同会社",
        // Anh
        "corporation", "limited", "corp", "inc", "llc", "ltd", "kk", "k k", "co",
    ];

    /// <summary>
    /// Bỏ dấu, chữ thường, gộp khoảng trắng. <c>Nguyễn Văn An</c> → <c>nguyen van an</c>.
    /// Đây cũng là cơ chế phục vụ US-06: gõ không dấu vẫn tìm được.
    /// </summary>
    public static string NameKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.Trim().ToLowerInvariant().Replace('đ', 'd');
        var decomposed = lowered.Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        var lastBase = '\0';

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                // Chỉ bỏ dấu phụ của chữ Latin. Dấu đục của katakana cũng là NonSpacingMark,
                // mà bỏ nó là làm hỏng chữ: トヨダ thành トヨタ là một công ty khác.
                if (lastBase < '̀')
                {
                    continue;
                }

                builder.Append(ch);
                continue;
            }

            lastBase = ch;
            builder.Append(ch);
        }

        return Collapse(builder.ToString().Normalize(NormalizationForm.FormC));
    }

    /// <summary>
    /// <see cref="NameKey"/> rồi bỏ chỉ danh pháp nhân **ở cả đầu lẫn cuối chuỗi**.
    ///
    /// Hai vị trí là bắt buộc, không phải cho đẹp: tiếng Nhật có hai lối đặt và cả hai đều phổ biến
    /// — <c>株式会社</c> đứng trước tên (<i>前株</i>) hoặc đứng sau (<i>後株</i>). Cùng một công ty,
    /// chỉ khác thói quen đăng ký. Luật chỉ cắt cuối chuỗi bỏ sót một nửa số thẻ Nhật.
    /// </summary>
    public static string CompanyKey(string? value)
    {
        var key = NameKey(value);
        if (key.Length == 0)
        {
            return string.Empty;
        }

        // Dấu câu về khoảng trắng để "co., ltd." đi cùng một luật với "co ltd".
        key = Collapse(PunctuationPattern().Replace(key, " "));

        // Lặp tới khi không bỏ được gì nữa: "abc trading co ltd" phải qua hai vòng.
        bool trimmedAny;
        do
        {
            trimmedAny = false;

            foreach (var designator in LegalDesignators)
            {
                if (key.Length == 0)
                {
                    break;
                }

                if (TryTrimBothEnds(ref key, designator))
                {
                    trimmedAny = true;
                }
            }
        }
        while (trimmedAny);

        return key;
    }

    private static bool TryTrimBothEnds(ref string key, string designator)
    {
        var trimmed = false;

        // Chỉ danh Latin phải đứng thành từ riêng; chỉ danh CJK thì không có khoảng trắng nào
        // để dựa vào, nên khớp thẳng.
        var latin = designator.All(char.IsAscii);

        if (key.StartsWith(designator, StringComparison.Ordinal)
            && (!latin || key.Length == designator.Length || key[designator.Length] == ' '))
        {
            key = key[designator.Length..].Trim();
            trimmed = true;
        }

        if (key.Length > 0
            && key.EndsWith(designator, StringComparison.Ordinal)
            && (!latin || key.Length == designator.Length || key[^(designator.Length + 1)] == ' '))
        {
            key = key[..^designator.Length].Trim();
            trimmed = true;
        }

        return trimmed;
    }

    internal static string Collapse(string value) =>
        WhitespacePattern().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"[.,;:]")]
    private static partial Regex PunctuationPattern();
}
