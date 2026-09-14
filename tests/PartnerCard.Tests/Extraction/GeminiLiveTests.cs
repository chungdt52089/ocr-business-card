using System.Diagnostics;
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
/// Ca này chạy **hai chặng, một lời gọi HTTP**:
/// <list type="number">
/// <item>Gọi <see cref="GeminiExtractor"/> trực tiếp, trong <c>try/catch</c> — để **thấy được
/// exception thật**. <c>CardPipeline</c> cố ý nuốt mọi exception thành kết quả có cấu trúc
/// (SPEC mục 10.3), nên gọi qua đường ống thì mọi nguyên nhân khác nhau đều hiện ra như nhau:
/// một chữ <c>extract_failed</c>.</item>
/// <item>Phát lại kết quả vừa bắt qua **đủ đường ống** bằng <see cref="ReplayExtractor"/> — để
/// biết guard có chặn oan đầu ra của mô hình thật không (SG-3 đòi đủ tám mục confidence, SG-5
/// đòi ký tự số điện thoại). Chặng này không gọi mạng.</item>
/// </list>
///
/// **Ca này gọi mạng và tiêu hạn mức.** Không có <c>GEMINI_API_KEY</c> trong biến môi trường thì
/// nó báo Skipped, nên <c>dotnet test</c> mặc định vẫn xanh khi đã ngắt Wi-Fi (ca I-09).
///
/// <code>
/// $env:GEMINI_API_KEY = "..."
/// dotnet test --filter Category=Live --logger "console;verbosity=detailed"
/// </code>
/// </summary>
[Trait("Category", "Live")]
public sealed class GeminiLiveTests(ITestOutputHelper output)
{
    /// <summary>
    /// Hạn giờ **riêng của ca đo**, mặc định 120 giây.
    ///
    /// Cố ý rộng và cố ý tách khỏi <c>ExtractTimeoutSeconds</c> của ứng dụng: mục đích của ca này
    /// là **biết mô hình thật cần bao lâu**, mà một hạn giờ chặt thì chỉ cho biết "lâu hơn hạn
    /// giờ". Chọn con số cho ứng dụng là việc làm **sau** khi có số đo ổn định.
    /// </summary>
    private const string TimeoutVariable = "GEMINI_LIVE_TIMEOUT_SECONDS";

    private const int DefaultTimeoutSeconds = 120;

    /// <summary>
    /// Ảnh chụp thật, không phải PNG render — PNG quá sạch, kết quả sẽ đẹp giả tạo
    /// (TEST-SPEC mục 12).
    /// </summary>
    private static string CardPath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "realcards", "ja-01.jpg");

    private static int TimeoutSeconds =>
        int.TryParse(Environment.GetEnvironmentVariable(TimeoutVariable), out var seconds) && seconds > 0
            ? seconds
            : DefaultTimeoutSeconds;

    [LiveFact]
    public async Task Trich_duoc_mot_the_Nhat_that_dau_cuoi()
    {
        var image = await File.ReadAllBytesAsync(CardPath);

        var options = Options.Create(new PartnerCardOptions
        {
            Extractor = ExtractorNames.Gemini,

            // Chỉ ảnh hưởng thể hiện dựng ở đây. appsettings.json KHÔNG bị đụng tới.
            ExtractTimeoutSeconds = TimeoutSeconds,
        });

        output.WriteLine($"Ảnh          : {Path.GetFileName(CardPath)}  {image.Length / 1024} KB");
        output.WriteLine($"Hạn giờ ca đo: {TimeoutSeconds}s  (đổi bằng {TimeoutVariable})");
        output.WriteLine($"promptVersion: {Prompts.Version}");
        output.WriteLine($"thinkingLevel: {GeminiExtractor.ThinkingLevel}");
        output.WriteLine(string.Empty);

        // ---- Chặng 1: lời gọi thật, exception nhìn thấy được ----------------------------
        var captured = await CallOnceAsync(image, options);

        // ---- Chặng 2: phát lại qua đủ đường ống, không gọi mạng -------------------------
        var outcome = await ReplayThroughPipelineAsync(captured, image, options);

        Report(outcome);

        outcome.Ok.Should().BeTrue(
            $"đường ống phải đi hết — mã lỗi nhận được: {outcome.ErrorCode ?? "(không)"}");

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

    /// <summary>
    /// Lời gọi thật duy nhất. Bấm giờ **bao ngoài**, độc lập với <c>Usage.LatencyMs</c>: đường
    /// lỗi không có <c>Usage</c>, mà đúng lúc hỏng mới là lúc cần biết nó đã chạy bao lâu.
    /// </summary>
    private async Task<RawExtraction> CallOnceAsync(byte[] image, IOptions<PartnerCardOptions> options)
    {
        var extractor = new GeminiExtractor(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            new SecretOptions { GeminiApiKey = LiveFactAttribute.ApiKey },
            options);

        var started = Stopwatch.GetTimestamp();

        try
        {
            var captured = await extractor.ExtractRawAsync(
                image, "image/jpeg", "ja", "ja-01.jpg", CancellationToken.None);

            output.WriteLine($"Đã trôi      : {ElapsedMs(started)} ms   (lời gọi thật, đo bao ngoài)");
            output.WriteLine($"JSON trả về  : {captured.Json.Length} ký tự");
            output.WriteLine(string.Empty);

            return captured;
        }
        catch (Exception ex)
        {
            ReportFailure(ex, ElapsedMs(started));
            throw;
        }
    }

    /// <summary>Đường ống thật, cắm bản phát lại — không có lời gọi HTTP thứ hai.</summary>
    private static async Task<ExtractOutcome> ReplayThroughPipelineAsync(
        RawExtraction captured, byte[] image, IOptions<PartnerCardOptions> options)
    {
        using var data = new TempDataDirectory();
        using var store = JsonPartnerStore.LoadFrom(data.Path, TimeProvider.System);

        var pipeline = new CardPipeline(
            new ReplayExtractor(captured), new SchemaGuard(), store,
            new RecordingAuditLogger(), options, TimeProvider.System);

        return await pipeline.ExtractAsync(
            Convert.ToBase64String(image), "image/jpeg", "ja", "ja-01.jpg", CancellationToken.None);
    }

    /// <summary>
    /// Chuỗi exception, phân loại nguyên nhân, và siêu dữ liệu phong bì nếu có.
    ///
    /// **Không in nội dung thẻ.** Thông báo của chúng ta chỉ mang siêu dữ liệu (<c>finishReason</c>,
    /// số token, độ dài chuỗi); thông báo của tầng mạng chỉ mang lỗi socket hay TLS.
    /// </summary>
    private void ReportFailure(Exception ex, int elapsedMs)
    {
        output.WriteLine($"HỎNG         : sau {elapsedMs} ms");
        output.WriteLine($"Chuỗi kiểu   : {string.Join(" -> ", Chain(ex).Select(e => e.GetType().Name))}");

        var innermost = Chain(ex).Last();
        output.WriteLine($"Trong cùng   : {innermost.GetType().Name}");
        output.WriteLine($"Message      : {innermost.Message}");

        if (ex is ExtractorException extractor)
        {
            output.WriteLine($"Mã lỗi       : {extractor.Code}");
        }

        // Phân biệt rõ hai họ nguyên nhân — chúng dẫn tới hai hướng xử lý khác hẳn nhau.
        var cause = Chain(ex).Any(e => e is HttpRequestException or IOException)
            ? "MẠNG — không tới được Gemini, hoặc kết nối rơi giữa đường. "
              + "Không phải chuyện prompt hay thinkingLevel."
            : innermost.Message.Contains("không có phần văn bản nào", StringComparison.Ordinal)
                ? "TA NÉM vì phong bì thiếu parts. Đọc finishReason và thoughtsTokenCount ở "
                  + "dòng Message trên: MAX_TOKENS kèm thoughtsTokenCount lớn nghĩa là mô hình "
                  + "nghĩ hết ngân sách output rồi không còn chỗ viết câu trả lời."
                : "KHÁC — xem chuỗi kiểu ở trên.";

        output.WriteLine($"Phân loại    : {cause}");
    }

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static int ElapsedMs(long started) =>
        (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>In ra để người chạy đối chiếu bằng mắt với <c>expected.json</c>.</summary>
    private void Report(ExtractOutcome outcome)
    {
        output.WriteLine($"Ok           : {outcome.Ok}   ErrorCode: {outcome.ErrorCode ?? "(không)"}");
        output.WriteLine($"Số đo        : model={outcome.Usage.Model} prompt={outcome.Usage.PromptVersion} "
                       + $"tokensIn={outcome.Usage.TokensIn} tokensOut={outcome.Usage.TokensOut} "
                       + $"latencyMs={outcome.Usage.LatencyMs}");

        if (outcome.Card is null)
        {
            return;
        }

        var card = outcome.Card;
        output.WriteLine(string.Empty);
        output.WriteLine($"fullName     : {card.FullName}          (kỳ vọng 田中 太郎)");
        output.WriteLine($"jobTitle     : {card.JobTitle}          (kỳ vọng 営業部長)");
        output.WriteLine($"company      : {card.Company}           (kỳ vọng 株式会社青葉精工)");
        output.WriteLine($"phones       : {string.Join(" | ", card.Phones)}");
        output.WriteLine($"emails       : {string.Join(" | ", card.Emails)}");
        output.WriteLine($"website      : {card.Website}");
        output.WriteLine($"address      : {card.Address}");
        output.WriteLine($"detectedLang : {card.DetectedLanguage}  (kỳ vọng ja)");
        output.WriteLine($"searchAlias  : {card.SearchAlias}");
        output.WriteLine(string.Empty);
        output.WriteLine("fieldConfidence:");

        foreach (var (field, score) in card.FieldConfidence.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {field,-13}: {score}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"Cần xem lại  : {string.Join(", ", outcome.ReviewFields)}");
        output.WriteLine($"Guard warning: {string.Join(", ", outcome.Warnings.Select(w => $"{w.Code}/{w.Field}"))}");
        output.WriteLine($"Normalizer   : {string.Join(", ", card.Warnings)}");
    }
}
