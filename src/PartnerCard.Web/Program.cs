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
// Dữ liệu và nhật ký đi qua cùng một hàm: hai luật giải đường dẫn là mầm của hai thư mục lệch nhau.
string UnderContentRoot(string path) =>
    Path.IsPathRooted(path)
        ? path
        : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, path));

var dataDirectory = UnderContentRoot(options.DataDirectory);
var logsDirectory = UnderContentRoot(options.LogsDirectory);

builder.Services.AddSingleton(TimeProvider.System);

try
{
    // Kiểm cấu hình và khoá trước tiên: thiếu khoá ở chế độ gemini thì chết ngay (I-07).
    var secrets = SecretLoader.Load(builder.Configuration, options);
    builder.Services.AddSingleton(secrets);

    // Nạp mồi TRƯỚC LoadFrom, không phải sau (SPEC mục 19.3). Chép file mồi vào thư mục dữ liệu rồi
    // mới nạp kho, để LoadFrom tính mã tiếp theo bằng max(bộ đếm, mã cao nhất trong kho) và thấy luôn
    // mã của mồi. Nạp sau thì mã tiếp theo đã chốt từ bộ đếm cũ, và hồ sơ mới đầu tiên đè lên một hồ
    // sơ mồi mà không báo gì.
    var seed = PartnerSeed.EnsureSeeded(dataDirectory, PartnerSeed.DefaultSeedPath);

    // Một dòng duy nhất cho log khởi động, và nó đáng giá đúng lúc không ai xem được đĩa máy chủ:
    // nói luôn có nạp mồi hay không, và hai thư mục ghi ra đĩa nằm ở đâu sau khi giải đường dẫn.
    // Đường dẫn tương đối tự giải theo thư mục cài đặt là lỗi im lặng hay gặp nhất khi chạy trong
    // container — JsonlAuditLogger nuốt lỗi ghi theo thiết kế, nên audit mất mà không ai biết.
    Console.WriteLine(
        $"{seed.Message} · thư mục dữ liệu: {dataDirectory} · thư mục nhật ký: {logsDirectory}");

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

// Ảnh gốc trên đĩa — data/images/<sha256>.jpg (SPEC mục 4.1). Dùng chung biến dataDirectory với
// JsonPartnerStore: hai luật giải đường dẫn là mầm của hai thư mục lệch nhau.
//
// Đường ống KHÔNG gọi nó. Nhánh trích xuất không persist gì (SPEC mục 2), nên chỗ ghi ảnh là
// tầng giao diện — nơi có byte ảnh trong tay, và chỉ ghi sau khi Validate đã cho qua.
builder.Services.AddSingleton(new ImageStore(dataDirectory));

// Nhật ký JSONL (SPEC mục 12): ghi thẳng, không hàng đợi. Singleton — giao diện Blazor và tool MCP dùng
// chung một instance, tức chung một khoá ghi.
builder.Services.AddSingleton<IAuditLogger>(sp => new JsonlAuditLogger(
    logsDirectory,
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<JsonlAuditLogger>>()));

// Giao diện Blazor và tool MCP gọi cùng class này, không có đường đi riêng (SPEC mục 2).
builder.Services.AddSingleton<CardPipeline>();

// Mã phiên để nối nhật ký (SPEC mục 10.5): scoped — mỗi lời gọi MCP một bản, mỗi circuit Blazor một bản.
builder.Services.AddSessionContext();

// Tool MCP (SPEC mục 10.2). Gói 2.2.0 mặc định Stateless: không có Mcp-Session-Id, nên X-Session-Id
// là thứ duy nhất nối các lời gọi của cùng một lượt làm việc. Đổi SessionMode là lệch SPEC — hỏi trước.
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents()
    // Blazor Server chuyển chuỗi base64 của ảnh qua SignalR, và mặc định của HubOptions là 32 KB
    // — chặn ngay tấm ảnh đầu tiên (SPEC mục 11.3). 1600px q80 thường dưới 400 KB nên 2 MB là dư.
    //
    // Trần này áp cho CHUỖI BASE64, không phải byte JPEG: base64 dài hơn 1,333 lần, nên 1,5 MB
    // JPEG là đúng 2,0 MB trên dây. Trang chụp in cả hai con số vì lý do đó.
    .AddHubOptions(hub => hub.MaximumReceiveMessageSize = 2 * 1024 * 1024);

var app = builder.Build();

app.UseAntiforgery();

// MapStaticAssets thay UseStaticFiles: mỗi file trong wwwroot có thêm một URL mang dấu vân tay nội
// dung (js/capture.<hash>.js), phục vụ kèm Cache-Control immutable. Đổi nội dung là đổi URL, nên
// trình duyệt không bao giờ chạy capture.js của bản build trước. Với UseStaticFiles, tên file không
// đổi theo phiên bản: điện thoại đã mở trang giữ bản cũ trong cache, import nạp một module không có
// init, và circuit hỏng ngay lần render đầu. Danh mục URL đọc từ
// PartnerCard.Web.staticwebassets.endpoints.json, file đi cùng bản publish.
app.MapStaticAssets();

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
