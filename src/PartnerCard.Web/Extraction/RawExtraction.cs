namespace PartnerCard.Web.Extraction;

/// <summary>
/// Thứ <c>ExtractRawAsync</c> trả về: **chuỗi JSON thô** mô hình gửi, kèm số đo của lần gọi.
///
/// Bản đầu của chữ ký chỉ trả <c>string</c>, và khi đó <c>usageMetadata</c> của Gemini không có
/// chỗ nào để đi ra — trong khi SPEC mục 12 đòi ghi <c>tokensIn</c>, <c>tokensOut</c> và
/// <c>latencyMs</c> vào nhật ký (SPEC mục 4.1, sửa 10/09).
/// </summary>
public sealed record RawExtraction(string Json, ExtractionUsage Usage);

/// <summary>
/// Số đo của một lần gọi mô hình — SPEC mục 4.1 và mục 12.
///
/// **Không bao giờ là <c>null</c>.** Một cách biểu diễn duy nhất cho "không có số đo"; để cả
/// <c>null</c> lẫn một giá trị rỗng cùng tồn tại là mầm của <c>if (x is null || x.IsEmpty)</c>
/// rải khắp nơi vài tuần sau.
///
/// <see cref="Model"/> phân biệt hai chuyện khác nhau: <c>"fake"</c> nghĩa là
/// <c>FakeExtractor</c> **đã chạy thật**, chỉ là nó không tốn token nào; còn <c>"-"</c> của
/// <see cref="Untracked"/> nghĩa là **không ai chạy cả**. Hồ sơ nhập tay mà ghi
/// <c>model: "fake"</c> là nói dối về nguồn gốc dữ liệu.
/// </summary>
public sealed record ExtractionUsage(
    int TokensIn,
    int TokensOut,
    int LatencyMs,
    string Model,
    string PromptVersion)
{
    /// <summary>Không có lần gọi mô hình nào — hồ sơ nhập tay, hoặc gọi thẳng <c>save_partner</c>.</summary>
    public static readonly ExtractionUsage Untracked = new(0, 0, 0, "-", "-");
}
