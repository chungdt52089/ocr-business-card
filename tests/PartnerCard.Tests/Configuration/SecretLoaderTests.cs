using Microsoft.Extensions.Configuration;
using PartnerCard.Web.Configuration;

namespace PartnerCard.Tests.Configuration;

/// <summary>
/// TEST-SPEC mục 10 — I-07 và I-08, các ca thuộc T-03.
///
/// Kiểm thẳng vào <see cref="SecretLoader"/> chứ không dựng cả host: luật cần chứng minh là
/// "thiếu khoá thì từ chối", và luật đó nằm trọn ở đây. Dựng host chỉ thêm một phụ thuộc
/// (<c>Mvc.Testing</c>) không có trong SPEC mục 1 mà không kiểm thêm được gì.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecretLoaderTests
{
    private const string ValidKey = "AIzaSyDUMMY-khoa-gia-cho-ca-kiem-thu";

    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void I07_che_do_gemini_thieu_khoa_thi_tu_choi_khoi_dong()
    {
        var options = new PartnerCardOptions { Extractor = ExtractorNames.Gemini };

        var act = () => SecretLoader.Load(Config(), options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{SecretOptions.ApiKeyVariable}*");
    }

    [Fact]
    public void I08_che_do_fake_khong_can_khoa_van_khoi_dong_binh_thuong()
    {
        var options = new PartnerCardOptions { Extractor = ExtractorNames.Fake };

        var secrets = SecretLoader.Load(Config(), options);

        secrets.GeminiApiKey.Should().BeNull();
    }

    [Fact]
    public void Che_do_gemini_co_khoa_hop_le_thi_nap_duoc()
    {
        var options = new PartnerCardOptions { Extractor = ExtractorNames.Gemini };

        var secrets = SecretLoader.Load(Config((SecretOptions.ApiKeyVariable, ValidKey)), options);

        secrets.GeminiApiKey.Should().Be(ValidKey);
    }

    [Theory]
    [InlineData("ngan")]
    [InlineData("khoa co khoang trang o giua")]
    public void Khoa_sai_dinh_dang_bi_tu_choi_va_thong_bao_khong_nhac_lai_gia_tri(string badKey)
    {
        var options = new PartnerCardOptions { Extractor = ExtractorNames.Gemini };

        var act = () => SecretLoader.Load(Config((SecretOptions.ApiKeyVariable, badKey)), options);

        var thrown = act.Should().Throw<InvalidOperationException>().Which;

        // Repo có remote công khai: thông báo lỗi không được lặp lại giá trị khoá, kể cả khoá sai.
        thrown.Message.Should().NotContain(badKey);
    }

    [Fact]
    public void Extractor_go_sai_bi_tu_choi_chu_khong_im_lang_roi_ve_fake()
    {
        var options = new PartnerCardOptions { Extractor = "gemni" };

        var act = () => SecretLoader.Load(Config(), options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Extractor*không hợp lệ*");
    }

    [Fact]
    public void Khoa_da_nap_khong_lot_ra_qua_ToString()
    {
        var secrets = new SecretOptions { GeminiApiKey = ValidKey };

        secrets.ToString().Should().NotContain(ValidKey);
    }
}
