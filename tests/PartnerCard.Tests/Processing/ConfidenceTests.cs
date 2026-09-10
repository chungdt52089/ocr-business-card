using PartnerCard.Web.Processing;

namespace PartnerCard.Tests.Processing;

/// <summary>
/// TEST-SPEC mục 4 — các ca thuộc T-06.
///
/// <c>Confidence</c> kết hợp hai nguồn và **lấy giá trị nhỏ hơn**: điểm mô hình tự báo, và
/// điểm kiểm định dạng. Lấy nhỏ hơn vì mô hình có thể rất tự tin về một email sai cú pháp.
/// </summary>
[Trait("Category", "Confidence")]
public sealed class ConfidenceTests
{
    // ---- C-01, C-02 · email --------------------------------------------------------

    [Fact]
    public void C01_email_hop_le_giu_nguyen_diem_mo_hinh()
    {
        Confidence.ForEmail("m.feld@halbrook-logistics.example", modelScore: 0.95)
            .Should().Be(0.95);
    }

    [Fact]
    public void C02_email_khong_co_a_coi_thi_ve_0_du_mo_hinh_rat_tu_tin()
    {
        Confidence.ForEmail("an.nguyen", modelScore: 0.95).Should().Be(0.0);
    }

    // ---- C-03, C-04, C-07, C-08 · điện thoại --------------------------------------

    [Fact]
    public void C03_so_10_chu_so_giu_nguyen_diem_mo_hinh()
    {
        Confidence.ForPhone("0355501284", modelScore: 0.9).Should().Be(0.9);
    }

    [Fact]
    public void C04_chuoi_4_chu_so_khong_phai_so_dien_thoai_thi_ve_0()
    {
        Confidence.ForPhone("1234", modelScore: 0.9).Should().Be(0.0);
    }

    [Fact]
    public void C07_so_khong_co_ma_quoc_gia_van_duoc_diem_dinh_dang_1()
    {
        // Đây là chỗ đã sửa ngày 08/09. Luật cũ ("chuẩn hoá được sang E.164 = 1,0, giữ nguyên
        // vì không chắc = 0,5") sẽ cho 0,5 ở đây, tức luôn dưới ngưỡng 0,9, tức 6 trong 8 thẻ
        // Nhật luôn bị tô. Tô lúc nào cũng sáng thì không còn là tín hiệu.
        Confidence.FormatScoreForPhone("03-5550-1284").Should().Be(1.0);
        Confidence.ForPhone("03-5550-1284", modelScore: 1.0).Should().Be(1.0);
    }

    [Fact]
    public void C08_so_16_chu_so_vuot_tran_E164_thi_ve_0()
    {
        Confidence.ForPhone("1234567890123456", modelScore: 0.9).Should().Be(0.0);
    }

    [Theory]
    [InlineData("19001234", 8)]              // biên dưới
    [InlineData("0355501284", 10)]
    [InlineData("+15550142887", 11)]
    [InlineData("+442079460331", 12)]
    [InlineData("123456789012345", 15)]      // biên trên
    public void Khoang_8_den_15_chu_so_duoc_diem_1(string phone, int expectedDigits)
    {
        phone.Count(char.IsDigit).Should().Be(expectedDigits, "ca kiểm phải đúng như nó tự nói");

        Confidence.FormatScoreForPhone(phone).Should().Be(1.0);
    }

    [Theory]
    [InlineData("1234567")]                  // 7 chữ số
    [InlineData("1234567890123456")]         // 16 chữ số
    public void Ngoai_khoang_8_den_15_thi_ve_0(string phone)
    {
        Confidence.FormatScoreForPhone(phone).Should().Be(0.0);
    }

    // ---- C-05 · trường rỗng --------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void C05_truong_rong_luon_duoc_0(string? value)
    {
        Confidence.ForEmail(value, modelScore: 1.0).Should().Be(0.0);
        Confidence.ForPhone(value, modelScore: 1.0).Should().Be(0.0);
        Confidence.ForText(value, modelScore: 1.0).Should().Be(0.0);
    }

    // ---- Lấy giá trị nhỏ hơn -------------------------------------------------------

    [Fact]
    public void Luon_lay_gia_tri_nho_hon_giua_hai_nguon()
    {
        // Mô hình thấp, định dạng đạt → lấy điểm mô hình.
        Confidence.ForText("Marcus Feld", modelScore: 0.4).Should().Be(0.4);

        // Mô hình cao, định dạng trượt → lấy điểm định dạng.
        Confidence.ForEmail("khong-phai-email", modelScore: 1.0).Should().Be(0.0);
    }

    // ---- Ngưỡng đánh dấu 0,9 (D-4) -------------------------------------------------

    [Theory]
    [InlineData(0.89, true)]
    [InlineData(0.9, false)]
    [InlineData(0.95, false)]
    [InlineData(0.0, true)]
    public void Nguong_can_xem_lai_la_0_9(double score, bool needsReview)
    {
        Confidence.NeedsReview(score, threshold: 0.9).Should().Be(needsReview);
    }

    // ---- Chấm cả tấm thẻ -----------------------------------------------------------

    [Fact]
    public void Evaluate_cham_du_tam_truong_va_chi_ra_truong_can_xem_lai()
    {
        var card = CardBuilder.Valid() with
        {
            Emails = ["khong-phai-email"],
            FieldConfidence = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["fullName"] = 0.97,
                ["jobTitle"] = 0.80,
                ["company"] = 0.93,
                ["phones"] = 0.99,
                ["emails"] = 0.99,
                ["website"] = 0.95,
                ["address"] = 0.95,
                ["searchAlias"] = 0.0,
            },
        };

        var scored = Confidence.Evaluate(card, threshold: 0.9);

        scored.FieldConfidence["fullName"].Should().Be(0.97);
        scored.FieldConfidence["emails"].Should().Be(0.0, "email sai cú pháp — lấy giá trị nhỏ hơn");
        scored.FieldConfidence["jobTitle"].Should().Be(0.80);

        scored.ReviewFields.Should().Contain(["emails", "jobTitle"]);
        scored.ReviewFields.Should().NotContain("fullName");
    }

    [Fact]
    public void Evaluate_khong_lam_doi_gia_tri_cac_truong()
    {
        var card = CardBuilder.Valid();

        var scored = Confidence.Evaluate(card, threshold: 0.9);

        scored.Card.FullName.Should().Be(card.FullName);
        scored.Card.Emails.Should().BeEquivalentTo(card.Emails);
    }
}
