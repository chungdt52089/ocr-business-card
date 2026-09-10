namespace PartnerCard.Web.Extraction;

/// <summary>
/// Lỗi mà <c>CardPipeline</c> phân biệt được — SPEC mục 4.1 và 4.6.
///
/// Nằm cạnh <see cref="IExtractor"/> chứ không nằm trong <c>GeminiExtractor</c>, vì nó là **một
/// phần của hợp đồng**: đường ống bắt ba kiểu con mà không cần biết bản cài nào đang chạy, và
/// bản thay thế sau này (BRD mục 4) phải ném đúng ba kiểu đó.
///
/// **Cố ý không kế thừa <see cref="OperationCanceledException"/>.** <c>CardPipeline</c> có một
/// mắt <c>catch (OperationCanceledException) { throw; }</c> để việc người dùng huỷ vẫn thoát ra
/// được; kiểu nào kế thừa nó sẽ rơi đúng vào mắt ấy và bay ra khỏi đường ống, trái mục 10.3.
///
/// <see cref="Exception.Message"/> luôn là **chuỗi cố định của ta**. Không bao giờ nhét thân
/// phản hồi của Gemini vào đây: nó đi thẳng ra màn hình người dùng.
/// </summary>
public abstract class ExtractorException : Exception
{
    protected ExtractorException(string code, string userMessage, Exception? inner = null)
        : base(userMessage, inner) => Code = code;

    /// <summary>Mã lỗi đường ống trả về và ghi vào nhật ký — mục 12.</summary>
    public string Code { get; }
}

/// <summary>
/// <c>429 RESOURCE_EXHAUSTED</c> — hết hạn mức trong ngày.
///
/// **Không bao giờ thử lại tự động.** Thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế, và trong
/// buổi demo thì nó còn khiến người dùng chụp lại thêm vài lần nữa.
/// </summary>
public sealed class ExtractorQuotaException(Exception? inner = null)
    : ExtractorException("quota_exhausted", "Đã hết hạn mức gọi mô hình hôm nay.", inner);

/// <summary>
/// Mô hình không trả lời kịp <c>ExtractTimeoutSeconds</c>.
///
/// Bản cài phải tự đổi timeout **của chính nó** sang kiểu này, và chỉ khi <c>ct</c> của người gọi
/// chưa bị huỷ — huỷ thật thì để nguyên cho nó thoát ra.
/// </summary>
public sealed class ExtractorTimeoutException(Exception? inner = null)
    : ExtractorException("extract_timeout", "Mô hình phản hồi quá chậm. Thử lại sau vài giây.", inner);

/// <summary>
/// <c>400 API_KEY_INVALID</c> — vấn đề **cấu hình**, không phải lỗi tấm ảnh.
///
/// Tách riêng vì thông báo "thử chụp lại rõ hơn" ở đây là sai hướng hoàn toàn: chụp bao nhiêu lần
/// cũng không sửa được một cái khoá hỏng.
/// </summary>
public sealed class ExtractorAuthException(Exception? inner = null)
    : ExtractorException("extractor_auth", "Khoá API không dùng được. Kiểm tra lại cấu hình.", inner);
