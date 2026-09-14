using System.Text.Json.Nodes;
using PartnerCard.Web.Extraction;

namespace PartnerCard.Tests.Extraction;

/// <summary>
/// Ca chống lệch của T-07 — SPEC mục 4.4.
///
/// <c>response_schema</c> gửi cho Gemini và danh sách khoá mà SG-1, SG-2, SG-3 dùng phải sinh ra
/// từ **cùng một nguồn** là <see cref="CardSchema.Fields"/>. Hai bản danh sách lệch nhau thì guard
/// chặn nhầm một kết quả hoàn toàn hợp lệ — và chặn nhầm chỉ lộ ra giữa buổi demo.
///
/// Thêm một trường mà quên một chỗ thì các ca dưới đây đỏ ngay tại `dotnet test`.
/// </summary>
[Trait("Category", "Guard")]
public sealed class CardSchemaTests
{
    /// <summary>Đúng 12 khoá của SPEC mục 4.4, đúng thứ tự.</summary>
    private static readonly string[] TwelveKeys =
    [
        "isBusinessCard", "rejectReason", "fullName", "jobTitle", "company", "phones",
        "emails", "website", "address", "detectedLanguage", "searchAlias", "fieldConfidence",
    ];

    /// <summary>Tập kiểu Gemini nhận — tập con OpenAPI, viết HOA (SPEC mục 4.4).</summary>
    private static readonly string[] UppercaseTypes =
        ["OBJECT", "STRING", "ARRAY", "BOOLEAN", "NUMBER"];

    // ---- Bốn danh sách đều suy ra từ Fields ------------------------------------------

    [Fact]
    public void Khai_bao_dung_12_truong_theo_SPEC_4_4()
    {
        CardSchema.Fields.Select(field => field.Name).Should().Equal(TwelveKeys);
    }

    [Fact]
    public void AllowedKeys_suy_ra_tu_Fields()
    {
        CardSchema.AllowedKeys.Should().BeEquivalentTo(TwelveKeys);
    }

    [Fact]
    public void RequiredKeys_dung_bay_khoa_bat_buoc()
    {
        CardSchema.RequiredKeys.Should().Equal(
            "isBusinessCard", "fullName", "company", "phones",
            "emails", "detectedLanguage", "fieldConfidence");
    }

    [Fact]
    public void ConfidenceRequiredFields_dung_tam_truong()
    {
        CardSchema.ConfidenceRequiredFields.Should().Equal(
            "fullName", "jobTitle", "company", "phones",
            "emails", "website", "address", "searchAlias");
    }

    [Fact]
    public void ConfidenceExemptFields_dung_ba_truong_va_khong_gom_fieldConfidence()
    {
        // Thiếu danh sách này thì SG-3 chặn MỌI tấm thẻ hợp lệ, vì detectedLanguage luôn có giá trị.
        CardSchema.ConfidenceExemptFields.Should().BeEquivalentTo(
            "isBusinessCard", "rejectReason", "detectedLanguage");

        // fieldConfidence không thuộc bên nào: nó là cái bản đồ, không phải một trường được chấm.
        CardSchema.ConfidenceExemptFields.Should().NotContain("fieldConfidence");
        CardSchema.ConfidenceRequiredFields.Should().NotContain("fieldConfidence");
    }

    // ---- response_schema sinh ra từ đúng bộ đó ---------------------------------------

    [Fact]
    public void ResponseSchema_co_dung_bo_khoa_cua_AllowedKeys()
    {
        Properties(CardSchema.ResponseSchema()).Should().BeEquivalentTo(CardSchema.AllowedKeys);
    }

    [Fact]
    public void ResponseSchema_required_khop_RequiredKeys()
    {
        Strings(CardSchema.ResponseSchema()["required"]).Should().Equal(CardSchema.RequiredKeys);
    }

    [Fact]
    public void ResponseSchema_propertyOrdering_theo_dung_thu_tu_Fields()
    {
        Strings(CardSchema.ResponseSchema()["propertyOrdering"])
            .Should().Equal(CardSchema.Fields.Select(field => field.Name));
    }

    [Fact]
    public void ResponseSchema_nhanh_fieldConfidence_khop_tam_truong()
    {
        var confidence = CardSchema.ResponseSchema()["properties"]!["fieldConfidence"]!.AsObject();

        Properties(confidence).Should().BeEquivalentTo(CardSchema.ConfidenceRequiredFields);
        Strings(confidence["propertyOrdering"]).Should().Equal(CardSchema.ConfidenceRequiredFields);
        confidence["properties"]!["fullName"]!["type"]!.GetValue<string>().Should().Be("NUMBER");
    }

    [Fact]
    public void Moi_type_trong_ResponseSchema_deu_viet_hoa()
    {
        // Gemini nhận tập con OpenAPI: "STRING", không phải "string".
        var types = new List<string>();
        CollectTypes(CardSchema.ResponseSchema(), types);

        types.Should().NotBeEmpty();
        types.Should().OnlyContain(type => UppercaseTypes.Contains(type));
    }

    [Fact]
    public void phones_va_emails_la_mang_chuoi()
    {
        var properties = CardSchema.ResponseSchema()["properties"]!;

        foreach (var field in (string[])["phones", "emails"])
        {
            properties[field]!["type"]!.GetValue<string>().Should().Be("ARRAY");
            properties[field]!["items"]!["type"]!.GetValue<string>().Should().Be("STRING");
        }
    }

    [Fact]
    public void ResponseSchema_tra_the_hien_moi_moi_lan_goi()
    {
        // JsonNode chỉ có một cha: dùng chung một thể hiện thì lần gắn thứ hai vào thân request
        // sẽ ném InvalidOperationException — hỏng đúng lúc gọi mô hình lần thứ hai.
        var first = CardSchema.ResponseSchema();
        var second = CardSchema.ResponseSchema();

        first.Should().NotBeSameAs(second);
        first.ToJsonString().Should().Be(second.ToJsonString());
    }

    // ---- helper ----------------------------------------------------------------------

    private static IEnumerable<string> Properties(JsonObject schema) =>
        schema["properties"]!.AsObject().Select(pair => pair.Key);

    private static IEnumerable<string> Strings(JsonNode? node) =>
        node!.AsArray().Select(element => element!.GetValue<string>());

    private static void CollectTypes(JsonNode? node, List<string> found)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var pair in o)
                {
                    if (pair.Key == "type")
                    {
                        found.Add(pair.Value!.GetValue<string>());
                    }
                    else
                    {
                        CollectTypes(pair.Value, found);
                    }
                }

                break;

            case JsonArray a:
                foreach (var element in a)
                {
                    CollectTypes(element, found);
                }

                break;
        }
    }
}
