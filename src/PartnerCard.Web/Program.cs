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
    builder.Services.AddSingleton(SecretLoader.Load(builder.Configuration, options));

    // Nạp một lần lúc khởi động. File hỏng thì chết ngay kèm thông báo rõ (S-12).
    builder.Services.AddSingleton<IPartnerStore>(
        JsonPartnerStore.LoadFrom(dataDirectory, TimeProvider.System));

    builder.Services.AddSingleton<IExtractor>(SelectExtractor(options));
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

return 0;

// Chọn bản cài IExtractor theo cấu hình (SPEC mục 4.1). SecretLoader đã loại giá trị lạ,
// nên tới đây chỉ còn hai nhánh hợp lệ.
static IExtractor SelectExtractor(PartnerCardOptions options)
{
    if (options.RequiresApiKey)
    {
        throw new InvalidOperationException(
            "Chưa có GeminiExtractor — nó thuộc T-07. " +
            $"Đặt {PartnerCardOptions.SectionName}:Extractor = \"{ExtractorNames.Fake}\" " +
            "để chạy đường ống offline.");
    }

    // Bảng đáp án được csproj chép sang output, nên tìm theo thư mục chứa binary
    // chứ không theo working directory.
    return new FakeExtractor(ExpectedCards.LoadFrom(
        Path.Combine(AppContext.BaseDirectory, "fakedata", "expected.json")));
}

// Để test dựng được host mà không phải mở public thứ gì khác.
public partial class Program;
