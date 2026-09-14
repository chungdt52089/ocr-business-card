using System.Text.Json;
using Microsoft.Extensions.Logging;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;

namespace PartnerCard.Tests.Tools;

/// <summary>
/// T-11 — <c>extract_business_card</c> và <c>save_partner</c>: M-04, M-05, M-06, M-09.
///
/// Hai tool này đi qua <c>CardPipeline</c>, không gọi thẳng extractor hay kho. Tầng tool chỉ ánh xạ
/// kết quả, và bắt những gì **thoát ra khỏi** đường ống.
/// </summary>
[Trait("Category", "Tool")]
public sealed class CardToolsTests
{
    [Fact]
    public async Task M04_extract_tra_ket_qua_co_cau_truc_va_khong_ghi_vao_kho()
    {
        using var harness = new ToolHarness();
        var expected = ExpectedCards.LoadFrom(Path.Combine(ToolHarness.CardsDirectory, "expected.json"))
            .Find("en-01")!;

        var result = await harness.ExtractAsync("en-01.png");

        result.Ok.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Card!.IsBusinessCard.Should().BeTrue();
        result.Card.FullName.Should().Be(expected.FullName);
        result.Card.FieldConfidence.Should().NotBeEmpty();

        File.Exists(harness.PartnersFile).Should().BeFalse("M-04: extract_business_card không ghi vào partners.json");
        (await harness.Store.SearchAsync(new PartnerQuery(), CancellationToken.None)).Should().BeEmpty();
    }

    /// <summary>
    /// <c>Card.Warnings</c> mang <c>[JsonIgnore]</c>, nên nếu tool trả thẳng thẻ thì cảnh báo của
    /// <c>Normalizer</c> biến mất khi serialize — đúng cái bẫy hai kênh ghi ở BACKLOG T-13.
    /// </summary>
    [Fact]
    public async Task M04_canh_bao_chuan_hoa_khong_bi_danh_roi_o_tang_tool()
    {
        using var harness = new ToolHarness();

        var result = await harness.ExtractAsync("ja-01.png");

        result.NormalizationWarnings.Should().Contain(Normalizer.UnnormalizedPhoneWarning);
    }

    [Fact]
    public async Task M05_save_ho_so_hop_le_tra_partnerId_va_ho_so_co_trong_kho()
    {
        using var harness = new ToolHarness();
        var card = CardBuilder.Valid();

        var saved = await harness.SaveAsync(card);

        saved.Ok.Should().BeTrue();
        saved.ErrorCode.Should().BeNull();
        saved.PartnerId.Should().Be("PTN0001");

        var stored = await harness.Store.GetAsync(saved.PartnerId!, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.FullName.Should().Be(card.FullName);
        stored.Company.Should().Be(card.Company);
        stored.Emails.Should().Equal(card.Emails);
        stored.Phones.Should().Equal(card.Phones);
    }

    [Fact]
    public async Task M06_save_ho_so_trung_tra_duplicateOf_va_chua_ghi_gi_them()
    {
        using var harness = new ToolHarness();
        var card = CardBuilder.Valid();

        var first = await harness.SaveAsync(card);
        var before = File.ReadAllBytes(harness.PartnersFile);

        var second = await harness.SaveAsync(card);

        second.Ok.Should().BeFalse();
        second.ErrorCode.Should().Be("duplicate");
        second.PartnerId.Should().BeNull();
        second.DuplicateOf!.PartnerId.Should().Be(first.PartnerId);
        second.DuplicateOf.Emails.Should().Equal(card.Emails);

        File.ReadAllBytes(harness.PartnersFile).Should().Equal(before, "M-06: chưa ghi gì thêm");
        (await harness.Store.SearchAsync(new PartnerQuery(), CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Save_voi_allowDuplicate_van_luu_ho_so_moi()
    {
        using var harness = new ToolHarness();
        var card = CardBuilder.Valid();

        await harness.SaveAsync(card);
        var again = await harness.SaveAsync(card, allowDuplicate: true);

        again.Ok.Should().BeTrue();
        again.PartnerId.Should().Be("PTN0002");
    }

    [Fact]
    public async Task M09_exception_thoat_khoi_pipeline_thanh_thong_bao_trung_tinh()
    {
        using var harness = new ToolHarness(options: new ThrowingOptions());

        var result = await harness.ExtractAsync("en-01.png");

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("extract_failed");
        result.Message.Should().NotBeNullOrWhiteSpace();
        ShouldBeNeutral(result.Message!);
        ShouldBeNeutral(JsonSerializer.Serialize(result));

        // Chi tiết ở lại phía server, trên kênh chẩn đoán.
        harness.Logger.Entries.Should().Contain(line => line.Level == LogLevel.Error);
    }

    /// <summary>
    /// "Thông báo trung tính" không có nghĩa là "giấu nguyên nhân" (SPEC mục 10.3). Nuốt
    /// <c>quota_exhausted</c> thành một câu chung chung là lặp lại đúng cái bẫy T-07 đã gỡ.
    /// </summary>
    [Fact]
    public async Task M09_ma_loi_cua_extractor_di_qua_tang_tool_nguyen_ven()
    {
        using var harness = new ToolHarness(new ThrowingExtractor(new ExtractorQuotaException()));

        var result = await harness.ExtractAsync("en-01.png");

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("quota_exhausted");
        result.Message.Should().Be(new ExtractorQuotaException().Message);
        ShouldBeNeutral(JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task M09_kho_nem_exception_khi_luu_thanh_save_failed_trung_tinh()
    {
        using var harness = new ToolHarness(store: new ThrowingPartnerStore());

        var result = await harness.SaveAsync(CardBuilder.Valid());

        result.Ok.Should().BeFalse();
        result.ErrorCode.Should().Be("save_failed");
        ShouldBeNeutral(JsonSerializer.Serialize(result));
    }

    internal static void ShouldBeNeutral(string text)
    {
        text.Should().NotContain(@"C:\");
        text.Should().NotContain("appsettings");
        text.Should().NotContain("GEMINI_API_KEY");
        text.Should().NotContain("partners.json");
        text.Should().NotContain("   at ");
        text.Should().NotContain("Exception");
    }
}
