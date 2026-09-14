using System.ComponentModel;
using ModelContextProtocol.Server;
using PartnerCard.Web.Audit;

namespace PartnerCard.Web.Tools;

/// <summary>
/// **Tool tạm của T-10 — T-11 xoá file này.** Chỉ để xác nhận MCP Inspector kết nối được tới
/// <c>/mcp</c> và header <c>X-Session-Id</c> tới được <see cref="SessionContext"/>.
///
/// Không được sống sót qua T-11: <c>tools/list</c> phải trả đúng 4 tool (SPEC mục 10.4, ca M-01).
/// </summary>
[McpServerToolType]
public sealed class PingTool(SessionContext session)
{
    [McpServerTool(Name = "ping")]
    [Description("Kiểm tra kết nối tới máy chủ PartnerCard. Trả về 'pong' kèm mã phiên máy chủ ghi nhận "
               + "cho lời gọi này — lấy từ header X-Session-Id, thiếu thì máy chủ tự sinh.")]
    public string Ping() => $"pong · sessionId={session.SessionId}";
}
