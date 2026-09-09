namespace PartnerCard.Web.Configuration;

/// <summary>
/// Khoá bí mật đã nạp. Cố ý là <c>class</c> chứ không phải <c>record</c>: record sinh sẵn
/// <c>ToString()</c> in ra mọi thành phần, và một dòng log vô ý là đủ để khoá lên remote công khai.
/// </summary>
public sealed class SecretOptions
{
    /// <summary>Tên khoá trong <c>appsettings.Development.json</c> — SPEC mục 13.</summary>
    public const string ApiKeyVariable = "GEMINI_API_KEY";

    /// <summary>Rỗng khi chạy chế độ <c>fake</c>, vì chế độ đó không gọi mạng.</summary>
    public string? GeminiApiKey { get; init; }
}

/// <summary>
/// Kiểm cấu hình lúc khởi động. Thiếu khoá ở chế độ <c>gemini</c> thì ném ngay để tiến trình
/// chết kèm thông báo rõ — SPEC mục 13. Cố ý chết sớm: chạy tiếp rồi hỏng ở lần gọi mô hình
/// đầu tiên trông giống hệt lỗi mạng, và trong buổi demo thì không kịp phân biệt.
///
/// **Chỉ xét khoá có mặt và đúng định dạng. Không gọi mạng.** Ca I-07, I-08.
/// </summary>
public static class SecretLoader
{
    /// <summary>
    /// Ngắn hơn mức này thì chắc chắn không phải khoá thật — bắt được ca dán nhầm chuỗi giữ chỗ.
    /// Không kiểm tiền tố <c>AIza</c>: đó là chi tiết Google có thể đổi, và từ chối nhầm một
    /// khoá hợp lệ thì tệ hơn nhận một khoá sai, vì lần gọi đầu tiên sẽ nói rõ khoá sai.
    /// </summary>
    private const int MinApiKeyLength = 20;

    public static SecretOptions Load(IConfiguration configuration, PartnerCardOptions options)
    {
        var extractor = options.Extractor?.Trim() ?? string.Empty;

        var known = extractor.Equals(ExtractorNames.Fake, StringComparison.OrdinalIgnoreCase)
            || extractor.Equals(ExtractorNames.Gemini, StringComparison.OrdinalIgnoreCase);

        if (!known)
        {
            // Gõ sai thành "gemni" mà im lặng rơi về fake là cái bẫy tệ nhất ở đây:
            // buổi demo sẽ chạy bằng đáp án dựng sẵn mà không ai biết.
            throw new InvalidOperationException(
                $"{PartnerCardOptions.SectionName}:Extractor = \"{extractor}\" không hợp lệ. " +
                $"Chỉ nhận \"{ExtractorNames.Fake}\" hoặc \"{ExtractorNames.Gemini}\".");
        }

        if (!options.RequiresApiKey)
        {
            // Chế độ fake không cần khoá — để người chấm clone về chạy được mà không cần
            // tài khoản Google, và để toàn bộ test chạy khi đã ngắt mạng (I-08, I-09).
            return new SecretOptions();
        }

        var apiKey = configuration[SecretOptions.ApiKeyVariable];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Thiếu {SecretOptions.ApiKeyVariable}. " +
                $"Chế độ {PartnerCardOptions.SectionName}:Extractor = \"{ExtractorNames.Gemini}\" cần khoá Gemini. " +
                "Đặt khoá trong src/PartnerCard.Web/appsettings.Development.json (file này nằm trong .gitignore), " +
                $"hoặc đổi Extractor sang \"{ExtractorNames.Fake}\" để chạy không cần khoá.");
        }

        if (apiKey.Any(char.IsWhiteSpace) || apiKey.Length < MinApiKeyLength)
        {
            // Thông báo không bao giờ nhắc lại giá trị khoá, kể cả một phần.
            throw new InvalidOperationException(
                $"{SecretOptions.ApiKeyVariable} không đúng định dạng: cần tối thiểu {MinApiKeyLength} ký tự " +
                "và không chứa khoảng trắng. Kiểm tra lại chuỗi đã dán.");
        }

        return new SecretOptions { GeminiApiKey = apiKey };
    }
}
