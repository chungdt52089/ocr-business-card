using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Fakes;

/// <summary>
/// Bản cài ném đúng một exception dựng sẵn — dùng để kiểm đường lỗi của <c>CardPipeline</c>
/// mà không cần tới HTTP.
///
/// Hành vi HTTP thật của <c>GeminiExtractor</c> (mã 429, 400, treo tới timeout) kiểm ở
/// <c>GeminiExtractorTests</c> với <c>HttpMessageHandler</c> giả; ở đây chỉ hỏi một câu:
/// **đường ống làm gì khi extractor ném?**
/// </summary>
public sealed class ThrowingExtractor(Exception toThrow) : IExtractor
{
    /// <summary>Ca 429 đòi đúng 1 — thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế.</summary>
    public int Calls { get; private set; }

    public Task<RawExtraction> ExtractRawAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct)
    {
        Calls++;
        throw toThrow;
    }

    public Task<CardExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct)
    {
        Calls++;
        throw toThrow;
    }
}
