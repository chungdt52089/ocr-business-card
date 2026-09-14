using System.Text.Json;

namespace PartnerCard.Web.Extraction;

/// <summary>
/// Một mục trong <c>expected.json</c> — đáp án cho một tấm thẻ, ghi **đúng như in trên thẻ**.
///
/// File này phục vụ hai người dùng: <see cref="FakeExtractor"/> và bộ đo (T-08). Vì vậy nó có
/// thêm vài khoá chỉ bộ đo mới cần: <c>fullNameLatin</c>, <c>companyLatin</c>, <c>cityLatin</c>
/// (TEST-SPEC mục 12 dùng ba khoá này để kiểm <c>searchAlias</c> chứa đủ cả ba) và <c>note</c>
/// (ghi cho người đọc, không ai chấm nó).
///
/// Bốn khoá ấy **có mặt ở đây kể từ T-08**. Bản đầu cố ý bỏ chúng, nhưng khi bộ đo ra đời thì nó
/// phải tự nạp lại <c>expected.json</c> bằng đường khác chỉ để lấy ba chuỗi — tức là hai đường
/// đọc cùng một file, đúng thứ mà cả file này sinh ra để tránh. <see cref="FakeExtractor"/> không
/// đụng tới chúng nên hành vi chế độ fake không đổi.
/// </summary>
public sealed record ExpectedCard(
    bool IsBusinessCard,
    string? FullName,
    string? JobTitle,
    string? Company,
    IReadOnlyList<string>? Phones,
    IReadOnlyList<string>? Emails,
    string? Website,
    string? Address,
    string? DetectedLanguage,
    string? SearchAlias,
    string? RejectReason = null,
    IReadOnlyDictionary<string, double>? FieldConfidence = null,
    string? FullNameLatin = null,
    string? CompanyLatin = null,
    string? CityLatin = null,
    string? Note = null);

/// <summary>
/// Bảng đáp án nạp từ <c>expected.json</c>, tra theo **mã thẻ** (<c>ja-06</c>).
/// </summary>
public sealed class ExpectedCards
{
    private readonly IReadOnlyDictionary<string, ExpectedCard> _byCardCode;

    private ExpectedCards(IReadOnlyDictionary<string, ExpectedCard> byCardCode) =>
        _byCardCode = byCardCode;

    public int Count => _byCardCode.Count;

    public static ExpectedCards LoadFrom(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException(
                $"Không tìm thấy bảng đáp án {filePath}. " +
                "Chế độ fake cần file này; kiểm tra luật chép expected.json sang thư mục output.");
        }

        Dictionary<string, ExpectedCard>? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, ExpectedCard>>(
                File.ReadAllText(filePath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Không đọc được {filePath}: file không phải JSON hợp lệ.", ex);
        }

        // Mã thẻ so không phân biệt hoa thường: tên file từ trình duyệt có thể là JA-06.JPG.
        var byCardCode = new Dictionary<string, ExpectedCard>(
            parsed ?? [], StringComparer.OrdinalIgnoreCase);

        return new ExpectedCards(byCardCode);
    }

    public ExpectedCard? Find(string? cardCode) =>
        cardCode is not null && _byCardCode.TryGetValue(cardCode, out var card) ? card : null;
}
