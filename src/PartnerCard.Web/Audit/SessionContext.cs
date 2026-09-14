namespace PartnerCard.Web.Audit;

/// <summary>
/// Mã phiên của một lượt làm việc — SPEC mục 10.5. Là giá trị điền vào <see cref="AuditEntry.SessionId"/>.
///
/// **Đây là mã tương quan, KHÔNG phải danh tính.** Dự án không có xác thực; client gửi giá trị nào
/// cũng được, kể cả trùng với phiên khác. Nó chỉ để nối các dòng nhật ký của cùng một lượt làm việc
/// khi đọc lại <c>logs/audit-*.jsonl</c> — không để phân quyền, không để nhận dạng ai. Người về sau
/// dùng nó làm ranh giới bảo mật là hiểu sai. Mã phiên nằm trong nhật ký, nên không được chứa dữ liệu
/// cá nhân (SPEC mục 12).
///
/// Hai nguồn:
/// <list type="bullet">
/// <item>Lời gọi MCP: header <c>X-Session-Id</c>, thiếu thì tự sinh và cảnh báo (M-11). Gói MCP 2.2.0
/// chạy Stateless nên không có <c>Mcp-Session-Id</c> — header này là cách duy nhất để client giữ một mã
/// qua nhiều lời gọi.</item>
/// <item>Circuit Blazor: không có header. Scoped nghĩa là mỗi circuit một bản; mã sinh một lần lúc dựng
/// và giữ nguyên suốt circuit đó.</item>
/// </list>
///
/// **Hai kênh ghi, không trộn.** Cảnh báo thiếu header là chẩn đoán, đi qua <see cref="ILogger"/>.
/// Nó không bao giờ thành một <see cref="AuditEntry"/> — cái đó đi qua <see cref="IAuditLogger"/>.
/// </summary>
public sealed class SessionContext
{
    public const string HeaderName = "X-Session-Id";

    /// <summary>Phải khớp đường dẫn truyền cho <c>app.MapMcp</c> trong Program.cs.</summary>
    internal const string McpPath = "/mcp";

    private SessionContext(string sessionId, bool fromHeader)
    {
        SessionId = sessionId;
        FromHeader = fromHeader;
    }

    public string SessionId { get; }

    /// <summary><c>false</c> khi mã do server tự sinh vì không có header.</summary>
    public bool FromHeader { get; }

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public static SessionContext From(HttpContext? http, ILogger logger)
    {
        var header = http?.Request.Headers[HeaderName].ToString().Trim();
        if (!string.IsNullOrEmpty(header))
        {
            return new SessionContext(header, fromHeader: true);
        }

        var generated = NewId();

        // Chỉ cảnh báo trên /mcp. Ở circuit Blazor, HttpContext là null hoặc là kết nối /_blazor,
        // và thiếu header ở đó là chuyện bình thường — cảnh báo chỉ thành nhiễu.
        if (http is not null && http.Request.Path.StartsWithSegments(McpPath))
        {
            logger.LogWarning(
                "Lời gọi MCP thiếu header {Header}; đã tự sinh mã phiên {SessionId}.",
                HeaderName, generated);
        }

        return new SessionContext(generated, fromHeader: false);
    }
}

public static class SessionContextServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký <see cref="SessionContext"/> dạng scoped. Program.cs và test gọi cùng hàm này, để test
    /// kiểm đúng đăng ký thật chứ không phải một bản dựng lại.
    /// </summary>
    public static IServiceCollection AddSessionContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped(sp => SessionContext.From(
            sp.GetRequiredService<IHttpContextAccessor>().HttpContext,
            sp.GetRequiredService<ILogger<SessionContext>>()));

        return services;
    }
}
