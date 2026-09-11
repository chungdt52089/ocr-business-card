using PartnerCard.Eval;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Eval;

/// <summary>
/// Hai mẫu số của điểm bốn trường bắt buộc — TEST-SPEC mục 13, SPEC mục 14.3.
///
/// Mỗi con số một mình dẫn tới một hiểu sai **ngược nhau**, nên báo cáo in cả hai khi có thẻ không
/// gọi được: bỏ thẻ hỏng khỏi mẫu số thì tỷ lệ đẹp lên đúng vì lượt đo hỏng nhiều hơn; còn đọc mỗi
/// con số nghiệm thu thì lại tưởng mô hình đọc kém trong khi thật ra mạng rớt.
/// </summary>
[Trait("Category", "Eval")]
public sealed class CoreScoreTests
{
    [Fact] // B-15
    public void Hai_mau_so_khac_nhau_khi_co_the_khong_goi_duoc()
    {
        // 4 thẻ tính điểm: 3 thẻ đúng cả 4 trường, 1 thẻ không có kết quả.
        var score = CoreScore.From(
        [
            Scored("en-01", matched: 4),
            Scored("en-02", matched: 4),
            Scored("en-03", matched: 4),
            Broken("ja-05", "extract_unavailable"),
        ]);

        score.Hit.Should().Be(12);

        score.Total.Should().Be(16, "mẫu số nghiệm thu giữ nguyên mọi thẻ, kể cả thẻ hỏng");
        score.Percent.Should().BeApproximately(75.0, 0.01);

        score.CalledTotal.Should().Be(12, "3 thẻ gọi được × 4 trường");
        score.CalledPercent.Should().BeApproximately(100.0, 0.01);

        score.BrokenCards.Should().Be(1);
    }

    [Fact] // B-15b
    public void Luot_do_tron_ven_thi_hai_mau_so_bang_nhau()
    {
        var score = CoreScore.From([Scored("en-01", matched: 4), Scored("en-02", matched: 3)]);

        score.BrokenCards.Should().Be(0, "không có thẻ hỏng thì báo cáo không in dòng phụ");
        score.Total.Should().Be(score.CalledTotal);
        score.Hit.Should().Be(7);
    }

    [Fact] // B-15c
    public void The_bi_bo_qua_vi_het_han_muc_cung_nam_trong_mau_so_nghiem_thu()
    {
        // Thẻ skipped chưa từng chạm tới mô hình, nhưng nó vẫn là một tấm thẻ của bộ mẫu — bỏ nó
        // khỏi mẫu số 72 thì con số nghiệm thu đo trên một bộ mẫu nhỏ hơn mà không nói ra.
        var score = CoreScore.From([Scored("en-01", matched: 4), Skipped("ja-08")]);

        score.Total.Should().Be(8);
        score.CalledTotal.Should().Be(4);
        score.BrokenCards.Should().Be(1);
    }

    [Fact] // B-15d
    public void Dong_phu_chi_hien_trong_EVAL_khi_co_the_khong_goi_duoc()
    {
        const string Marker = "chỉ thẻ có kết quả";

        Build([Scored("en-01", matched: 4)])
            .Should().NotContain(Marker);

        Build([Scored("en-01", matched: 4), Broken("ja-05", "extract_timeout")])
            .Should().Contain(Marker).And.Contain("← con số nghiệm thu");
    }

    [Fact] // B-16
    public void Dong_dem_mang_ca_hai_nhan_vi_file_va_the_tinh_diem_la_hai_tap_khac_nhau()
    {
        // 2 thẻ tính điểm + ja-01-partial: 3 file nhưng chỉ 2 tấm cộng vào điểm.
        var block = Build(
        [
            Scored("en-01", matched: 4),
            Scored("ja-01", matched: 4),
            Partial("ja-01-partial"),
        ]);

        block.Should().Contain("3/3 file gọi được · 2/2 thẻ tính điểm",
            "hai con số đếm hai tập khác nhau nên cả hai phải mang nhãn");

        block.Should().Contain("3 file ≠ 2 thẻ tính điểm")
            .And.Contain("`ja-01-partial` (ca chống bịa)",
                "nhãn phải nói được tấm nào là phần chênh và vì sao nó ngoài 72 điểm");
    }

    private static string Build(IReadOnlyList<CardRun> runs) =>
        MarkdownReport.Build(
            new ReportContext("fake", "-", "-", "realcards", true, false, TimeSpan.Zero, DateTimeOffset.UnixEpoch),
            runs);

    private static CardRun Scored(string code, int matched) =>
        new(code, code + ".jpg", CardScorer.GroupOf(code), CardRole.Core)
        {
            Card = CardBuilder.Valid(),
            Fields = [.. CardScorer.CoreFields.Select((field, i) =>
                new FieldMatch(field, i < matched, i < matched, "x", "x"))],
        };

    private static CardRun Partial(string code) =>
        new(code, code + ".jpg", CardScorer.GroupOf(code), CardRole.AntiFabrication)
        {
            Card = CardBuilder.Valid() with { Emails = [], Website = string.Empty },
            SpecialPass = true,
        };

    private static CardRun Broken(string code, string errorCode) =>
        new(code, code + ".jpg", CardScorer.GroupOf(code), CardRole.Core) { ErrorCode = errorCode };

    private static CardRun Skipped(string code) =>
        new(code, code + ".jpg", CardScorer.GroupOf(code), CardRole.Core)
        {
            ErrorCode = "skipped_quota",
            Skipped = true,
        };
}
