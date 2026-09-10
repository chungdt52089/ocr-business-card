using System.Text.Json;

namespace PartnerCard.Web.Extraction;

/// <summary>
/// Một mục trong <c>expected.json</c> — đáp án cho một tấm thẻ, ghi **đúng như in trên thẻ**.
///
/// File này phục vụ hai người dùng: <see cref="FakeExtractor"/> và bộ đo (T-08). Vì vậy nó có
/// thêm vài khoá chỉ bộ đo mới cần (<c>fullNameLatin</c>, <c>companyLatin</c>, <c>cityLatin</c>,
/// <c>note</c>); những khoá đó không có mặt ở đây và bị bỏ qua khi nạp — đúng chủ ý.
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
    IReadOnlyDictionary<string, double>? FieldConfidence = null);

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
