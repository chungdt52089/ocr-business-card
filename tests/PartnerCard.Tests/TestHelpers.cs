using PartnerCard.Web.Models;

namespace PartnerCard.Tests;

/// <summary>Thư mục dữ liệu tạm, dọn sạch khi ca kiểm thử xong. Test không bao giờ đụng <c>Code\data\</c>.</summary>
public sealed class TempDataDirectory : IDisposable
{
    public TempDataDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "partnercard-tests", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string PartnersFile => System.IO.Path.Combine(Path, "partners.json");

    public string CounterFile => System.IO.Path.Combine(Path, "counter.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Dọn dẹp thất bại không phải lý do để một ca kiểm thử đỏ.
        }
    }
}

/// <summary>
/// Đồng hồ nhích một giây sau mỗi lần đọc. Nhờ vậy hai lần lưu liên tiếp có mốc thời gian
/// khác nhau, nên thứ tự "mới nhất" (S-07) và ca S-05 kiểm được dứt khoát, không phụ thuộc
/// tốc độ máy.
/// </summary>
public sealed class SteppingClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        var current = _now;
        _now = _now.AddSeconds(1);
        return current;
    }
}

/// <summary>Dựng <see cref="Partner"/> hợp lệ để ca kiểm thử chỉ phải nêu thứ nó quan tâm.</summary>
public static class PartnerFactory
{
    public static Partner New(
        string fullName = "Marcus Feld",
        string company = "Halbrook Logistics",
        string? email = "m.feld@halbrook-logistics.example",
        string partnerId = "") =>
        new(
            PartnerId: partnerId,
            FullName: fullName,
            JobTitle: "Operations Director",
            Company: company,
            Phones: ["+15550142887"],
            Emails: email is null ? [] : [email],
            Website: "https://halbrook-logistics.example",
            Address: "418 Kestrel Avenue, Suite 12, Portland OR 97205",
            DetectedLanguage: "en",
            SearchAlias: null,
            Industry: null,
            ShortDescription: null,
            MainProducts: [],
            EnrichmentSourceUrl: null,
            EnrichedAt: null,
            SourceImage: "images/abc123.jpg",
            ImageSha256: "abc123",
            FieldConfidence: new Dictionary<string, double> { ["fullName"] = 1.0 },
            EditedFields: [],
            Extraction: new ExtractionMeta("fake", "v1", DateTimeOffset.UnixEpoch, 0),
            Status: PartnerStatus.Confirmed,
            CreatedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch);
}
