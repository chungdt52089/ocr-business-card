using PartnerCard.Web.Configuration;

namespace PartnerCard.Tests;

/// <summary>
/// Ca **gọi mạng thật và tiêu hạn mức**. Chỉ chạy khi biến môi trường
/// <c>GEMINI_API_KEY</c> có mặt; không có thì báo <b>Skipped</b>.
///
/// **Vì sao đặt <c>Skip</c> trong constructor chứ không kiểm bên trong thân ca:** xUnit 2.9.3
/// chưa có <c>Assert.Skip</c> động, nên kiểm bên trong rồi <c>return</c> sớm sẽ báo
/// <b>Passed</b> — một ca gọi mạng báo xanh trong lúc chưa gọi gì là đúng loại "sai mà trông
/// như đúng" mà cả dự án được dựng để chặn.
///
/// Khoá đọc từ biến môi trường của shell người chạy, **không** từ
/// <c>appsettings.Development.json</c>: test không có việc gì phải mở file chứa khoá.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            Skip = $"Cần {SecretOptions.ApiKeyVariable} trong biến môi trường. "
                 + "Ca này gọi Gemini thật và tiêu hạn mức miễn phí trong ngày.";
        }
    }

    /// <summary>Khoá đang có, hoặc <c>null</c>. Không bao giờ ghi giá trị này ra log hay assert.</summary>
    public static string? ApiKey =>
        Environment.GetEnvironmentVariable(SecretOptions.ApiKeyVariable);
}
