namespace PartnerCard.Web.Extraction;

/// <summary>
/// Schema đầu ra của mô hình — SPEC mục 4.4. Khai báo **một chỗ duy nhất**, vì cùng bộ danh sách
/// này vừa là schema gửi cho Gemini (T-07) vừa là nguồn sự thật cho SG-1, SG-2 và SG-3 (T-06).
/// Hai bản sao lệch nhau là cách chắc chắn nhất để guard chặn nhầm hoặc bỏ lọt.
/// </summary>
public static class CardSchema
{
    /// <summary>Mọi khoá hợp lệ. Khoá nào ngoài danh sách này thì SG-1 bỏ đi.</summary>
    public static readonly IReadOnlySet<string> AllowedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "isBusinessCard",
        "rejectReason",
        "fullName",
        "jobTitle",
        "company",
        "phones",
        "emails",
        "website",
        "address",
        "detectedLanguage",
        "searchAlias",
        "fieldConfidence",
    };

    /// <summary>Thiếu một trong các khoá này thì SG-2 chặn, không lưu.</summary>
    public static readonly IReadOnlyList<string> RequiredKeys =
    [
        "isBusinessCard",
        "fullName",
        "company",
        "phones",
        "emails",
        "detectedLanguage",
        "fieldConfidence",
    ];

    /// <summary>
    /// Tám trường **bắt buộc có confidence** khi mang giá trị — SG-3.
    /// </summary>
    public static readonly IReadOnlyList<string> ConfidenceRequiredFields =
    [
        "fullName",
        "jobTitle",
        "company",
        "phones",
        "emails",
        "website",
        "address",
        "searchAlias",
    ];

    /// <summary>
    /// Ba trường **miễn trừ** SG-3: chúng mang giá trị mà không cần confidence, và đó là đúng.
    /// Thiếu danh sách này thì SG-3 chặn mọi tấm thẻ hợp lệ, vì <c>detectedLanguage</c> luôn có giá trị.
    /// </summary>
    public static readonly IReadOnlySet<string> ConfidenceExemptFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "isBusinessCard",  // boolean phân loại, không phải chữ đọc từ thẻ
        "rejectReason",    // mô hình tự viết khi từ chối, không trích từ thẻ
        "detectedLanguage" // nhãn phân loại, không phải nội dung
    };
}
