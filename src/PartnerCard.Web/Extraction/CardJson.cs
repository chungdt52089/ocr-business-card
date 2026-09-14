using System.Text.Encodings.Web;
using System.Text.Json;
using PartnerCard.Web.Models;

namespace PartnerCard.Web.Extraction;

/// <summary>
/// Chỗ **duy nhất** biết hình dạng JSON của một tấm thẻ trên dây — gom lại ở T-07.
///
/// Trước đó cùng 12 khoá bị chép tay ở hai nơi (<c>FakeExtractor.ExtractRawAsync</c> và
/// <c>CardPipeline.Serialize</c>), cộng danh sách khoá trong <see cref="CardSchema"/> là ba.
/// Thêm một trường mà quên một chỗ thì guard chặn nhầm chính kết quả hợp lệ của mình.
///
/// Phân vai: <see cref="CardSchema"/> biết **có những khoá nào**; class này biết **viết và đọc
/// chúng ra sao**. Ca <c>CardJsonTests</c> đối chiếu hai bên với nhau.
/// </summary>
public static class CardJson
{
    /// <summary>
    /// <c>UnsafeRelaxedJsonEscaping</c> là bắt buộc, không phải tuỳ chọn: thiếu nó thì
    /// <c>株式会社青葉精工</c> ra <c>\uXXXX</c> — vẫn đúng JSON nhưng không ai đọc nổi khi gỡ rối,
    /// và guard thì soi chuỗi.
    /// </summary>
    private static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Indented = new(Compact)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Serialize đúng 12 khoá của schema. <c>Warnings</c> mang <c>[JsonIgnore]</c> nên không lọt
    /// ra dây — nó do ta thêm vào sau, mà guard thấy khoá lạ thì SG-1 sẽ kêu.
    /// </summary>
    public static string Serialize(CardExtractionResult card, bool indented = false) =>
        JsonSerializer.Serialize(card, indented ? Indented : Compact);

    /// <summary>
    /// Trả <c>null</c> khi chuỗi là <c>null</c> theo nghĩa JSON. Chuỗi **hỏng cú pháp** thì ném
    /// <c>JsonException</c> chứ không trả <c>null</c> — người gọi phải bắt cả hai. <c>CardPipeline</c>
    /// không gặp trường hợp sau vì SG-0 đã chặn chuỗi không phải JSON trước khi tới đây; bộ đo thì
    /// không qua guard nên nó bắt <c>JsonException</c> và ghi thành <c>bad_json</c>.
    /// </summary>
    public static CardExtractionResult? Deserialize(string json) =>
        JsonSerializer.Deserialize<CardExtractionResult>(json, Compact);
}
