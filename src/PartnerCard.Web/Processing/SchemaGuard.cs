using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using PartnerCard.Web.Extraction;

namespace PartnerCard.Web.Processing;

/// <summary>Guard đứng ở hai vị trí khác nhau tuỳ nhánh — SPEC mục 2.</summary>
public enum GuardBranch
{
    /// <summary>Soi chuỗi JSON **thô do mô hình trả về**, trước khi deserialize. Áp dụng cả 8 luật.</summary>
    Extraction,

    /// <summary>
    /// Soi DTO đã serialize, ở cuối chuỗi. **Bỏ qua SG-1 và SG-2**: JSON do chính ta serialize
    /// từ record nên khoá lạ và khoá thiếu không thể xảy ra.
    /// </summary>
    Save,
}

public sealed record GuardWarning(string Code, string Field, string Reason);

/// <summary>
/// Phán quyết của guard. <see cref="CleanedJson"/> là <c>null</c> khi bị chặn — chặn nghĩa là
/// không có gì đi tiếp, không phải "đi tiếp nhưng đã dọn".
/// </summary>
public sealed record GuardResult(
    bool IsBlocked,
    string? BlockCode,
    string? CleanedJson,
    IReadOnlyList<GuardWarning> Warnings)
{
    public bool IsClean => !IsBlocked && Warnings.Count == 0;
}

public interface ISchemaGuard
{
    GuardResult Check(string jsonContent, GuardBranch branch);
}

/// <summary>
/// Lưới an toàn cuối — SPEC mục 7. Nhận **chuỗi JSON**, không nhận record: deserialize vào
/// record C# là khoá lạ bị nuốt im lặng, và SG-1 không bao giờ kích hoạt được nữa.
///
/// SG-7 là luật quan trọng nhất. Nó là điều kiện làm US-02 thành một bảo đảm chứ không phải
/// một lời hứa: hệ thống đã nói không đọc được thì không được đồng thời đưa ra dữ liệu.
/// </summary>
public sealed class SchemaGuard : ISchemaGuard
{
    private const int MaxFullNameLength = 100;
    private const int MaxCompanyLength = 200;

    /// <summary>Khớp theo **chuỗi con**: dấu vết này không bao giờ là một phần của dữ liệu thật.</summary>
    private static readonly string[] ChatterFragments = ["```json", "I cannot", "Tôi không thể"];

    /// <summary>
    /// Khớp theo **toàn bộ giá trị**, không phân biệt hoa thường. Khớp chuỗi con ở đây sẽ xoá
    /// nhầm một công ty tên "Nullson" hay một địa chỉ có chữ "unknown" trong tên đường.
    /// </summary>
    private static readonly string[] ChatterValues = ["n/a", "null", "unknown", "không rõ"];

    /// <summary>
    /// Ký tự được phép còn lại trong một số điện thoại **thô**. Gồm cả `.`, `(`, `)` vì ở nhánh
    /// trích xuất guard chạy **trước** <c>Normalizer</c>, mà `(028) 3822 1100` là dạng in hợp lệ
    /// trên thẻ (SPEC mục 5.1). Chỉ liệt kê số/`+`/`-`/khoảng trắng như bản đầu của SG-5 sẽ xoá
    /// nhầm những số đó.
    /// </summary>
    private static readonly char[] AllowedPhoneChars = ['+', '-', ' ', '.', '(', ')'];

    private static readonly string[] TextFields =
        ["fullName", "jobTitle", "company", "website", "address", "searchAlias"];

    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public GuardResult Check(string jsonContent, GuardBranch branch)
    {
        var root = TryParse(jsonContent);
        if (root is null)
        {
            return Blocked("SG-0", "Nội dung trả về không phải một đối tượng JSON.");
        }

        var warnings = new List<GuardWarning>();

        if (branch == GuardBranch.Extraction)
        {
            RemoveUnknownKeys(root, warnings);

            if (FirstMissingRequiredKey(root) is { } missing)
            {
                return Blocked("SG-2", $"Thiếu khoá bắt buộc: {missing}.");
            }
        }

        CleanStringFields(root, warnings);
        CleanArray(root, "emails", IsAcceptableEmail, "SG-4", warnings);
        CleanArray(root, "phones", IsAcceptablePhone, "SG-5", warnings);

        if (FirstFieldMissingConfidence(root) is { } withoutConfidence)
        {
            return Blocked("SG-3", $"Trường {withoutConfidence} có giá trị nhưng thiếu confidence.");
        }

        if (SaysNotACardButCarriesData(root))
        {
            return Blocked("SG-7",
                "isBusinessCard = false nhưng vẫn có trường mang giá trị.");
        }

        return new GuardResult(
            IsBlocked: false,
            BlockCode: null,
            CleanedJson: root.ToJsonString(OutputOptions),
            Warnings: warnings);
    }

    private static JsonObject? TryParse(string jsonContent)
    {
        try
        {
            return JsonNode.Parse(jsonContent) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>SG-1 — bỏ khoá lạ, ghi cảnh báo.</summary>
    private static void RemoveUnknownKeys(JsonObject root, List<GuardWarning> warnings)
    {
        var unknown = root
            .Select(pair => pair.Key)
            .Where(key => !CardSchema.AllowedKeys.Contains(key))
            .ToList();

        foreach (var key in unknown)
        {
            root.Remove(key);
            warnings.Add(new GuardWarning("SG-1", key, "Khoá không có trong schema, đã bỏ."));
        }
    }

    /// <summary>SG-2 — thiếu khoá bắt buộc thì chặn.</summary>
    private static string? FirstMissingRequiredKey(JsonObject root) =>
        CardSchema.RequiredKeys.FirstOrDefault(key => !root.ContainsKey(key));

    /// <summary>SG-6 và SG-8 trên các trường chuỗi.</summary>
    private static void CleanStringFields(JsonObject root, List<GuardWarning> warnings)
    {
        foreach (var field in TextFields)
        {
            var value = ReadString(root, field);
            if (value.Length == 0)
            {
                continue;
            }

            if (IsModelChatter(value))
            {
                root[field] = string.Empty;
                warnings.Add(new GuardWarning("SG-6", field, "Giá trị mang dấu vết mô hình nói chuyện."));
                continue;
            }

            var limit = field switch
            {
                "fullName" => MaxFullNameLength,
                "company" => MaxCompanyLength,
                _ => int.MaxValue,
            };

            if (value.Length > limit)
            {
                root[field] = string.Empty;
                warnings.Add(new GuardWarning("SG-8", field, $"Dài bất thường, vượt {limit} ký tự."));
            }
        }
    }

    /// <summary>SG-4 và SG-5 — bỏ phần tử không qua được kiểm định dạng.</summary>
    private static void CleanArray(
        JsonObject root, string field, Func<string, bool> isAcceptable,
        string code, List<GuardWarning> warnings)
    {
        if (root[field] is not JsonArray array)
        {
            return;
        }

        var kept = new JsonArray();
        var dropped = false;

        foreach (var element in array)
        {
            var value = element?.GetValue<string>() ?? string.Empty;

            if (value.Length > 0 && isAcceptable(value) && !IsModelChatter(value))
            {
                kept.Add(value);
            }
            else
            {
                dropped = true;
            }
        }

        if (!dropped)
        {
            return;
        }

        root[field] = kept;
        warnings.Add(new GuardWarning(code, field, "Phần tử không qua kiểm định dạng, đã xoá."));
    }

    /// <summary>SG-3 — trường có giá trị thì phải có confidence, trừ ba trường được miễn.</summary>
    private static string? FirstFieldMissingConfidence(JsonObject root)
    {
        var confidence = root["fieldConfidence"] as JsonObject;

        foreach (var field in CardSchema.ConfidenceRequiredFields)
        {
            if (CardSchema.ConfidenceExemptFields.Contains(field) || !HasValue(root, field))
            {
                continue;
            }

            if (confidence is null || !confidence.ContainsKey(field))
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>SG-7 — nói không đọc được thì không được đồng thời đưa ra dữ liệu.</summary>
    private static bool SaysNotACardButCarriesData(JsonObject root)
    {
        if (root["isBusinessCard"]?.GetValue<bool>() is not false)
        {
            return false;
        }

        // Chỉ xét tám trường nội dung. rejectReason phải có giá trị mới đúng, còn
        // detectedLanguage là nhãn phân loại — cùng lý do với danh sách miễn trừ của SG-3.
        return CardSchema.ConfidenceRequiredFields.Any(field => HasValue(root, field));
    }

    private static bool HasValue(JsonObject root, string field) => root[field] switch
    {
        JsonArray array => array.Count > 0,
        null => false,
        var node => !string.IsNullOrWhiteSpace(node.GetValue<string>()),
    };

    private static string ReadString(JsonObject root, string field) =>
        root[field] is { } node && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : string.Empty;

    private static bool IsModelChatter(string value)
    {
        if (ChatterFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ChatterValues.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsAcceptableEmail(string value) =>
        Confidence.FormatScoreForEmail(value) == 1.0;

    private static bool IsAcceptablePhone(string value) =>
        value.All(ch => char.IsDigit(ch) || AllowedPhoneChars.Contains(ch));

    private static GuardResult Blocked(string code, string reason) =>
        new(IsBlocked: true, BlockCode: code, CleanedJson: null,
            Warnings: [new GuardWarning(code, string.Empty, reason)]);
}
