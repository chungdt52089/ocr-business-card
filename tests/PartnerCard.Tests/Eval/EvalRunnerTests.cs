using PartnerCard.Eval;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Eval;

/// <summary>
/// Hành vi vòng lặp đo — TEST-SPEC mục 13.
///
/// Ba yêu cầu ở đây đến từ bốn lượt gọi thật ngày 10/09, trong đó **hai lượt trả <c>503</c>**.
/// Không ca nào trong nhóm này chạy được ở chế độ <c>fake</c> thường: fake không bao giờ ném, nên
/// đường xử lý lỗi chỉ được kiểm ở đây.
/// </summary>
[Trait("Category", "Eval")]
public sealed class EvalRunnerTests
{
    private static readonly string CardsDirectory =
        Path.Combine(AppContext.BaseDirectory, "TestData", "realcards");

    private static ExpectedCards Answers => ExpectedCards.LoadFrom(
        Path.Combine(AppContext.BaseDirectory, "TestData", "cards", "expected.json"));

    [Fact] // B-05
    public async Task Mot_the_hong_khong_lam_sap_ca_luot_do()
    {
        var extractor = new ScriptedExtractor { FailWith = { ["ja-05"] = new ExtractorUnavailableException() } };
        var runs = await Run(extractor, "en-01.jpg", "ja-05.jpg", "ja-06.jpg");

        extractor.Calls.Should().Be(3, "lượt đo phải đi hết danh sách chứ không dừng ở thẻ hỏng");
        runs.Count(run => run.Called).Should().Be(2);

        var broken = runs.Single(run => !run.Called);
        broken.CardCode.Should().Be("ja-05");
        broken.ErrorCode.Should().Be("extract_unavailable");
        broken.Skipped.Should().BeFalse("nó đã thực sự được gọi, chỉ là gọi hỏng");
    }

    [Fact] // B-06
    public async Task Het_han_muc_thi_dung_han_va_khong_goi_them_lan_nao()
    {
        var extractor = new ScriptedExtractor { FailWith = { ["ja-05"] = new ExtractorQuotaException() } };
        var runs = await Run(extractor, "en-01.jpg", "ja-05.jpg", "ja-06.jpg", "ja-07.jpg");

        extractor.Calls.Should().Be(2,
            "429 kéo dài tới khi hạn mức reset, nên gọi tiếp chỉ tiêu thêm request của hạn mức đã cạn");

        runs.Single(run => run.CardCode == "ja-05").ErrorCode.Should().Be("quota_exhausted");

        var skipped = runs.Where(run => run.Skipped).ToList();
        skipped.Select(run => run.CardCode).Should().Equal("ja-06", "ja-07");
        skipped.Should().OnlyContain(run => run.ErrorCode == "skipped_quota");
    }

    [Fact] // B-06b
    public async Task Khoa_hong_cung_dung_han_vi_khong_tam_nao_dung_duoc()
    {
        var extractor = new ScriptedExtractor { FailWith = { ["en-01"] = new ExtractorAuthException() } };
        var runs = await Run(extractor, "en-01.jpg", "ja-06.jpg");

        extractor.Calls.Should().Be(1);
        runs.Single(run => run.Skipped).ErrorCode.Should().Be("skipped_auth");
    }

    [Fact] // B-07
    public async Task Chay_duoc_mot_tap_con_de_thu_bo_do_bang_ba_request()
    {
        var extractor = new ScriptedExtractor();

        await Run(extractor, "en-01.jpg", "ja-03.jpg", "bi-01.jpg");

        extractor.Calls.Should().Be(3, "cả bộ có 19 ảnh; --cards phải cắt xuống đúng số đã nêu");
    }

    [Fact] // B-05b
    public async Task JSON_khong_deserialize_duoc_thanh_bad_json_chu_khong_nem_ra_ngoai()
    {
        var extractor = new ScriptedExtractor { RawJson = "{ khong phai json }" };
        var runs = await Run(extractor, "en-01.jpg");

        runs.Single().ErrorCode.Should().Be("bad_json");
    }

    [Fact] // B-08
    public void P50_va_p95_dung_nearest_rank_nen_luon_tra_ve_mot_gia_tri_da_do_duoc()
    {
        int[] values = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

        Stats.Percentile(values, 0.50).Should().Be(50);
        Stats.Percentile(values, 0.95).Should().Be(100);
        Stats.Percentile([], 0.95).Should().Be(0);
        Stats.Percentile([42], 0.50).Should().Be(42);
    }

    [Fact] // B-08b
    public void Trung_vi_lay_trung_binh_hai_gia_tri_giua_khi_so_phan_tu_chan()
    {
        Stats.Median([1.0, 0.6]).Should().Be(0.8);
        Stats.Median([1.0, 0.6, 0.2]).Should().Be(0.6);
        Stats.Median([]).Should().Be(0);
    }

    private static async Task<IReadOnlyList<CardRun>> Run(IExtractor extractor, params string[] fileNames)
    {
        var paths = fileNames.Select(name => Path.Combine(CardsDirectory, name)).ToList();

        return await new EvalRunner(extractor, Answers)
            .RunAsync(paths, TimeSpan.Zero, progress: null, CancellationToken.None);
    }

    /// <summary>
    /// Trả kết quả rỗng cho mọi tấm, trừ những mã thẻ được dặn ném. Đếm số lời gọi, vì ca B-06
    /// không hỏi "kết quả là gì" mà hỏi **"có gọi thêm lần nào nữa không"**.
    /// </summary>
    private sealed class ScriptedExtractor : IExtractor
    {
        public Dictionary<string, Exception> FailWith { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? RawJson { get; init; }

        public int Calls { get; private set; }

        public Task<RawExtraction> ExtractRawAsync(
            ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
            string? sourceName, CancellationToken ct)
        {
            Calls++;

            var code = Path.GetFileNameWithoutExtension(sourceName ?? string.Empty);

            if (FailWith.TryGetValue(code, out var failure))
            {
                throw failure;
            }

            var json = RawJson ?? CardJson.Serialize(CardExtractionResult.NotACard("ca kiểm thử"));

            return Task.FromResult(new RawExtraction(json, new ExtractionUsage(11, 22, 33, "test", "v0")));
        }

        public Task<CardExtractionResult> ExtractAsync(
            ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
            string? sourceName, CancellationToken ct) =>
            throw new NotSupportedException("Bộ đo phải đi qua ExtractRawAsync để lấy được số token.");
    }
}
