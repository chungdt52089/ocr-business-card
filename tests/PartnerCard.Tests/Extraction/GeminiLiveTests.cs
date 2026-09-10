using Microsoft.Extensions.Options;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;
using Xunit.Abstractions;

namespace PartnerCard.Tests.Extraction;

/// <summary>
/// Điều kiện "Xong khi" của T-07: **trích được một thẻ Nhật thật đầu-cuối.**
///
/// Đi qua đủ <see cref="CardPipeline"/> chứ không gọi thẳng extractor, vì câu hỏi thật sự là
/// *"guard có chặn oan đầu ra của mô hình thật không"* — SG-3 đòi đủ tám mục confidence, SG-5
/// đòi số điện thoại chỉ chứa ký tự cho phép. Gọi riêng extractor thì không kiểm được điều đó.
///
/// **Ca này gọi mạng và tiêu hạn mức.** Không có <c>GEMINI_API_KEY</c> trong biến môi trường thì
/// nó báo Skipped, nên <c>dotnet test</c> mặc định vẫn xanh khi đã ngắt Wi-Fi (ca I-09).
///
/// <code>
/// $env:GEMINI_API_KEY = "..."
/// dotnet test --filter Category=Live
/// </code>
/// </summary>
[Trait("Category", "Live")]
public sealed class GeminiLiveTests(ITestOutputHelper output)
{
    /// <summary>
    /// Ảnh chụp thật, không phải PNG render — PNG quá sạch, kết quả sẽ đẹp giả tạo
    /// (TEST-SPEC mục 12).
    /// </summary>
    private static string CardPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "realcards", "ja-01.jpg");

    [LiveFact]
    public async Task Trich_duoc_mot_the_Nhat_that_dau_cuoi()
    {
        using var data = new TempDataDirectory();
        using var store = JsonPartnerStore.LoadFrom(data.Path, TimeProvider.System);

        var options = Options.Create(new PartnerCardOptions
        {
            Extractor = ExtractorNames.Gemini,
            DataDirectory = data.Path,
        });

        var extractor = new GeminiExtractor(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            new SecretOptions { GeminiApiKey = LiveFactAttribute.ApiKey },
            options);

        var pipeline = new CardPipeline(
            extractor, new SchemaGuard(), store, new RecordingAuditLogger(),
            options, TimeProvider.System);

        var outcome = await pipeline.ExtractAsync(
            Convert.ToBase64String(await File.ReadAllBytesAsync(CardPath)),
            "image/jpeg", "ja", "ja-01.jpg", CancellationToken.None);

        Report(outcome);

        outcome.Ok.Should().BeTrue($"đường ống phải đi hết; mã lỗi nhận được: {outcome.ErrorCode}");

        var card = outcome.Card!;
        card.IsBusinessCard.Should().BeTrue();
        card.FullName.Should().NotBeEmpty();
        card.Company.Should().NotBeEmpty();
        card.Phones.Should().NotBeEmpty();
        card.DetectedLanguage.Should().NotBeEmpty();

        // Số đo phải có thật — 0 token nghĩa là usageMetadata đọc sai chỗ.
        outcome.Usage.TokensIn.Should().BeGreaterThan(0);
        outcome.Usage.LatencyMs.Should().BeGreaterThan(0);
        outcome.Usage.PromptVersion.Should().Be(Prompts.Version);

        // Chấm 1.0 tất tay nghĩa là điều 6 của prompt chưa ăn — màn hình sẽ không bao giờ tô vàng.
        // Đây chỉ là ghi chú để đọc, không phải điều kiện đỏ: T-08 mới là chỗ chấm việc đó.
        if (card.FieldConfidence.Values.All(score => score >= 1.0))
        {
            output.WriteLine(string.Empty);
            output.WriteLine("CẢNH BÁO: mô hình trả 1.0 cho MỌI trường. Xem lại điều 6 của prompt.");
        }
    }

    /// <summary>In ra để người chạy đối chiếu bằng mắt với `expected.json`.</summary>
    private void Report(ExtractOutcome outcome)
    {
        output.WriteLine($"Ok            : {outcome.Ok}   ErrorCode: {outcome.ErrorCode ?? "(không)"}");
        output.WriteLine($"Số đo         : model={outcome.Usage.Model} prompt={outcome.Usage.PromptVersion} "
                       + $"tokensIn={outcome.Usage.TokensIn} tokensOut={outcome.Usage.TokensOut} "
                       + $"latencyMs={outcome.Usage.LatencyMs}");

        if (outcome.Card is null)
        {
            return;
        }

        var card = outcome.Card;
        output.WriteLine(string.Empty);
        output.WriteLine($"fullName      : {card.FullName}          (kỳ vọng 田中 太郎)");
        output.WriteLine($"jobTitle      : {card.JobTitle}          (kỳ vọng 営業部長)");
        output.WriteLine($"company       : {card.Company}           (kỳ vọng 株式会社青葉精工)");
        output.WriteLine($"phones        : {string.Join(" | ", card.Phones)}");
        output.WriteLine($"emails        : {string.Join(" | ", card.Emails)}");
        output.WriteLine($"website       : {card.Website}");
        output.WriteLine($"address       : {card.Address}");
        output.WriteLine($"detectedLang  : {card.DetectedLanguage}  (kỳ vọng ja)");
        output.WriteLine($"searchAlias   : {card.SearchAlias}");
        output.WriteLine(string.Empty);
        output.WriteLine("fieldConfidence:");

        foreach (var (field, score) in card.FieldConfidence.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {field,-13}: {score}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"Cần xem lại   : {string.Join(", ", outcome.ReviewFields)}");
        output.WriteLine($"Guard warnings: {string.Join(", ", outcome.Warnings.Select(w => $"{w.Code}/{w.Field}"))}");
        output.WriteLine($"Normalizer    : {string.Join(", ", card.Warnings)}");
    }
}
