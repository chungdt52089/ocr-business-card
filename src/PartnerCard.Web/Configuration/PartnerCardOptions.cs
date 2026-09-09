namespace PartnerCard.Web.Configuration;

/// <summary>Khối cấu hình <c>PartnerCard</c> — SPEC mục 13.</summary>
public sealed class PartnerCardOptions
{
    public const string SectionName = "PartnerCard";

    /// <summary>
    /// Thư mục chứa <c>partners.json</c>, <c>counter.json</c> và <c>images/</c>.
    /// Đường dẫn tương đối tính theo <c>ContentRootPath</c>, nên mặc định <c>../../data</c>
    /// trỏ đúng <c>Code\data\</c> khi chạy <c>dotnet run --project src/PartnerCard.Web</c>.
    /// Test luôn truyền thư mục tạm.
    /// </summary>
    public string DataDirectory { get; set; } = "../../data";

    /// <summary>
    /// <c>fake</c> hoặc <c>gemini</c> — SPEC mục 4.1. Mặc định <c>fake</c> cố ý: bản clone mới
    /// không có <c>appsettings.Development.json</c> nên cũng không có khoá, và ta muốn người
    /// chấm chạy được ngay thay vì gặp một tiến trình từ chối khởi động.
    /// </summary>
    public string Extractor { get; set; } = ExtractorNames.Fake;

    public string Model { get; set; } = "gemini-3.8-flash";

    public string PromptVersion { get; set; } = "v1";

    /// <summary>Trần 8 MB theo NFR-4, nằm an toàn dưới trần 20 MB của Gemini (SPEC mục 4.2).</summary>
    public int MaxImageBytes { get; set; } = 8 * 1024 * 1024;

    public int ExtractTimeoutSeconds { get; set; } = 20;

    /// <summary>Dưới ngưỡng này thì màn hình xác nhận tô trường lên — 0,9 theo D-4.</summary>
    public double ConfidenceReviewThreshold { get; set; } = 0.9;

    /// <summary>Đúng chế độ gọi mô hình thật, tức là bắt buộc phải có khoá.</summary>
    public bool RequiresApiKey =>
        string.Equals(Extractor, ExtractorNames.Gemini, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Hai giá trị hợp lệ của <see cref="PartnerCardOptions.Extractor"/>.</summary>
public static class ExtractorNames
{
    public const string Fake = "fake";
    public const string Gemini = "gemini";
}
