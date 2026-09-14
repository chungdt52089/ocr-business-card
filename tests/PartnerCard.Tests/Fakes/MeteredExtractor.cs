using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Fakes;

/// <summary>
/// Bọc một extractor, chờ một khoảng cố định rồi gắn số đo dựng sẵn — ca A-04.
///
/// Bản fake tự nó ghi 0 token và chạy gần như tức thì, nên ca "ghi <c>tokensIn</c>,
/// <c>tokensOut</c>, <c>latencyMs</c>" sẽ xanh cả khi ba trường đó bị ghi cứng bằng 0.
/// Đợi bằng <see cref="Task.Delay(TimeSpan, CancellationToken)"/>, không gọi mạng.
/// </summary>
public sealed class MeteredExtractor(IExtractor inner, ExtractionUsage usage, TimeSpan delay) : IExtractor
{
    public async Task<RawExtraction> ExtractRawAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct)
    {
        await Task.Delay(delay, ct);

        var raw = await inner.ExtractRawAsync(imageBytes, mimeType, languageHint, sourceName, ct);

        return raw with { Usage = usage };
    }

    public Task<CardExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct) =>
        inner.ExtractAsync(imageBytes, mimeType, languageHint, sourceName, ct);
}
