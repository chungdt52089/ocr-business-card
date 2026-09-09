using PartnerCard.Web.Components;

var builder = WebApplication.CreateBuilder(args);

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

// Để test dựng được host mà không phải mở public thứ gì khác.
public partial class Program;
