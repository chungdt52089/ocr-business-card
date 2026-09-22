using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;

namespace PartnerCard.Web.Components.Review;

/// <summary>
/// Một dòng trong danh sách <c>phones</c>/<c>emails</c>. Class chứ không phải chuỗi: <c>@bind</c> trong
/// <c>foreach</c> cần chỗ để ghi vào, và <c>@key</c> cần danh tính ổn định khi xoá một dòng ở giữa.
/// </summary>
public sealed class EditableValue(string value = "")
{
    public string Value { get; set; } = value;
}

/// <summary>Nhãn của một ô trên màn hình xác nhận — BACKLOG T-13, "Quyết định cần chốt".</summary>
public enum FieldMark
{
    None,

    /// <summary>Có giá trị, confidence dưới ngưỡng, chưa ai sửa — tô vàng, "cần kiểm".</summary>
    NeedsCheck,

    /// <summary>
    /// Rỗng. **Không tô**, chỉ nhãn "chưa có": rỗng là câu trả lời hợp lệ ("không có trên thẻ" hoặc
    /// "không chắc" — CLAUDE.md luật cứng 10), không phải một lượt đọc cần soát lại. Trường rỗng luôn
    /// được 0 điểm, nên tô theo điểm thì <c>searchAlias</c> của mọi thẻ tiếng Anh thành ô vàng vô nghĩa.
    /// </summary>
    Missing,

    /// <summary>Người đã sửa — nằm trong <c>editedFields</c>.</summary>
    Edited,
}

/// <param name="Field">Tên trường theo schema. Rỗng hoặc không khớp ô nào thì hiện ở danh sách chung.</param>
public sealed record ReviewNote(string Field, string Code, string Text);

/// <summary>Kết quả một lần lưu thành công — đủ để hiện "Đã lưu PTN00xx — Tên, Công ty".</summary>
public sealed record SavedPartner(
    string PartnerId, string FullName, string Company, IReadOnlyList<GuardWarning> Warnings);

/// <summary>
/// Trạng thái của màn hình xác nhận (T-13, PRD US-03), tách khỏi Razor để test được mà không cần
/// thư viện render component (SPEC mục 1 không cho thêm package).
///
/// Đây là chỗ **nối <see cref="ExtractOutcome.Usage"/> sang <see cref="PartnerDraft.Usage"/>** (SPEC mục
/// 4.1). Nhánh lưu không trích xuất nên tự nó không biết lần gọi mô hình nào; quên nối thì hồ sơ vẫn lưu
/// êm với <c>model: "-"</c> — trông y như người tự gõ, và không có gì báo.
/// </summary>
public sealed class ReviewDraft
{
    public const string FullNameField = "fullName";
    public const string JobTitleField = "jobTitle";
    public const string CompanyField = "company";
    public const string PhonesField = "phones";
    public const string EmailsField = "emails";
    public const string WebsiteField = "website";
    public const string AddressField = "address";
    public const string SearchAliasField = "searchAlias";
    public const string DetectedLanguageField = "detectedLanguage";

    /// <summary>Các ô trên form, theo thứ tự hiển thị. <c>editedFields</c> cũng xếp theo thứ tự này.</summary>
    public static readonly IReadOnlyList<string> FormFields =
    [
        FullNameField, JobTitleField, CompanyField, PhonesField, EmailsField,
        WebsiteField, AddressField, SearchAliasField, DetectedLanguageField,
    ];

    private readonly Snapshot _original;
    private readonly IReadOnlyDictionary<string, double> _fieldConfidence;
    private readonly IReadOnlySet<string> _reviewFields;
    private readonly IReadOnlyList<string> _previouslyEdited;

    private ReviewDraft(
        string? partnerId,
        Snapshot original,
        IReadOnlyDictionary<string, double> fieldConfidence,
        IEnumerable<string> reviewFields,
        IReadOnlyList<string> previouslyEdited,
        IReadOnlyList<ReviewNote> notes,
        string sourceImage,
        string imageSha256,
        ExtractionUsage usage)
    {
        PartnerId = partnerId;
        _original = original;
        _fieldConfidence = fieldConfidence;
        _reviewFields = reviewFields.ToHashSet(StringComparer.Ordinal);
        _previouslyEdited = previouslyEdited;
        Notes = notes;
        SourceImage = sourceImage;
        ImageSha256 = imageSha256;
        Usage = usage;

        FullName = original.FullName;
        JobTitle = original.JobTitle;
        Company = original.Company;
        Phones = [.. original.Phones.Select(value => new EditableValue(value))];
        Emails = [.. original.Emails.Select(value => new EditableValue(value))];
        Website = original.Website;
        Address = original.Address;
        SearchAlias = original.SearchAlias;
        DetectedLanguage = original.DetectedLanguage;
    }

    /// <summary>
    /// Thẻ vừa trích xuất, chưa lưu. <paramref name="imageSha256"/> là thứ <c>ImageStore.SaveAsync</c> trả
    /// về — tên file trên đĩa; <paramref name="sourceImage"/> là tên file gốc trình duyệt gửi lên.
    /// </summary>
    public static ReviewDraft FromOutcome(ExtractOutcome outcome, string sourceImage, string imageSha256)
    {
        var card = outcome.Card
            ?? throw new ArgumentException("Màn hình xác nhận chỉ dựng từ kết quả có thẻ.", nameof(outcome));

        return new ReviewDraft(
            partnerId: null,
            Snapshot.From(card),
            card.FieldConfidence,
            outcome.ReviewFields,
            previouslyEdited: [],
            NotesFrom(outcome, card),
            sourceImage,
            imageSha256,
            // Đây là mắt nối SPEC mục 4.1 đòi: ExtractOutcome.Usage → màn hình giữ lại → PartnerDraft.Usage.
            outcome.Usage);
    }

    /// <summary>
    /// Hồ sơ đã lưu, mở lại để xem/sửa (T-14 bấm thẻ ở Lịch sử). Ảnh và xuất xứ trích xuất đi tiếp
    /// nguyên vẹn: không dựng lại <c>Usage</c> từ <c>Extraction</c> thì chỉ sửa một chức danh cũng biến
    /// hồ sơ gemini thành <c>model: "-"</c> — đúng cái bẫy của SPEC mục 4.1, chỉ là ở đường sửa.
    /// </summary>
    public static ReviewDraft FromPartner(Partner partner) => new(
        partner.PartnerId,
        Snapshot.From(partner),
        partner.FieldConfidence,
        // Hồ sơ đã có người xác nhận: không tô "cần kiểm". Hồ sơ mồi còn mang fieldConfidence rỗng —
        // tô theo điểm thì cả form vàng.
        reviewFields: [],
        partner.EditedFields,
        notes: [],
        partner.SourceImage,
        partner.ImageSha256,
        new ExtractionUsage(
            partner.Extraction.TokensIn ?? 0,
            partner.Extraction.TokensOut ?? 0,
            partner.Extraction.LatencyMs,
            partner.Extraction.Model,
            partner.Extraction.PromptVersion));

    /// <summary><c>null</c> là hồ sơ mới; có mã là sửa hồ sơ đó.</summary>
    public string? PartnerId { get; }

    public bool IsSavedPartner => PartnerId is not null;

    public string SourceImage { get; }

    public string ImageSha256 { get; }

    public ExtractionUsage Usage { get; }

    /// <summary>
    /// Cảnh báo lúc trích xuất, gộp từ **cả hai kênh** (BACKLOG T-13): <c>SchemaGuard</c> qua
    /// <see cref="ExtractOutcome.Warnings"/>, và <c>Normalizer</c> qua <c>outcome.Card.Warnings</c>. Chỉ
    /// đọc kênh đầu là im lặng đánh rơi <c>unnormalizedPhone</c> — cảnh báo hay gặp nhất với thẻ Nhật.
    /// </summary>
    public IReadOnlyList<ReviewNote> Notes { get; }

    public string FullName { get; set; }

    public string JobTitle { get; set; }

    public string Company { get; set; }

    public List<EditableValue> Phones { get; }

    public List<EditableValue> Emails { get; }

    public string Website { get; set; }

    public string Address { get; set; }

    public string SearchAlias { get; set; }

    public string DetectedLanguage { get; set; }

    public IEnumerable<ReviewNote> NotesFor(string field) =>
        Notes.Where(note => note.Field == field);

    /// <summary>Ghi chú không gắn với ô nào — ví dụ SG-1, khoá lạ ngoài schema.</summary>
    public IEnumerable<ReviewNote> GeneralNotes =>
        Notes.Where(note => !FormFields.Contains(note.Field));

    /// <summary>
    /// Trường người đã sửa so với lúc mở màn hình, cộng những trường đã sửa ở các lần lưu trước.
    /// So sau khi cắt khoảng trắng; với danh sách thì bỏ dòng rỗng rồi so cả dãy — thêm một dòng
    /// trống rồi để đó không phải là sửa.
    /// </summary>
    public IReadOnlyList<string> EditedFields()
    {
        var current = Snapshot.From(this);
        var edited = new HashSet<string>(_previouslyEdited, StringComparer.Ordinal);

        foreach (var field in FormFields)
        {
            if (!current.SameAs(_original, field))
            {
                edited.Add(field);
            }
        }

        return [.. FormFields.Where(edited.Contains), .. _previouslyEdited.Where(f => !FormFields.Contains(f))];
    }

    public FieldMark MarkFor(string field)
    {
        if (EditedFields().Contains(field))
        {
            return FieldMark.Edited;
        }

        if (Snapshot.From(this).IsEmpty(field))
        {
            return FieldMark.Missing;
        }

        return _reviewFields.Contains(field) ? FieldMark.NeedsCheck : FieldMark.None;
    }

    /// <summary>
    /// Lỗi chặn nút Lưu. Kiểm bằng **đúng hàm của guard** (SG-4, SG-5): báo ngay dưới ô cho người sửa,
    /// thay vì để guard lặng lẽ xoá phần tử đó lúc lưu.
    /// </summary>
    public IReadOnlyList<ReviewNote> Problems
    {
        get
        {
            var current = Snapshot.From(this);
            var problems = new List<ReviewNote>();

            problems.AddRange(current.Emails
                .Where(email => !SchemaGuard.IsAcceptableEmail(email))
                .Select(email => new ReviewNote(EmailsField, "SG-4", $"“{email}” không phải email hợp lệ.")));

            problems.AddRange(current.Phones
                .Where(phone => !SchemaGuard.IsAcceptablePhone(phone))
                .Select(phone => new ReviewNote(
                    PhonesField, "SG-5", $"“{phone}” có ký tự không dùng trong số điện thoại.")));

            return problems;
        }
    }

    /// <summary>PRD US-03: nút Lưu chỉ bật khi có họ tên hoặc tên công ty.</summary>
    public bool HasNameOrCompany =>
        !string.IsNullOrWhiteSpace(FullName) || !string.IsNullOrWhiteSpace(Company);

    public bool CanSave => HasNameOrCompany && Problems.Count == 0;

    /// <summary>
    /// Bản nháp gửi vào nhánh lưu. <c>FieldConfidence</c> là điểm lúc trích xuất — nhánh lưu tự chấm lại
    /// và chạy guard (SPEC mục 2), màn hình không tự nâng điểm cho trường người đã sửa.
    /// </summary>
    public PartnerDraft ToPartnerDraft(bool allowDuplicate)
    {
        var current = Snapshot.From(this);

        var card = new CardExtractionResult(
            // Người đã nhìn tấm thẻ và bấm Lưu (US-03).
            IsBusinessCard: true,
            RejectReason: string.Empty,
            FullName: current.FullName,
            JobTitle: current.JobTitle,
            Company: current.Company,
            Phones: current.Phones,
            Emails: current.Emails,
            Website: current.Website,
            Address: current.Address,
            DetectedLanguage: current.DetectedLanguage,
            SearchAlias: current.SearchAlias,
            FieldConfidence: _fieldConfidence);

        return new PartnerDraft(PartnerId, card, SourceImage, ImageSha256, EditedFields(), allowDuplicate)
        {
            Usage = Usage,
        };
    }

    private static IReadOnlyList<ReviewNote> NotesFrom(ExtractOutcome outcome, CardExtractionResult card)
    {
        var notes = new List<ReviewNote>();

        // Kênh 1 — SchemaGuard: email sai định dạng bị xoá (SG-4), khoá lạ bị bỏ (SG-1)…
        notes.AddRange(outcome.Warnings.Select(w => new ReviewNote(w.Field, w.Code, w.Reason)));

        // Kênh 2 — Normalizer. Kiểu khác (chuỗi mã, không phải GuardWarning), nằm ở chỗ khác (trên thẻ).
        foreach (var code in card.Warnings)
        {
            notes.Add(code == Normalizer.UnnormalizedPhoneWarning
                ? new ReviewNote(PhonesField, code,
                    "Có số không kèm mã quốc gia — giữ nguyên như in trên thẻ, hệ thống không tự đoán mã.")
                : new ReviewNote(string.Empty, code, code));
        }

        return notes;
    }

    /// <summary>Giá trị đã cắt khoảng trắng, danh sách đã bỏ dòng rỗng — dạng dùng để so và để lưu.</summary>
    private sealed record Snapshot(
        string FullName,
        string JobTitle,
        string Company,
        IReadOnlyList<string> Phones,
        IReadOnlyList<string> Emails,
        string Website,
        string Address,
        string SearchAlias,
        string DetectedLanguage)
    {
        public static Snapshot From(CardExtractionResult card) => new(
            Clean(card.FullName), Clean(card.JobTitle), Clean(card.Company),
            Clean(card.Phones), Clean(card.Emails),
            Clean(card.Website), Clean(card.Address), Clean(card.SearchAlias), Clean(card.DetectedLanguage));

        public static Snapshot From(Partner partner) => new(
            Clean(partner.FullName), Clean(partner.JobTitle), Clean(partner.Company),
            Clean(partner.Phones), Clean(partner.Emails),
            Clean(partner.Website), Clean(partner.Address), Clean(partner.SearchAlias),
            Clean(partner.DetectedLanguage));

        public static Snapshot From(ReviewDraft draft) => new(
            Clean(draft.FullName), Clean(draft.JobTitle), Clean(draft.Company),
            Clean(draft.Phones.Select(item => item.Value)), Clean(draft.Emails.Select(item => item.Value)),
            Clean(draft.Website), Clean(draft.Address), Clean(draft.SearchAlias), Clean(draft.DetectedLanguage));

        public bool IsEmpty(string field) => field switch
        {
            PhonesField => Phones.Count == 0,
            EmailsField => Emails.Count == 0,
            _ => Text(field).Length == 0,
        };

        public bool SameAs(Snapshot other, string field) => field switch
        {
            PhonesField => Phones.SequenceEqual(other.Phones, StringComparer.Ordinal),
            EmailsField => Emails.SequenceEqual(other.Emails, StringComparer.Ordinal),
            _ => string.Equals(Text(field), other.Text(field), StringComparison.Ordinal),
        };

        private string Text(string field) => field switch
        {
            FullNameField => FullName,
            JobTitleField => JobTitle,
            CompanyField => Company,
            WebsiteField => Website,
            AddressField => Address,
            SearchAliasField => SearchAlias,
            DetectedLanguageField => DetectedLanguage,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Không phải ô trên form."),
        };

        private static string Clean(string? value) => value?.Trim() ?? string.Empty;

        private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
            [.. values.Select(Clean).Where(value => value.Length > 0)];
    }
}
