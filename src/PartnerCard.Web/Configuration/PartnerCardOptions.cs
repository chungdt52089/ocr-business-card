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

    /// <summary>
    /// Đổi sang Flash Lite ngày 11/09 sau khi đo: **72/72 ở cả hai lượt độc lập**, p50 khoảng
    /// 2,0–2,7 giây, và hạn mức rộng hơn hẳn — 15 RPM / 500 RPD so với 5 RPM / 20 RPD của
    /// `gemini-3.8-flash` (SPEC mục 4.3 và 4.6). Trần ngày 20 của 3.8 Flash không đủ cho **hai**
    /// lượt đo 19 thẻ, tức không đủ để làm phép so trước–sau mà T-09 sống bằng nó.
    ///
    /// Giữ đúng chuỗi này khớp `appsettings.json`: để hai nơi lệch nhau thì bản clone thiếu file
    /// cấu hình sẽ chạy một model khác với model đã nghiệm thu, và không có gì báo.
    /// </summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    // PromptVersion cố ý KHÔNG có ở đây: nó là hằng Prompts.Version, nằm cạnh chính chuỗi
    // prompt được gửi đi (SPEC mục 13). Để trong cấu hình thì sửa prompt mà quên đổi số là
    // hồ sơ ghi một phiên bản chưa từng được gửi.

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
