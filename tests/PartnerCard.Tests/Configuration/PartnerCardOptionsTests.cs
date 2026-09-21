using Microsoft.Extensions.Configuration;
using PartnerCard.Web.Configuration;

namespace PartnerCard.Tests.Configuration;

/// <summary>
/// <c>LogsDirectory</c> — SPEC mục 13, thêm ở T-18.
///
/// Trước T-18 thư mục nhật ký là một hằng, không đổi được qua cấu hình. Ca này giữ hai nửa của
/// khoá mới: mặc định vẫn trỏ <c>Code\logs\</c> khi chạy cục bộ, và biến môi trường đè được —
/// nếu không đè được thì trên máy chủ nó giải thành một chỗ tiến trình không ghi nổi, mà
/// <c>JsonlAuditLogger</c> nuốt lỗi ghi theo thiết kế nên audit **mất âm thầm**.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PartnerCardOptionsTests
{
    [Fact]
    public void LogsDirectory_co_mac_dinh_tro_ve_Code_logs_va_cau_hinh_de_duoc()
    {
        new PartnerCardOptions().LogsDirectory.Should().Be("../../logs");

        // Đúng dạng biến môi trường dùng khi deploy: PartnerCard__LogsDirectory.
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(
                $"{PartnerCardOptions.SectionName}:LogsDirectory", "/tmp/scanie/logs")])
            .Build()
            .GetSection(PartnerCardOptions.SectionName)
            .Get<PartnerCardOptions>();

        options!.LogsDirectory.Should().Be("/tmp/scanie/logs");
    }
}
