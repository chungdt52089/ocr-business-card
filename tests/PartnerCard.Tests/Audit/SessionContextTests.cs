using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PartnerCard.Tests.Fakes;
using PartnerCard.Tests.Tools;
using PartnerCard.Web.Audit;

namespace PartnerCard.Tests.Audit;

/// <summary>
/// T-10 — <c>SessionContext</c> (SPEC mục 10.5), ca M-11.
///
/// Mã phiên là **mã tương quan để nối nhật ký, không phải danh tính**. Cảnh báo thiếu header
/// đi qua <see cref="ILogger"/>, **không** qua <c>IAuditLogger</c>.
/// </summary>
[Trait("Category", "Tool")]
public sealed class SessionContextTests
{
    private static HttpContext McpRequest(string? sessionHeader = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Post;
        http.Request.Path = "/mcp";

        if (sessionHeader is not null)
        {
            http.Request.Headers[SessionContext.HeaderName] = sessionHeader;
        }

        return http;
    }

    [Fact]
    public void M11_thieu_header_tren_mcp_tu_sinh_ma_va_ghi_canh_bao()
    {
        var logger = new RecordingLogger();

        var session = SessionContext.From(McpRequest(), logger);

        session.SessionId.Should().MatchRegex("^[0-9a-f]{12}$");
        session.FromHeader.Should().BeFalse();

        var line = logger.Entries.Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Warning);
        line.Message.Should().Contain(SessionContext.HeaderName).And.Contain(session.SessionId);

        // Mỗi lời gọi thiếu header là một mã mới — không có mã "mặc định" dùng chung.
        SessionContext.From(McpRequest(), logger).SessionId.Should().NotBe(session.SessionId);
    }

    [Fact]
    public void M11_header_chi_co_khoang_trang_coi_nhu_thieu()
    {
        var logger = new RecordingLogger();

        var session = SessionContext.From(McpRequest("   "), logger);

        session.SessionId.Should().MatchRegex("^[0-9a-f]{12}$");
        session.FromHeader.Should().BeFalse();
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void M11_co_header_thi_dung_nguyen_gia_tri_khong_canh_bao()
    {
        var logger = new RecordingLogger();

        var session = SessionContext.From(McpRequest("inspector-001"), logger);

        session.SessionId.Should().Be("inspector-001");
        session.FromHeader.Should().BeTrue();
        logger.Entries.Should().BeEmpty();
    }

    /// <summary>
    /// Bản T-10 kiểm bằng tool <c>ping</c>; T-11 gỡ <c>ping</c> nên kiểm bằng một tool thật — và nay có
    /// nhật ký, nên soi được luôn mã tự sinh có thật sự đi tới dòng audit hay không.
    /// </summary>
    [Fact]
    public async Task M11_tool_van_chay_khi_thieu_header()
    {
        var session = SessionContext.From(McpRequest(), new RecordingLogger());
        using var harness = new ToolHarness(session: session);

        var result = await harness.ExtractAsync("en-01.png");

        result.Ok.Should().BeTrue();

        using var line = JsonDocument.Parse(harness.LogLines().Single());
        line.RootElement.GetProperty("sessionId").GetString()
            .Should().Be(session.SessionId).And.MatchRegex("^[0-9a-f]{12}$");
    }

    /// <summary>
    /// Mô phỏng Blazor: mỗi circuit là một DI scope và không có header. Dùng chính
    /// <c>AddSessionContext()</c> mà Program.cs gọi, không dựng lại đăng ký riêng cho test.
    /// </summary>
    [Fact]
    public void M11_moi_scope_mot_ma_giu_nguyen_trong_scope()
    {
        var logger = new RecordingLogger();
        using var provider = new ServiceCollection()
            .AddLogging(b => b.AddProvider(logger))
            .AddSessionContext()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        string circuitA;
        using (var scope = provider.CreateScope())
        {
            var first = scope.ServiceProvider.GetRequiredService<SessionContext>();
            var again = scope.ServiceProvider.GetRequiredService<SessionContext>();

            again.SessionId.Should().Be(first.SessionId);
            circuitA = first.SessionId;
        }

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SessionContext>()
                .SessionId.Should().NotBe(circuitA);
        }

        // Không có HttpContext thì không phải lời gọi MCP — thiếu header là bình thường, không cảnh báo.
        logger.Entries.Should().NotContain(l => l.Level >= LogLevel.Warning);
    }

    /// <summary>
    /// Đăng ký DI phải đọc header của **request hiện tại** qua <see cref="IHttpContextAccessor"/>.
    /// Thiếu ca này thì một đăng ký lỡ truyền <c>null</c> vẫn qua được mọi ca ở trên.
    /// </summary>
    [Fact]
    public void M11_dang_ky_DI_doc_header_cua_request_hien_tai()
    {
        using var provider = new ServiceCollection()
            .AddLogging(b => b.AddProvider(new RecordingLogger()))
            .AddSessionContext()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = McpRequest("inspector-002");

        try
        {
            using var scope = provider.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<SessionContext>();

            session.SessionId.Should().Be("inspector-002");
            session.FromHeader.Should().BeTrue();
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }
}
