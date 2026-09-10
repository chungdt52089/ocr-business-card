using PartnerCard.Web.Models;

namespace PartnerCard.Web.Extraction;

/// <summary>
/// Ranh giới quan trọng nhất trong hệ thống — SPEC mục 4.1.
///
/// Lý do ở BRD mục 4: nếu phải ngừng gửi ảnh ra nước ngoài thì việc thay thế phải gói gọn
/// trong một class. **Không được để lời gọi HTTP tới Gemini xuất hiện ở bất kỳ chỗ nào khác.**
/// </summary>
public interface IExtractor
{
    /// <summary>
    /// Trả **chuỗi JSON thô** chưa deserialize, kèm số đo của lần gọi. Đây là thứ
    /// <c>CardPipeline</c> dùng.
    ///
    /// Cần nó vì SPEC mục 2 và mục 7 đặt guard **trước** bước deserialize: khoá lạ chỉ tồn tại
    /// trong chuỗi mô hình gửi về, deserialize vào record C# là chúng bị nuốt im lặng và SG-1
    /// không bao giờ kích hoạt được nữa. <see cref="ExtractAsync"/> ở dưới đã deserialize rồi,
    /// nên nó **không** dùng được cho đường ống.
    ///
    /// Ba tình huống dưới đây phải ném **kiểu riêng**, không được nuốt thành kết quả rỗng, để
    /// <c>CardPipeline</c> phân biệt được mà không cần biết bản cài nào đang chạy (SPEC mục 4.1):
    /// <c>ExtractorQuotaException</c>, <c>ExtractorTimeoutException</c>, <c>ExtractorAuthException</c>.
    /// </summary>
    Task<RawExtraction> ExtractRawAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        string? languageHint,
        string? sourceName,
        CancellationToken ct);

    /// <summary>
    /// Chữ ký ở SPEC mục 4.1. Tiện cho lời gọi lẻ và cho bộ đo (T-08), nơi không cần guard.
    /// </summary>
    /// <param name="sourceName">
    /// Tên file gốc của ảnh (<c>ja-06.jpg</c>) — **không phải tham số riêng cho test**.
    /// <c>GeminiExtractor</c> bỏ qua nó; <c>FakeExtractor</c> dùng nó làm khoá tra
    /// <c>expected.json</c>; <c>CardPipeline</c> ghi nó vào <c>Partner.SourceImage</c>.
    /// </param>
    Task<CardExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        string? languageHint,
        string? sourceName,
        CancellationToken ct);
}
