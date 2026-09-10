using Microsoft.Extensions.Options;
using PartnerCard.Web.Configuration;

namespace PartnerCard.Web.Extraction;

/// <summary>
/// Chọn bản cài <see cref="IExtractor"/> theo cấu hình — SPEC mục 4.1.
///
/// Hàm này từng là một hàm cục bộ ở cuối <c>Program.cs</c>. T-08 kéo nó ra đây vì bộ đo cần đúng
/// phép chọn ấy, mà một hàm cục bộ thì không gọi lại được. Chép sang bộ đo một bản thứ hai là mở
/// đường cho hai bản lệch nhau: hôm nào <c>Program.cs</c> đổi cách dựng <c>GeminiExtractor</c> mà
/// bộ đo không đổi theo, con số đo được sẽ không còn là con số của thứ đang chạy thật.
/// </summary>
public static class ExtractorFactory
{
    /// <param name="expectedJsonPath">
    /// Đường tới <c>expected.json</c> cho chế độ <c>fake</c>. Hai người gọi đưa hai đường khác
    /// nhau, và đó là đúng: ứng dụng đọc bản đã chép cạnh binary nên chạy được từ thư mục làm việc
    /// bất kỳ, còn bộ đo đọc thẳng bản trong repo vì nó chỉ chạy trong repo.
    /// </param>
    /// <param name="http">
    /// Chỉ dùng ở chế độ <c>gemini</c>. Không truyền thì tự dựng một cái. Dùng lại cho cả vòng đời
    /// tiến trình: chỉ có một endpoint, và tạo mới mỗi lời gọi là bỏ phí bắt tay TLS rồi đọng
    /// socket ở TIME_WAIT.
    /// </param>
    public static IExtractor Create(
        PartnerCardOptions options,
        SecretOptions secrets,
        string expectedJsonPath,
        HttpClient? http = null)
    {
        if (options.RequiresApiKey)
        {
            // Timeout để vô hạn là cố ý: nguồn hạn giờ duy nhất là CancellationTokenSource trong
            // GeminiExtractor, vì chỉ nó phân biệt được timeout của mình với việc người dùng huỷ —
            // HttpClient.Timeout thì không (SPEC mục 4.1).
            http ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            return new GeminiExtractor(http, secrets, Options.Create(options));
        }

        return new FakeExtractor(ExpectedCards.LoadFrom(expectedJsonPath));
    }

    /// <summary>Bản đáp án mà csproj chép sang cạnh binary — đường của ứng dụng.</summary>
    public static string DefaultExpectedJsonPath =>
        Path.Combine(AppContext.BaseDirectory, "fakedata", "expected.json");
}
