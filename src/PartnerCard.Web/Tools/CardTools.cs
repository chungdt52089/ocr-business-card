using System.ComponentModel;
using ModelContextProtocol.Server;
using PartnerCard.Web.Audit;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;

namespace PartnerCard.Web.Tools;

/// <summary>
/// Hai tool đi qua <see cref="CardPipeline"/> — SPEC mục 10. Giao diện Blazor gọi đúng đường ống này.
///
/// Constructor cố ý **không** nhận <c>IExtractor</c> hay <c>IPartnerStore</c>: không có chúng trong tay thì
/// không có cách nào đi tắt qua Validate, Guard hay chống trùng.
///
/// **Tầng tool không ghi audit.** Đường ống đã ghi; ghi thêm ở đây là mỗi lời gọi ra hai dòng (A-01).
///
/// **Một lớp <c>catch</c> mới bọc lên đường ống** (SPEC mục 10.3), mỗi tool giữ đúng thứ tự catch của
/// phương thức đường ống nó gọi. Thứ bị giấu là stack trace, đường dẫn file, tên khoá cấu hình — **không
/// phải mã lỗi**: <c>quota_exhausted</c> đi ra nguyên vẹn.
/// </summary>
[McpServerToolType]
public sealed class CardTools(CardPipeline pipeline, SessionContext session, ILogger<CardTools> logger)
{
    private const string ExtractTool = "extract_business_card";
    private const string SaveTool = "save_partner";

    [McpServerTool(Name = ExtractTool)]
    [Description("Đọc một ảnh danh thiếp và trả về các trường đã trích xuất. Không lưu vào kho. "
               + "Ảnh truyền dưới dạng base64, tối đa 8 MB, định dạng PNG/JPEG/WEBP/HEIC/HEIF.")]
    public async Task<ExtractCardToolResult> ExtractBusinessCardAsync(
        [Description("Nội dung ảnh mã hoá base64, không kèm tiền tố data URI")] string imageBase64,
        [Description("Kiểu MIME của ảnh, ví dụ image/jpeg")] string mimeType,
        [Description("Gợi ý ngôn ngữ nếu biết trước: vi, en, ko, ja, zh")] string? languageHint = null,
        [Description("Tên file gốc của ảnh, ví dụ ja-06.jpg")] string? sourceName = null,
        CancellationToken ct = default)
    {
        try
        {
            var outcome = await pipeline.ExtractAsync(
                imageBase64, mimeType, languageHint, sourceName, ct, session.SessionId);

            return ExtractCardToolResult.From(outcome);
        }
        catch (ExtractorException ex)
        {
            // Đứng trước mắt OperationCanceledException, y như CardPipeline. Message là chuỗi cố định của ta,
            // không bao giờ là thân phản hồi của Gemini (ExtractorExceptions.cs).
            return ExtractCardToolResult.Failed(ex.Code, ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Chi tiết ở lại phía server, trên kênh chẩn đoán — không vào kết quả, không vào nhật ký audit.
            logger.LogError(ex, "Tool {Tool} gặp lỗi thoát ra khỏi đường ống.", ExtractTool);

            return ExtractCardToolResult.Failed(
                "extract_failed", "Máy chủ gặp lỗi khi xử lý ảnh này. Thử lại sau.");
        }
    }

    [McpServerTool(Name = SaveTool)]
    [Description("Lưu một hồ sơ đối tác đã được người xác nhận vào kho. Kiểm tra trùng theo email: nếu đã có hồ sơ "
               + "dùng chung email thì KHÔNG lưu, trả errorCode = duplicate kèm duplicateOf là hồ sơ đang có; "
               + "muốn vẫn tạo hồ sơ mới thì gọi lại với allowDuplicate = true. Truyền partnerId đã có để ghi đè "
               + "hồ sơ đó. Trường nào không chắc thì để chuỗi rỗng hoặc mảng rỗng, không đoán.")]
    public async Task<SavePartnerToolResult> SavePartnerAsync(
        [Description("Họ tên đầy đủ, giữ nguyên chữ viết trên danh thiếp. Chuỗi rỗng nếu không có")] string fullName,
        [Description("Chức danh. Chuỗi rỗng nếu không có")] string jobTitle,
        [Description("Tên công ty, giữ nguyên chữ viết trên danh thiếp. Chuỗi rỗng nếu không có")] string company,
        [Description("Danh sách số điện thoại như in trên thẻ. Mảng rỗng nếu không có")] string[] phones,
        [Description("Danh sách email. Mảng rỗng nếu không có")] string[] emails,
        [Description("Website của đối tác. Chuỗi rỗng nếu không có")] string website,
        [Description("Địa chỉ. Chuỗi rỗng nếu không có")] string address,
        [Description("Ngôn ngữ nhận diện được, lấy từ kết quả extract_business_card. Chuỗi rỗng nếu không rõ")] string detectedLanguage,
        [Description("Gợi ý tìm kiếm bằng chữ Latin, lấy từ kết quả extract_business_card. Chuỗi rỗng nếu không có")] string searchAlias,
        [Description("Mã hồ sơ đã có, ví dụ PTN0001, để ghi đè hồ sơ đó. Bỏ trống để tạo hồ sơ mới")] string? partnerId = null,
        [Description("Điểm tin cậy từng trường, chép nguyên fieldConfidence từ kết quả extract_business_card. Bỏ trống thì mọi trường tính 0")] Dictionary<string, double>? fieldConfidence = null,
        [Description("Tên các trường người dùng đã sửa so với kết quả trích xuất, ví dụ [\"jobTitle\"]. Bỏ trống nếu không sửa gì")] string[]? editedFields = null,
        [Description("Đặt true khi người dùng đã thấy cảnh báo trùng và vẫn chọn tạo hồ sơ mới. Mặc định false")] bool allowDuplicate = false,
        CancellationToken ct = default)
    {
        // `?? ` trên tham số không-null là cố ý: client gửi `null` tường minh thì bộ giải JSON vẫn để lọt vào.
        var card = new CardExtractionResult(
            // Lưu là hành vi sau khi người đã xác nhận tấm thẻ (US-03).
            IsBusinessCard: true,
            RejectReason: string.Empty,
            FullName: fullName ?? string.Empty,
            JobTitle: jobTitle ?? string.Empty,
            Company: company ?? string.Empty,
            Phones: phones ?? [],
            Emails: emails ?? [],
            Website: website ?? string.Empty,
            Address: address ?? string.Empty,
            DetectedLanguage: detectedLanguage ?? string.Empty,
            SearchAlias: searchAlias ?? string.Empty,
            FieldConfidence: fieldConfidence ?? new Dictionary<string, double>(StringComparer.Ordinal));

        var draft = new PartnerDraft(
            PartnerId: string.IsNullOrWhiteSpace(partnerId) ? null : partnerId.Trim(),
            Card: card,
            // Tool không nhận ảnh, nên không có ảnh gốc hay mã băm nào để ghi.
            SourceImage: string.Empty,
            ImageSha256: string.Empty,
            EditedFields: editedFields ?? [],
            AllowDuplicate: allowDuplicate);
        // Usage cố ý để Untracked: số đo của một lần trích xuất do client tự khai thì server không kiểm chứng
        // được, và ghi nó vào hồ sơ là ghi một nguồn gốc dữ liệu không ai bảo đảm.

        try
        {
            var outcome = await pipeline.SaveAsync(draft, session.SessionId, ct);

            return SavePartnerToolResult.From(outcome);
        }
        catch (OperationCanceledException)
        {
            // Nhánh lưu không gọi mô hình nên không có ExtractorException — thứ tự y như CardPipeline.SaveAsync.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tool {Tool} gặp lỗi thoát ra khỏi đường ống.", SaveTool);

            return SavePartnerToolResult.Failed("save_failed", "Không lưu được hồ sơ. Thử lại sau.");
        }
    }
}
