using System.Text.Json;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Models;
using PartnerCard.Web.Tools;

namespace PartnerCard.Tests.Tools;

/// <summary>
/// T-11 — <c>search_partners</c> và <c>enrich_partner</c>: M-07, M-08, M-09.
///
/// <c>search_partners</c> chỉ đọc kho. <c>enrich_partner</c> không chạm gì cả: lớp bổ sung đã cắt
/// ngày 08/09 (SPEC mục 9), tool chỉ giữ chỗ trong <c>tools/list</c>.
/// </summary>
[Trait("Category", "Tool")]
public sealed class PartnerToolsTests
{
    [Fact]
    public async Task M07_search_khong_tieu_chi_tra_toi_da_20()
    {
        using var harness = new ToolHarness();

        for (var i = 1; i <= 25; i++)
        {
            await harness.JsonStore.UpsertAsync(
                PartnerFactory.New(fullName: $"Đối tác {i}", email: $"p{i}@halbrook-logistics.example"),
                CancellationToken.None);
        }

        var result = await harness.Partners.SearchPartnersAsync();

        result.Ok.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Partners.Should().HaveCount(QueryLimits.DefaultTake);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("https://halbrook-logistics.example", "Halbrook Logistics")]
    public void M08_enrich_partner_voi_bat_ky_dau_vao_nao_tra_not_implemented(string? website, string? companyName)
    {
        var result = PartnerTools.EnrichPartner(website, companyName);

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("not_implemented");
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task M09_kho_nem_exception_khi_tim_thanh_thong_bao_trung_tinh()
    {
        using var harness = new ToolHarness(store: new ThrowingPartnerStore());

        var result = await harness.Partners.SearchPartnersAsync(keyword: "Feld");

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("search_failed");
        result.Partners.Should().BeEmpty();
        result.Message.Should().NotBeNullOrWhiteSpace();
        CardToolsTests.ShouldBeNeutral(result.Message!);
        CardToolsTests.ShouldBeNeutral(JsonSerializer.Serialize(result));
    }
}
