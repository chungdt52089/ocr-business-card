using PartnerCard.Web.Audit;
using PartnerCard.Web.Components;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PartnerCardOptions>(
    builder.Configuration.GetSection(PartnerCardOptions.SectionName));

var options = builder.Configuration
    .GetSection(PartnerCardOptions.SectionName)
    .Get<PartnerCardOptions>() ?? new PartnerCardOptions();

// Đường dẫn tương đối tính theo ContentRootPath chứ không theo working directory, để
// `dotnet run --project src/PartnerCard.Web` chạy từ đâu cũng trỏ đúng Code\data\ (SPEC mục 13).
var dataDirectory = Path.IsPathRooted(options.DataDirectory)
    ? options.DataDirectory
    : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, options.DataDirectory));

builder.Services.AddSingleton(TimeProvider.System);

try
{
    // Kiểm cấu hình và khoá trước tiên: thiếu khoá ở chế độ gemini thì chết ngay (I-07).
    var secrets = SecretLoader.Load(builder.Configuration, options);
    builder.Services.AddSingleton(secrets);

    // Nạp một lần lúc khởi động. File hỏng thì chết ngay kèm thông báo rõ (S-12).
    builder.Services.AddSingleton<IPartnerStore>(
        JsonPartnerStore.LoadFrom(dataDirectory, TimeProvider.System));

    builder.Services.AddSingleton(ExtractorFactory.Create(
        options, secrets, ExtractorFactory.DefaultExpectedJsonPath));
}
catch (InvalidOperationException ex)
{
    // Thông báo của SecretLoader và JsonPartnerStore đã đủ rõ để tự đứng một mình,
    // và cả hai đều không chứa giá trị khoá.
    Console.Error.WriteLine($"Không khởi động được: {ex.Message}");
    return 1;
}

builder.Services.AddSingleton<ISchemaGuard, SchemaGuard>();

// Bản ghi JSONL thật là T-11; tới đó thay dòng này.
builder.Services.AddSingleton<IAuditLogger, NullAuditLogger>();

// Giao diện Blazor và tool MCP gọi cùng class này, không có đường đi riêng (SPEC mục 2).
builder.Services.AddSingleton<CardPipeline>();

// Mã phiên để nối nhật ký (SPEC mục 10.5): scoped — mỗi lời gọi MCP một bản, mỗi circuit Blazor một bản.
builder.Services.AddSessionContext();

// Tool MCP (SPEC mục 10.2). Gói 2.2.0 mặc định Stateless: không có Mcp-Session-Id, nên X-Session-Id
// là thứ duy nhất nối các lời gọi của cùng một lượt làm việc. Đổi SessionMode là lệch SPEC — hỏi trước.
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

// Kiểm tra sống (SPEC mục 13). Mở đường dẫn này bằng điện thoại qua http://<IP-LAN>:5080/health
// là cách rẻ nhất để biết cả ba thứ đều đúng: bind 0.0.0.0, tường lửa cổng 5080, cùng Wi-Fi.
// Làm ở T-01 chứ không đợi T-12 — hỏng thì biết sớm mười ngày (SPEC mục 18.4).
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Phải khớp SessionContext.McpPath — chỉ lời gọi dưới đường dẫn này mới bị cảnh báo khi thiếu X-Session-Id.
app.MapMcp("/mcp");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

return 0;

// Để test dựng được host mà không phải mở public thứ gì khác.
public partial class Program;
