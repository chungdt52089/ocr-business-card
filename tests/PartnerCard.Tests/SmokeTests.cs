namespace PartnerCard.Tests;

/// <summary>
/// T-01 — khung dựng xong thì bộ dữ liệu mẫu phải nằm cạnh binary, vì T-04 đọc nó lúc chạy.
/// Sai luật chép file ở đây thì T-04 đỏ hàng loạt mà nguyên nhân trông không liên quan.
/// </summary>
public sealed class SmokeTests
{
    private static string TestData => Path.Combine(AppContext.BaseDirectory, "TestData");

    [Fact]
    [Trait("Category", "Integration")]
    public void Bo_anh_render_va_dap_an_di_cung_binary()
    {
        var cards = Path.Combine(TestData, "cards");

        Directory.Exists(cards).Should().BeTrue();
        Directory.GetFiles(cards, "*.png").Should().HaveCount(20);
        File.Exists(Path.Combine(cards, "expected.json")).Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Bo_anh_chup_that_di_cung_binary()
    {
        var realcards = Path.Combine(TestData, "realcards");

        Directory.Exists(realcards).Should().BeTrue();
        Directory.GetFiles(realcards, "*.jpg").Should().HaveCount(19);
    }
}
