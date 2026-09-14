using System.ComponentModel;
using ModelContextProtocol.Server;
using PartnerCard.Web.Models;
using PartnerCard.Web.Storage;

namespace PartnerCard.Web.Tools;

/// <summary>
/// Hai tool không đi qua <c>CardPipeline</c> — SPEC mục 10.
///
/// <c>search_partners</c> **chỉ đọc** kho: không trích xuất, không ghi, nên không có mắt xích nào của đường
/// ống để đi qua. <c>enrich_partner</c> không chạm gì cả — nó là phương thức <c>static</c>, không nhận phụ
/// thuộc nào, nên về cấu trúc cũng không chạm được.
///
/// Không tool nào ở đây gọi <see cref="IPartnerStore.DeleteAsync"/> hay <see cref="IPartnerStore.UpsertAsync"/>.
/// Không có tool xoá, sửa trực tiếp hay xuất file (SPEC mục 10.4, ca M-10).
///
/// Cả hai không ghi audit: tìm kiếm không đổi trạng thái và không tốn hạn mức, còn từ khoá thường chính là
/// tên người — thứ SPEC mục 12 cấm đưa vào nhật ký.
/// </summary>
[McpServerToolType]
public sealed class PartnerTools(IPartnerStore store, ILogger<PartnerTools> logger)
{
    private const string SearchTool = "search_partners";

    [McpServerTool(Name = SearchTool)]
    [Description("Tìm hồ sơ đối tác đã lưu. Chỉ đọc, không thay đổi gì trong kho. Từ khoá khớp chuỗi con, "
               + "không phân biệt hoa thường, trên họ tên và tên công ty. Bỏ trống mọi tiêu chí thì trả các hồ sơ "
               + "cập nhật gần nhất. Mặc định 20 hồ sơ, tối đa 100.")]
    public async Task<SearchPartnersToolResult> SearchPartnersAsync(
        [Description("Từ khoá tìm trong họ tên và tên công ty. Bỏ trống để không lọc")] string? keyword = null,
        [Description("Lọc thêm theo tên công ty, khớp chuỗi con. Bỏ trống để không lọc")] string? company = null,
        [Description("Số hồ sơ tối đa, mặc định 20, tối đa 100")] int take = QueryLimits.DefaultTake,
        CancellationToken ct = default)
    {
        try
        {
            var partners = await store.SearchAsync(
                new PartnerQuery(keyword, company, QueryLimits.Normalize(take)), ct);

            return SearchPartnersToolResult.Found([.. partners.Select(PartnerDto.From)]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Chi tiết (thường mang đường dẫn file) ở lại phía server, không vào kết quả (SPEC mục 10.3).
            logger.LogError(ex, "Tool {Tool} gặp lỗi khi đọc kho.", SearchTool);

            return SearchPartnersToolResult.Failed("search_failed", "Không tìm được hồ sơ lúc này. Thử lại sau.");
        }
    }

    /// <summary>
    /// Giữ chỗ trong <c>tools/list</c> — lớp bổ sung đã cắt ngày 08/09 (SPEC mục 9, ca M-08). Mô tả nói thẳng
    /// là chưa triển khai, để agent không dựa vào nó.
    /// </summary>
    [McpServerTool(Name = "enrich_partner")]
    [Description("CHƯA TRIỂN KHAI — luôn trả errorCode = not_implemented, không đọc hay ghi gì. Chức năng bổ sung "
               + "thông tin công ty từ website của đối tác đã cắt khỏi bản MVP; tool chỉ giữ chỗ.")]
    public static EnrichPartnerToolResult EnrichPartner(
        [Description("Website của đối tác. Hiện không được dùng")] string? website = null,
        [Description("Tên công ty của đối tác. Hiện không được dùng")] string? companyName = null) =>
        new(false, "not_implemented", "Chức năng bổ sung thông tin từ website chưa được triển khai trong bản này.");
}
