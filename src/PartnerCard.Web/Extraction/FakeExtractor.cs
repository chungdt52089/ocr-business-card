using System.Text.Encodings.Web;
using System.Text.Json;
using PartnerCard.Web.Models;

namespace PartnerCard.Web.Extraction;

/// <summary>
/// Bản cài chạy offline: trả đáp án dựng sẵn từ <c>expected.json</c>, **tra theo mã thẻ lấy từ
/// tên file** (<c>ja-06.jpg</c> → <c>ja-06</c>) — SPEC mục 4.1.
///
/// **Không tra theo mã băm.** Cùng một tấm thẻ có hai phiên bản, PNG render và JPG chụp lại,
/// hash khác nhau nhưng chung một đáp án. Khoá theo hash thì <c>expected.json</c> phải có hai
/// mục cho mỗi thẻ và phải sửa lại mỗi lần chụp lại ảnh.
///
/// Nhờ class này mà toàn bộ test và toàn bộ luồng giao diện chạy được khi đã ngắt mạng, và
/// không tốn một đồng hạn mức nào.
/// </summary>
public sealed class FakeExtractor(ExpectedCards cards) : IExtractor
{
    /// <summary>Tên file không khớp mã thẻ nào — kể cả khi ảnh có thể là danh thiếp thật.</summary>
    public const string UnknownCardReason =
        "Ảnh không khớp mã thẻ mẫu nào trong bộ dữ liệu offline.";

    /// <summary>Thẻ âm tính trong bộ mẫu chưa ghi sẵn lý do thì dùng câu này.</summary>
    public const string NotABusinessCardReason =
        "Ảnh này không phải danh thiếp.";

    private static readonly JsonSerializerOptions RawOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public Task<CardExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        string? languageHint,
        string? sourceName,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // imageBytes, mimeType và languageHint cố ý không dùng: bản cài này không nhìn vào ảnh.
        // Kiểm kích thước và mime là việc của mắt xích Validate trong CardPipeline (T-06b).
        var expected = cards.Find(CardCodeFrom(sourceName));

        return Task.FromResult(expected is null
            ? CardExtractionResult.NotACard(UnknownCardReason)
            : Build(expected));
    }

    /// <summary>
    /// JSON "thô" của bản cài giả được serialize lại từ chính kết quả trên, nên hai đường không
    /// bao giờ lệch nhau. Không có mô hình nào tham gia, nên SG-1 sẽ không bao giờ kích hoạt ở
    /// chế độ fake — đúng và không tránh được: một bản cài giả không tự sinh ra khoá lạ.
    /// Ca G-14 dùng <c>HostileExtractor</c> chính là để bù chỗ đó.
    /// </summary>
    public async Task<string> ExtractRawAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        string? languageHint,
        string? sourceName,
        CancellationToken ct)
    {
        var card = await ExtractAsync(imageBytes, mimeType, languageHint, sourceName, ct);

        // Warnings không thuộc schema mục 4.4, và guard sẽ coi nó là khoá lạ. Bỏ ra trước.
        return JsonSerializer.Serialize(
            new
            {
                isBusinessCard = card.IsBusinessCard,
                rejectReason = card.RejectReason,
                fullName = card.FullName,
                jobTitle = card.JobTitle,
                company = card.Company,
                phones = card.Phones,
                emails = card.Emails,
                website = card.Website,
                address = card.Address,
                detectedLanguage = card.DetectedLanguage,
                searchAlias = card.SearchAlias,
                fieldConfidence = card.FieldConfidence,
            },
            RawOptions);
    }

    /// <summary>
    /// <c>ja-06.jpg</c> → <c>ja-06</c>. Trình duyệt gửi lên cả đường dẫn giả
    /// (<c>C:\fakepath\ja-06.jpg</c>), nên phải cắt cả phần thư mục.
    /// </summary>
    internal static string? CardCodeFrom(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return null;
        }

        // Đổi dấu \ thành / trước khi cắt: trên Linux thì Path không coi \ là dấu phân cách,
        // mà SPEC mục 1 nói dự án phải chạy được cả Linux x64.
        var code = Path.GetFileNameWithoutExtension(sourceName.Replace('\\', '/'));

        return string.IsNullOrWhiteSpace(code) ? null : code;
    }

    private static CardExtractionResult Build(ExpectedCard expected)
    {
        var phones = expected.Phones ?? [];
        var emails = expected.Emails ?? [];

        var card = new CardExtractionResult(
            IsBusinessCard: expected.IsBusinessCard,
            RejectReason: expected.RejectReason ?? DefaultRejectReason(expected),
            FullName: expected.FullName ?? string.Empty,
            JobTitle: expected.JobTitle ?? string.Empty,
            Company: expected.Company ?? string.Empty,
            Phones: phones,
            Emails: emails,
            Website: expected.Website ?? string.Empty,
            Address: expected.Address ?? string.Empty,
            DetectedLanguage: expected.DetectedLanguage ?? string.Empty,
            SearchAlias: expected.SearchAlias ?? string.Empty,
            FieldConfidence: new Dictionary<string, double>(StringComparer.Ordinal));

        return card with { FieldConfidence = expected.FieldConfidence ?? Generate(card) };
    }

    private static string DefaultRejectReason(ExpectedCard expected) =>
        expected.IsBusinessCard ? string.Empty : NotABusinessCardReason;

    /// <summary>
    /// <c>fieldConfidence</c> là **đầu ra của mô hình**, không phải sự thật trên tấm thẻ, nên nó
    /// không nằm trong <c>expected.json</c> mà sinh ở đây: có giá trị → 1,0 · rỗng → 0,0
    /// (SPEC mục 4.1). Mục nào trong <c>expected.json</c> có sẵn thì giá trị đó thắng.
    /// </summary>
    private static Dictionary<string, double> Generate(CardExtractionResult card)
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

        return CardSchema.ConfidenceRequiredFields.ToDictionary(
            field => field,
            field => filled[field] ? 1.0 : 0.0,
            StringComparer.Ordinal);
    }
}
