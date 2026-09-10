using PartnerCard.Web.Processing;

namespace PartnerCard.Tests.Processing;

/// <summary>
/// TEST-SPEC mục 3 — các ca thuộc T-05.
///
/// Phạm vi Anh–Nhật: quy tắc số điện thoại rất hẹp, **không suy đoán mã quốc gia** (SPEC mục 5.1).
/// </summary>
[Trait("Category", "Normalize")]
public sealed class NormalizerTests
{
    // ---- N-01 → N-04 · số điện thoại ----------------------------------------------

    [Theory]
    [InlineData("+1 555 0142 887", "+15550142887")]      // N-01
    [InlineData("03-5550-1284", "0355501284")]           // N-02 — KHÔNG thêm +81
    [InlineData("(028) 3822 1100", "02838221100")]       // N-03
    [InlineData("+81-3-5550-2277", "+81355502277")]      // N-04
    public void N01_N04_so_dien_thoai_chi_bo_ky_tu_phan_cach(string input, string expected)
    {
        Normalizer.Phone(input).Should().Be(expected);
    }

    [Fact]
    public void N02_khong_bao_gio_them_ma_quoc_gia_cho_so_noi_dia_nhat()
    {
        // Thêm +81 vào số Nhật là làm hỏng dữ liệu, không phải làm sạch: người Nhật vẫn có
        // danh thiếp tiếng Anh, và người Anh vẫn có số bắt đầu bằng 0.
        Normalizer.Phone("03-5550-1284").Should().NotStartWith("+");
    }

    // ---- N-05 → N-08 · email, website, địa chỉ ------------------------------------

    [Fact]
    public void N05_email_ve_chu_thuong_va_cat_khoang_trang()
    {
        Normalizer.Email("  An.Nguyen@ABC.example ").Should().Be("an.nguyen@abc.example");
    }

    [Theory]
    [InlineData("abc.example", "https://abc.example")]                  // N-06
    [InlineData("http://www.abc.example/", "https://abc.example")]      // N-07
    [InlineData("https://abc.example/gioi-thieu", "https://abc.example/gioi-thieu")]
    public void N06_N07_website_them_scheme_bo_www_bo_gach_cuoi(string input, string expected)
    {
        Normalizer.Website(input).Should().Be(expected);
    }

    [Fact]
    public void N08_dia_chi_gop_khoang_trang_va_bo_xuong_dong()
    {
        var input = "418 Kestrel Avenue,\r\n  Suite 12,\n\tPortland   OR 97205";

        Normalizer.Address(input).Should().Be("418 Kestrel Avenue, Suite 12, Portland OR 97205");
    }

    // ---- N-09 → N-11, N-14, N-15 · companyKey -------------------------------------

    [Theory]
    [InlineData("ABC Trading Co., Ltd.", "abc trading")]   // N-09
    [InlineData("株式会社青葉精工", "青葉精工")]              // N-10 — 前株, chỉ danh đứng TRƯỚC
    [InlineData("有限会社鈴木鋼材", "鈴木鋼材")]              // N-11
    [InlineData("青葉精工株式会社", "青葉精工")]              // N-14 — 後株, chỉ danh đứng SAU
    [InlineData("Ironvale Precision Ltd.", "ironvale precision")]
    [InlineData("Nordfell Marine AB", "nordfell marine ab")]
    [InlineData("Công ty TNHH Thương mại ABC", "thuong mai abc")]
    public void N09_N14_companyKey_bo_chi_danh_phap_nhan_o_ca_hai_vi_tri(string input, string expected)
    {
        TextKeys.CompanyKey(input).Should().Be(expected);
    }

    [Fact]
    public void N10_va_N14_cung_mot_cong_ty_phai_ra_cung_mot_khoa()
    {
        // Đây là điểm của cả luật: 前株 và 後株 là cùng một công ty, chỉ khác thói quen đăng ký.
        // Luật chỉ cắt cuối chuỗi sẽ trượt N-10; luật chỉ cắt đầu sẽ trượt N-14.
        TextKeys.CompanyKey("株式会社青葉精工")
            .Should().Be(TextKeys.CompanyKey("青葉精工株式会社"));
    }

    [Fact]
    public void N15_chuoi_chi_co_chi_danh_tra_ve_rong_khong_nem_exception()
    {
        TextKeys.CompanyKey("株式会社").Should().BeEmpty();
    }

    // ---- N-12 · đầu vào rỗng -------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void N12_truong_null_hoac_rong_tra_rong_khong_nem_exception(string? input)
    {
        Normalizer.Phone(input).Should().BeEmpty();
        Normalizer.Email(input).Should().BeEmpty();
        Normalizer.Website(input).Should().BeEmpty();
        Normalizer.Address(input).Should().BeEmpty();
        TextKeys.NameKey(input).Should().BeEmpty();
        TextKeys.CompanyKey(input).Should().BeEmpty();
    }

    // ---- N-13 · khoá so sánh không được sửa giá trị gốc ----------------------------

    [Fact]
    public void N13_sinh_khoa_so_sanh_khong_lam_doi_gia_tri_goc_trong_ho_so()
    {
        var card = CardBuilder.Valid() with
        {
            FullName = "Nguyễn Văn An",
            Company = "株式会社青葉精工",
        };

        var normalized = Normalizer.Normalize(card);

        // Khoá là dạng bỏ dấu và đã cắt chỉ danh…
        normalized.NameKey.Should().Be("nguyen van an");
        normalized.CompanyKey.Should().Be("青葉精工");

        // …còn hồ sơ vẫn giữ nguyên từng ký tự.
        normalized.Card.FullName.Should().Be("Nguyễn Văn An");
        normalized.Card.Company.Should().Be("株式会社青葉精工");
    }

    [Fact]
    public void NameKey_bo_dau_tieng_viet_de_go_khong_dau_van_tim_duoc()
    {
        TextKeys.NameKey("Nguyễn Văn An").Should().Be("nguyen van an");
        TextKeys.NameKey("Trần  Đức  Đạt").Should().Be("tran duc dat");
    }

    [Fact]
    public void NameKey_khong_duoc_bo_dau_duc_cua_katakana()
    {
        // Dấu đục cũng là NonSpacingMark như dấu tiếng Việt, nên luật bỏ dấu viết ẩu sẽ nuốt nó.
        // トヨダ thành トヨタ là một công ty khác — đó là làm hỏng dữ liệu, không phải chuẩn hoá.
        TextKeys.NameKey("株式会社トヨダ").Should().Be("株式会社トヨダ");
        TextKeys.CompanyKey("株式会社トヨダ").Should().Be("トヨダ");
    }

    // ---- unnormalizedPhone · ghi chú, không phải điểm trừ (SPEC 5.1) --------------

    [Fact]
    public void So_khong_co_ma_quoc_gia_sinh_ghi_chu_unnormalizedPhone()
    {
        var card = CardBuilder.Valid() with { Phones = ["03-5550-1284"] };

        var normalized = Normalizer.Normalize(card);

        normalized.UnnormalizedPhones.Should().ContainSingle().Which.Should().Be("0355501284");
        normalized.Card.Warnings.Should().Contain(Normalizer.UnnormalizedPhoneWarning);
    }

    [Fact]
    public void So_co_ma_quoc_gia_thi_khong_sinh_ghi_chu()
    {
        var card = CardBuilder.Valid() with { Phones = ["+81-3-5550-2277"] };

        var normalized = Normalizer.Normalize(card);

        normalized.UnnormalizedPhones.Should().BeEmpty();
        normalized.Card.Warnings.Should().NotContain(Normalizer.UnnormalizedPhoneWarning);
    }

    [Fact]
    public void Normalize_ap_dung_luat_5_1_va_5_2_len_gia_tri_luu()
    {
        var card = CardBuilder.Valid() with
        {
            Phones = ["+1 555 0142 887"],
            Emails = ["  M.Feld@Halbrook-Logistics.Example "],
            Website = "www.halbrook-logistics.example/",
            Address = "418 Kestrel Avenue,\n  Suite 12",
        };

        var normalized = Normalizer.Normalize(card).Card;

        normalized.Phones.Should().ContainSingle().Which.Should().Be("+15550142887");
        normalized.Emails.Should().ContainSingle().Which.Should().Be("m.feld@halbrook-logistics.example");
        normalized.Website.Should().Be("https://halbrook-logistics.example");
        normalized.Address.Should().Be("418 Kestrel Avenue, Suite 12");
    }
}
