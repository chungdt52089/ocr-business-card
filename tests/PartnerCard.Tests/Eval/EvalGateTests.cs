using PartnerCard.Eval;
using PartnerCard.Web.Extraction;

namespace PartnerCard.Tests.Eval;

/// <summary>
/// Cổng của T-08 — TEST-SPEC mục 13, ca B-11.
///
/// **Chạy bộ đo ở chế độ <c>fake</c> phải ra đúng 72/72.** <c>FakeExtractor</c> đọc chính
/// <c>expected.json</c>, tức là đáp án đi vào rồi đi ra không đổi, nên bất cứ con số nào khác
/// 100% là **lỗi của bộ đo**, không phải của mô hình.
///
/// Ca này ở đây chứ không phải một lần kiểm bằng tay, vì nếu chỉ kiểm tay thì lần sửa luật so sánh
/// sau — ở T-09, hay khi thêm một thẻ mẫu — sẽ âm thầm làm lệch con số nghiệm thu mà không ai biết.
/// Con số nghiệm thu sai mà trông như đúng đúng là loại hỏng cả dự án được xây để chặn.
/// </summary>
[Trait("Category", "Eval")]
public sealed class EvalGateTests
{
    private static string RealCards => Path.Combine(AppContext.BaseDirectory, "TestData", "realcards");

    private static string ExpectedJson =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "cards", "expected.json");

    [Fact] // B-11
    public async Task Che_do_fake_tren_toan_bo_realcards_phai_ra_72_tren_72()
    {
        var runs = await RunEverything();

        var core = runs.Where(run => run.Role == CardRole.Core).ToList();

        core.Should().HaveCount(18, "18 tấm thẻ tính điểm — ja-01-partial chấm riêng (TEST-SPEC mục 12)");

        var hit = core.Sum(run =>
            run.Fields.Count(field => CardScorer.CoreFields.Contains(field.Field) && field.Matched));

        hit.Should().Be(72, "chế độ fake trả về chính đáp án, nên chênh một điểm cũng là lỗi bộ đo");
    }

    [Fact] // B-11b
    public async Task Moi_truong_phu_cung_phai_khop_o_che_do_fake()
    {
        var runs = await RunEverything();

        var wrong = runs
            .Where(run => run.Role == CardRole.Core)
            .SelectMany(run => run.Fields.Where(field => !field.Matched).Select(field => $"{run.CardCode}.{field.Field}"))
            .ToList();

        wrong.Should().BeEmpty("kể cả website và searchAlias — hai chỗ luật so sánh dễ sai nhất");
    }

    [Fact] // B-11c
    public async Task Luot_do_gom_dung_19_anh_va_ca_chong_bia_dat()
    {
        var runs = await RunEverything();

        runs.Should().HaveCount(19, "SPEC mục 4.6: 19 lời gọi, 18 thẻ tính điểm — hai con số khác nhau");
        runs.Should().OnlyContain(run => run.Called);

        var partial = runs.Single(run => run.Role == CardRole.AntiFabrication);

        partial.CardCode.Should().Be("ja-01-partial");
        partial.SpecialPass.Should().BeTrue();
    }

    private static async Task<IReadOnlyList<CardRun>> RunEverything()
    {
        var images = Directory.EnumerateFiles(RealCards, "*.jpg")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        var runner = new EvalRunner(
            new FakeExtractor(ExpectedCards.LoadFrom(ExpectedJson)),
            ExpectedCards.LoadFrom(ExpectedJson));

        return await runner.RunAsync(images, TimeSpan.Zero, progress: null, CancellationToken.None);
    }
}
