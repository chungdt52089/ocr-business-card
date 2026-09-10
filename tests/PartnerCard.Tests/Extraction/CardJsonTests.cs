using System.Text.Json.Nodes;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Extraction;

/// <summary>
/// Ca chống lệch thứ hai của T-07: hình dạng JSON <see cref="CardJson"/> viết ra phải khớp đúng
/// danh sách khoá <see cref="CardSchema"/> khai báo.
///
/// Lệch một khoá là guard chặn nhầm — SG-1 kêu khoá lạ hoặc SG-2 kêu thiếu khoá bắt buộc, trên
/// chính chuỗi do ta sinh ra.
/// </summary>
[Trait("Category", "Guard")]
public sealed class CardJsonTests
{
    private static CardExtractionResult SampleCard() => new(
        IsBusinessCard: true,
        RejectReason: string.Empty,
        FullName: "田中 太郎",
        JobTitle: "営業部長",
        Company: "株式会社青葉精工",
        Phones: ["03-5550-1284"],
        Emails: ["t.tanaka@aoba-seiko.example"],
        Website: "https://aoba-seiko.example",
        Address: "東京都千代田区",
        DetectedLanguage: "ja",
        SearchAlias: "Tanaka Taro Aoba Seiko Tokyo Chiyoda",
        FieldConfidence: CardSchema.ConfidenceRequiredFields.ToDictionary(
            field => field, _ => 1.0, StringComparer.Ordinal));

    [Fact]
    public void Serialize_sinh_dung_bo_khoa_ma_CardSchema_khai_bao()
    {
        // So theo tập hợp chứ không theo thứ tự: thứ tự khoá khi serialize là chi tiết của
        // System.Text.Json, còn thứ tự ta thật sự quan tâm nằm ở propertyOrdering của schema.
        var keys = JsonNode.Parse(CardJson.Serialize(SampleCard()))!
            .AsObject().Select(pair => pair.Key);

        keys.Should().BeEquivalentTo(CardSchema.AllowedKeys);
    }

    [Fact]
    public void Serialize_khong_de_Warnings_lot_ra_day()
    {
        var card = SampleCard() with { Warnings = ["unnormalizedPhone"] };

        // Guard soi chuỗi này ở nhánh lưu; warnings lọt ra là SG-1 kêu oan chính ta.
        var json = CardJson.Serialize(card);

        json.Should().NotContain("warnings").And.NotContain("unnormalizedPhone");
        JsonNode.Parse(json)!.AsObject().Select(pair => pair.Key)
            .Should().BeEquivalentTo(CardSchema.AllowedKeys);
    }

    [Fact]
    public void Serialize_giu_nguyen_chu_Nhat_khong_escape()
    {
        var json = CardJson.Serialize(SampleCard());

        // Bị escape thì chuỗi sẽ là \u682a\u5f0f… — tức không còn chứa chính mấy chữ này nữa.
        json.Should().Contain("株式会社青葉精工");
    }

    [Fact]
    public void Vong_tron_serialize_roi_deserialize_giu_nguyen_noi_dung()
    {
        var card = SampleCard();

        var back = CardJson.Deserialize(CardJson.Serialize(card));

        back.Should().NotBeNull();
        back!.FullName.Should().Be("田中 太郎");
        back.Company.Should().Be("株式会社青葉精工");
        back.Phones.Should().Equal(card.Phones);
        back.FieldConfidence.Should().BeEquivalentTo(card.FieldConfidence);
        back.Warnings.Should().BeEmpty("Warnings không đi trên dây, nên chiều về luôn rỗng");
    }

    [Fact]
    public void Deserialize_chuoi_null_tra_ve_null_chu_khong_nem()
    {
        CardJson.Deserialize("null").Should().BeNull();
    }
}
