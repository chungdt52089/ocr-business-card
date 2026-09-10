using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Fakes;

/// <summary>
/// Bản cài <see cref="IExtractor"/> **cố tình trả rác** — TEST-SPEC G-14.
///
/// Mọi trường điền đầy, <c>isBusinessCard = false</c>, email không có <c>@</c>,
/// <c>company</c> là ```` ```json ````. Đây là kịch bản tệ nhất mà một mô hình có thể tạo ra:
/// nó vừa nói "không đọc được" vừa đưa ra dữ liệu.
///
/// Guard phải chặn. Nếu ca dùng class này không đỏ khi gỡ guard ra thì lưới an toàn không tồn tại.
/// </summary>
public sealed class HostileExtractor : IExtractor
{
    public const string Json = """
        {
          "isBusinessCard": false,
          "rejectReason": "",
          "fullName": "Nguyễn Văn An",
          "jobTitle": "Giám đốc Kinh doanh",
          "company": "```json",
          "phones": ["0912345678"],
          "emails": ["an.nguyen"],
          "website": "abc.example",
          "address": "12 Nguyễn Huệ, Quận 1",
          "detectedLanguage": "vi",
          "searchAlias": "",
          "fieldConfidence": {
            "fullName": 0.99, "jobTitle": 0.99, "company": 0.99, "phones": 0.99,
            "emails": 0.99, "website": 0.99, "address": 0.99, "searchAlias": 0.0
          }
        }
        """;

    public Task<string> ExtractRawAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct) =>
        Task.FromResult(Json);

    public Task<CardExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct) =>
        throw new NotSupportedException("Ca G-14 đi qua ExtractRawAsync để guard soi được JSON thô.");
}
