using System.Text.Json.Nodes;

namespace PartnerCard.Web.Extraction;

/// <summary>Kiểu của một trường trong schema đầu ra — SPEC mục 4.4.</summary>
public enum CardFieldKind
{
    Bool,
    Text,
    TextArray,

    /// <summary>Bản đồ điểm tin cậy: khoá là tên trường, giá trị là số.</summary>
    ConfidenceMap,
}

/// <summary>
/// Một trường trong schema. Bốn thuộc tính này là toàn bộ thứ cần biết để vừa sinh
/// <c>response_schema</c> gửi cho Gemini, vừa nuôi ba luật SG-1, SG-2 và SG-3.
/// </summary>
/// <param name="Required">Thiếu khoá này thì SG-2 chặn.</param>
/// <param name="NeedsConfidence">Mang giá trị mà thiếu mục trong <c>fieldConfidence</c> thì SG-3 chặn.</param>
public sealed record CardField(string Name, CardFieldKind Kind, bool Required, bool NeedsConfidence);

/// <summary>
/// Schema đầu ra của mô hình — SPEC mục 4.4. Khai báo **một chỗ duy nhất**: cùng bộ
/// <see cref="Fields"/> này vừa sinh ra schema gửi cho Gemini (T-07) vừa là nguồn sự thật cho
/// SG-1, SG-2 và SG-3 (T-06).
///
/// Hai bản sao lệch nhau là cách chắc chắn nhất để guard chặn nhầm một kết quả hợp lệ — và chặn
/// nhầm thì chỉ lộ ra giữa buổi demo. Vì vậy bốn danh sách dưới đây **suy ra** từ <see cref="Fields"/>,
/// không ai gõ tay lần thứ hai.
/// </summary>
public static class CardSchema
{
    /// <summary>
    /// Nguồn sự thật duy nhất. Thứ tự ở đây cũng là <c>propertyOrdering</c> gửi cho mô hình.
    /// </summary>
    public static readonly IReadOnlyList<CardField> Fields =
    [
        //           tên                 kiểu                       bắt buộc  cần confidence
        new("isBusinessCard",   CardFieldKind.Bool,          true,  false),
        new("rejectReason",     CardFieldKind.Text,          false, false),
        new("fullName",         CardFieldKind.Text,          true,  true),
        new("jobTitle",         CardFieldKind.Text,          false, true),
        new("company",          CardFieldKind.Text,          true,  true),
        new("phones",           CardFieldKind.TextArray,     true,  true),
        new("emails",           CardFieldKind.TextArray,     true,  true),
        new("website",          CardFieldKind.Text,          false, true),
        new("address",          CardFieldKind.Text,          false, true),
        new("detectedLanguage", CardFieldKind.Text,          true,  false),
        new("searchAlias",      CardFieldKind.Text,          false, true),
        new("fieldConfidence",  CardFieldKind.ConfidenceMap, true,  false),
    ];

    /// <summary>Mọi khoá hợp lệ. Khoá nào ngoài danh sách này thì SG-1 bỏ đi.</summary>
    public static readonly IReadOnlySet<string> AllowedKeys =
        Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Thiếu một trong các khoá này thì SG-2 chặn, không lưu.</summary>
    public static readonly IReadOnlyList<string> RequiredKeys =
        [.. Fields.Where(field => field.Required).Select(field => field.Name)];

    /// <summary>Tám trường **bắt buộc có confidence** khi mang giá trị — SG-3.</summary>
    public static readonly IReadOnlyList<string> ConfidenceRequiredFields =
        [.. Fields.Where(field => field.NeedsConfidence).Select(field => field.Name)];

    /// <summary>
    /// Ba trường **miễn trừ** SG-3: chúng mang giá trị mà không cần confidence, và đó là đúng —
    /// <c>isBusinessCard</c> là boolean phân loại, <c>rejectReason</c> do mô hình tự viết,
    /// <c>detectedLanguage</c> là nhãn. Thiếu danh sách này thì SG-3 chặn mọi tấm thẻ hợp lệ,
    /// vì <c>detectedLanguage</c> luôn có giá trị.
    ///
    /// <c>fieldConfidence</c> không thuộc bên nào: nó là chính cái bản đồ, không phải một trường
    /// được chấm điểm.
    /// </summary>
    public static readonly IReadOnlySet<string> ConfidenceExemptFields =
        Fields.Where(field => !field.NeedsConfidence && field.Kind != CardFieldKind.ConfidenceMap)
              .Select(field => field.Name)
              .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Schema gửi trong <c>generationConfig.response_schema</c> — SPEC mục 4.4.
    ///
    /// Hai chỗ khác lối JSON Schema quen thuộc, vì Gemini nhận **tập con OpenAPI**:
    /// <c>type</c> viết HOA, và có <c>propertyOrdering</c>.
    ///
    /// **Trả thể hiện mới mỗi lần gọi, cố ý.** <c>JsonNode</c> chỉ có một cha; dùng chung một thể
    /// hiện thì lần gắn thứ hai vào thân request sẽ ném — tức hỏng đúng ở lời gọi thứ hai, chỗ
    /// không ai nghĩ tới khi thử lần đầu thấy chạy được.
    /// </summary>
    public static JsonObject ResponseSchema()
    {
        var properties = new JsonObject();

        foreach (var field in Fields)
        {
            properties[field.Name] = SchemaFor(field.Kind);
        }

        return new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = properties,
            ["propertyOrdering"] = ArrayOf(Fields.Select(field => field.Name)),
            ["required"] = ArrayOf(RequiredKeys),
        };
    }

    private static JsonObject SchemaFor(CardFieldKind kind) => kind switch
    {
        CardFieldKind.Bool => new JsonObject { ["type"] = "BOOLEAN" },
        CardFieldKind.Text => new JsonObject { ["type"] = "STRING" },
        CardFieldKind.TextArray => new JsonObject
        {
            ["type"] = "ARRAY",
            ["items"] = new JsonObject { ["type"] = "STRING" },
        },
        CardFieldKind.ConfidenceMap => ConfidenceSchema(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Kiểu trường chưa có bản dịch sang schema."),
    };

    /// <summary>Bản đồ confidence có đúng tám khoá của <see cref="ConfidenceRequiredFields"/>.</summary>
    private static JsonObject ConfidenceSchema()
    {
        var properties = new JsonObject();

        foreach (var field in ConfidenceRequiredFields)
        {
            properties[field] = new JsonObject { ["type"] = "NUMBER" };
        }

        return new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = properties,
            ["propertyOrdering"] = ArrayOf(ConfidenceRequiredFields),
        };
    }

    private static JsonArray ArrayOf(IEnumerable<string> values)
    {
        var array = new JsonArray();

        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
