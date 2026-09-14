using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Fakes;

/// <summary>
/// Phát lại một <see cref="RawExtraction"/> đã bắt được, **không gọi mạng lần nào**.
///
/// Lý do nó tồn tại: ca <c>Live</c> cần cả hai thứ mà bình thường loại trừ nhau —
/// **thấy được exception** của lời gọi thật (mà <c>CardPipeline</c> thì cố ý nuốt hết thành kết
/// quả có cấu trúc), và **vẫn đi hết đường ống** để biết guard có chặn oan đầu ra của mô hình
/// thật không. Gọi extractor trực tiếp rồi phát lại kết quả qua đường ống cho cả hai, mà vẫn
/// chỉ tiêu **một** request hạn mức.
/// </summary>
public sealed class ReplayExtractor(RawExtraction captured) : IExtractor
{
    public Task<RawExtraction> ExtractRawAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct) =>
        Task.FromResult(captured);

    public Task<CardExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct) =>
        Task.FromResult(CardJson.Deserialize(captured.Json)
            ?? throw new InvalidOperationException("Chuỗi đã bắt được không dựng lại thành thẻ."));
}
