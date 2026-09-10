using PartnerCard.Eval;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Eval;

/// <summary>
/// Luật so sánh của bộ đo — TEST-SPEC mục 13, nhóm B.
///
/// Nhóm này tồn tại vì một lượt chạy ở chế độ <c>fake</c> **không đi qua** những đường này: fake
/// trả về đúng đáp án nên mọi trường luôn khớp, và không ca sai nào được thực thi. Muốn tin con số
/// nghiệm thu thì phải biết bộ đo cũng **bắt được cái sai**, không chỉ xác nhận cái đúng.
/// </summary>
[Trait("Category", "Eval")]
public sealed class CardScorerTests
{
    [Fact] // B-01
    public void Phones_va_emails_so_theo_tap_hop_nen_dao_thu_tu_van_dung()
    {
        var expected = Expected() with
        {
            Phones = ["+1 555 0142 887", "03-5550-1284"],
            Emails = ["A@x.example", "b@x.example"],
        };

        var actual = Card() with
        {
            Phones = ["03-5550-1284", "+1 555 0142 887"],
            Emails = ["b@x.example", "a@x.example"],
        };

        var fields = CardScorer.Score(expected, actual);

        fields.Single(f => f.Field == "phones").Matched.Should().BeTrue();
        fields.Single(f => f.Field == "emails").Matched.Should().BeTrue();
    }

    [Fact] // B-02
    public void Thieu_mot_phan_tu_trong_phones_tinh_la_sai()
    {
        var expected = Expected() with { Phones = ["03-5550-1284", "03-5550-1285"] };
        var actual = Card() with { Phones = ["03-5550-1284"] };

        CardScorer.Score(expected, actual).Single(f => f.Field == "phones").Matched.Should().BeFalse();
    }

    [Fact] // B-03
    public void Truong_chu_khop_sau_khi_gop_khoang_trang_va_bo_dau_cham_cuoi()
    {
        var expected = Expected() with { Company = "Halbrook Logistics" };
        var actual = Card() with { Company = "Halbrook  Logistics." };

        CardScorer.Score(expected, actual).Single(f => f.Field == "company").Matched.Should().BeTrue();
    }

    [Fact] // B-03b
    public void Sai_dau_thi_truot_o_cot_chinh_nhung_khop_o_cot_bo_dau()
    {
        var expected = Expected() with { FullName = "Tomás Herrera" };
        var actual = Card() with { FullName = "Tomas Herrera" };

        var match = CardScorer.Score(expected, actual).Single(f => f.Field == "fullName");

        match.Matched.Should().BeFalse();
        match.MatchedLoose.Should().BeTrue("cột \"khớp sau khi bỏ dấu\" phải chỉ ra đây chỉ là lỗi dấu");
    }

    [Fact] // B-04 — cái bẫy Normalizer.Website
    public void Website_khop_vi_Normalizer_chay_len_ca_hai_ve()
    {
        // expected.json lưu tên miền trần; mô hình thật hay trả kèm scheme và www.
        var expected = Expected() with { Website = "halbrook-logistics.example" };
        var actual = Card() with { Website = "https://www.halbrook-logistics.example/" };

        CardScorer.Score(expected, actual).Single(f => f.Field == "website").Matched.Should().BeTrue();
    }

    [Fact] // B-04b
    public void Website_khac_ten_mien_thi_van_sai()
    {
        var expected = Expected() with { Website = "halbrook-logistics.example" };
        var actual = Card() with { Website = "https://halbrook.example" };

        CardScorer.Score(expected, actual).Single(f => f.Field == "website").Matched.Should().BeFalse();
    }

    [Fact] // B-09
    public void Ca_chong_bia_dat_khi_emails_va_website_rong()
    {
        var blank = Card() with { Emails = [], Website = string.Empty };
        var fabricated = Card() with { Emails = [], Website = "https://aoba-seiko.example" };

        CardScorer.AntiFabricationPasses(blank).Should().BeTrue();
        CardScorer.AntiFabricationPasses(fabricated).Should()
            .BeFalse("suy ra website từ tên miền không nhìn thấy chính là thứ ca này bắt");
    }

    [Fact] // B-09b
    public void Ja_01_partial_mang_vai_chong_bia_chu_khong_cong_vao_72_diem()
    {
        var answers = ExpectedCards.LoadFrom(
            Path.Combine(AppContext.BaseDirectory, "TestData", "cards", "expected.json"));

        CardScorer.RoleOf("ja-01-partial", answers.Find("ja-01-partial")).Should().Be(CardRole.AntiFabrication);
        CardScorer.RoleOf("ja-01", answers.Find("ja-01")).Should().Be(CardRole.Core);
        CardScorer.RoleOf("neg-01", answers.Find("neg-01")).Should().Be(CardRole.Negative);
        CardScorer.RoleOf("khong-co-that", answers.Find("khong-co-that")).Should().Be(CardRole.NoAnswer);
    }

    [Fact] // B-10
    public void Min_va_trung_vi_chi_tinh_tren_truong_co_gia_tri()
    {
        // jobTitle rỗng nên điểm 0,0 của nó không được kéo min xuống — nếu kéo thì min luôn bằng 0
        // với mọi tấm thẻ và cả hai con số mất sạch ý nghĩa.
        var card = Card() with
        {
            JobTitle = string.Empty,
            FieldConfidence = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["fullName"] = 1.0,
                ["jobTitle"] = 0.0,
                ["company"] = 0.6,
            },
        };

        var values = CardScorer.FilledConfidences(card);

        values.Should().BeEquivalentTo([1.0, 0.6]);
        values.Min().Should().Be(0.6);
    }

    [Fact] // B-12
    public void Bi_01_va_bi_02_thuoc_nhom_Nhat()
    {
        CardScorer.GroupOf("en-01").Should().Be(CardGroup.English);
        CardScorer.GroupOf("ja-01").Should().Be(CardGroup.Japanese);
        CardScorer.GroupOf("bi-01").Should().Be(CardGroup.Japanese);
    }

    [Fact] // B-13
    public void SearchAlias_phai_chua_du_ba_phan_phien_am()
    {
        var expected = Expected() with
        {
            FullNameLatin = "Tanaka Taro",
            CompanyLatin = "Aoba Seiko",
            CityLatin = "Tokyo Chiyoda",
        };

        var full = Card() with { SearchAlias = "Tanaka Taro Aoba Seiko Tokyo Chiyoda" };
        var missingCity = Card() with { SearchAlias = "Tanaka Taro Aoba Seiko" };

        CardScorer.Score(expected, full).Single(f => f.Field == "searchAlias").Matched.Should().BeTrue();
        CardScorer.Score(expected, missingCity).Single(f => f.Field == "searchAlias").Matched.Should().BeFalse();
    }

    private static ExpectedCard Expected() => new(
        IsBusinessCard: true,
        FullName: "Marcus Feld",
        JobTitle: "Operations Director",
        Company: "Halbrook Logistics",
        Phones: ["+1 555 0142 887"],
        Emails: ["m.feld@halbrook-logistics.example"],
        Website: "halbrook-logistics.example",
        Address: "418 Kestrel Avenue",
        DetectedLanguage: "en",
        SearchAlias: null);

    private static CardExtractionResult Card() => CardBuilder.Valid() with
    {
        Address = "418 Kestrel Avenue",
        Website = "halbrook-logistics.example",
    };
}
