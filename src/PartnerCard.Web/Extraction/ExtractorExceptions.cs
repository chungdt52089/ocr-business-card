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
/// <c>429 RESOURCE_EXHAUSTED</c> vì **trần ngày (RPD)** đã cạn.
///
/// **Không bao giờ thử lại tự động.** Thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế, và trong
/// buổi demo thì nó còn khiến người dùng chụp lại thêm vài lần nữa.
/// </summary>
public sealed class ExtractorQuotaException(Exception? inner = null)
    : ExtractorException("quota_exhausted", "Đã hết hạn mức gọi mô hình hôm nay.", inner);

/// <summary>
/// <c>429 RESOURCE_EXHAUSTED</c> vì **trần phút (RPM)**, không phải trần ngày.
///
/// **Hai thứ này cùng một mã HTTP nhưng đối lập nhau về vòng đời**, nên gộp chúng là hỏng cả hai
/// đầu: trần phút hết sau chưa tới một phút, còn trần ngày kéo tới khi reset (SPEC mục 4.6).
///
/// | | Trần phút | Trần ngày |
/// |---|---|---|
/// | Chờ bao lâu thì hết | &lt; 60 giây | Tới nửa đêm giờ Thái Bình Dương |
/// | Gọi tiếp có ích không | Có, sau khi chờ | Không, và tiêu thêm request |
///
/// Coi trần phút là trần ngày thì lượt đo dừng ngay ở tấm thứ sáu trong khi chỉ cần chờ. Coi trần
/// ngày là trần phút thì ngồi chờ 18 lần vô ích. Vì vậy <c>GeminiExtractor</c> đọc
/// <c>QuotaFailure</c> trong thân phản hồi để tách hai ca, và **không đọc được thì coi là trần
/// ngày** — dừng nhầm thì chạy lại một lượt, còn đi tiếp nhầm thì cả bảng số thành rác.
/// </summary>
public sealed class ExtractorRateLimitException(Exception? inner = null)
    : ExtractorException("rate_limited", "Đang gọi nhanh quá hạn mức cho phép. Thử lại sau ít giây.", inner);

/// <summary>
/// Mô hình không trả lời kịp <c>ExtractTimeoutSeconds</c>.
///
/// Bản cài phải tự đổi timeout **của chính nó** sang kiểu này, và chỉ khi <c>ct</c> của người gọi
/// chưa bị huỷ — huỷ thật thì để nguyên cho nó thoát ra.
/// </summary>
public sealed class ExtractorTimeoutException(Exception? inner = null)
    : ExtractorException("extract_timeout", "Mô hình phản hồi quá chậm. Thử lại sau vài giây.", inner);

/// <summary>
/// Cả họ <c>5xx</c> — dịch vụ phía Google trục trặc, **không phải lỗi của ta**.
///
/// Tách khỏi <see cref="ExtractorQuotaException"/> vì hai mã này xử lý **ngược nhau**, và nhầm
/// hướng nào cũng tốn:
/// <list type="bullet">
/// <item><c>5xx</c> là **tạm thời** — thử lại một lần thường là ăn, và không tốn gì thêm nếu
/// không ăn.</item>
/// <item><c>429</c> là **hết hạn mức trong ngày** — thử lại chỉ tiêu thêm quota mà kết quả
/// vẫn thế.</item>
/// </list>
///
/// Cũng không được lẫn vào <c>extract_failed</c>: câu "thử chụp lại rõ hơn" bảo người dùng làm
/// đúng việc vô ích, trong khi thứ họ cần làm là đợi vài giây.
/// </summary>
public sealed class ExtractorUnavailableException(Exception? inner = null)
    : ExtractorException("extract_unavailable", "Dịch vụ đang quá tải. Thử lại sau vài giây.", inner);

/// <summary>
/// <c>400 API_KEY_INVALID</c> — vấn đề **cấu hình**, không phải lỗi tấm ảnh.
///
/// Tách riêng vì thông báo "thử chụp lại rõ hơn" ở đây là sai hướng hoàn toàn: chụp bao nhiêu lần
/// cũng không sửa được một cái khoá hỏng.
/// </summary>
public sealed class ExtractorAuthException(Exception? inner = null)
    : ExtractorException("extractor_auth", "Khoá API không dùng được. Kiểm tra lại cấu hình.", inner);
