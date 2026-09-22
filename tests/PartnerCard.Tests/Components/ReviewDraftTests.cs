using Microsoft.Extensions.Options;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Components.Review;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Components;

/// <summary>
/// T-13 — màn hình xác nhận. Logic của màn hình nằm ở <see cref="ReviewDraft"/>, nên các ca ở đây đi đúng
/// đường của nút Lưu: <c>ExtractAsync → ReviewDraft → ToPartnerDraft → SaveAsync</c>, không cần render Razor.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ReviewDraftTests
{
    private const string Sha = "9f2a000000000000000000000000000000000000000000000000000000000c31";

    private static string CardsDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "cards");

    private static IExtractor Fake() =>
        new FakeExtractor(ExpectedCards.LoadFrom(Path.Combine(CardsDirectory, "expected.json")));

    private sealed class Harness : IDisposable
    {
        private readonly TempDataDirectory _data = new();

        public Harness(IExtractor? extractor = null)
        {
            Store = JsonPartnerStore.LoadFrom(_data.Path, new SteppingClock());
            Pipeline = new CardPipeline(
                extractor ?? Fake(), new SchemaGuard(), Store, new RecordingAuditLogger(),
                Options.Create(new PartnerCardOptions()), new SteppingClock());
        }

        public JsonPartnerStore Store { get; }

        public CardPipeline Pipeline { get; }

        public async Task<ReviewDraft> ExtractAsync(string cardFile)
        {
            var outcome = await Pipeline.ExtractAsync(
                Base64Of(cardFile), "image/png", null, cardFile, CancellationToken.None);

            outcome.Ok.Should().BeTrue();
            return ReviewDraft.FromOutcome(outcome, cardFile, Sha);
        }

        public Task<SaveOutcome> SaveAsync(ReviewDraft draft, bool allowDuplicate = false) =>
            Pipeline.SaveAsync(draft.ToPartnerDraft(allowDuplicate), "s1", CancellationToken.None);

        public void Dispose()
        {
            Store.Dispose();
            _data.Dispose();
        }
    }

    private static string Base64Of(string cardFile) =>
        Convert.ToBase64String(File.ReadAllBytes(Path.Combine(CardsDirectory, cardFile)));

    /// <summary>Đường ống với một extractor phát lại đúng chuỗi JSON cho trước — để có cảnh báo của guard.</summary>
    private static async Task<ReviewDraft> ExtractRawAsync(string json)
    {
        using var harness = new Harness(new ReplayExtractor(new RawExtraction(json, FakeExtractor.Usage)));
        return await harness.ExtractAsync("en-01.png");
    }

    // =====================================================================================
    // Nối số đo — BACKLOG T-13, SPEC mục 4.1
    // =====================================================================================

    [Fact]
    public async Task Ho_so_luu_tu_man_xac_nhan_mang_dung_model_va_do_tre_cua_lan_trich_xuat()
    {
        // FakeExtractor ghi 0 ms, nên "LatencyMs > 0" xanh được chỉ khi số đo thật sự đi hết đường vòng.
        var usage = new ExtractionUsage(
            TokensIn: 1200, TokensOut: 180, LatencyMs: 2345, Model: "gemini-test", PromptVersion: "v3");
        using var harness = new Harness(new MeteredExtractor(Fake(), usage, TimeSpan.Zero));

        var draft = await harness.ExtractAsync("en-01.png");
        draft.JobTitle = "Chức danh người đã sửa";

        var saved = await harness.SaveAsync(draft);
        saved.Ok.Should().BeTrue();

        var stored = await harness.Store.GetAsync(saved.PartnerId!, CancellationToken.None);

        stored!.Extraction.Model.Should().Be("gemini-test",
            "quên nối ExtractOutcome.Usage thì hồ sơ vẫn lưu êm, chỉ là model \"-\" — trông như người tự gõ");
        stored.Extraction.LatencyMs.Should().BeGreaterThan(0).And.Be(2345);
        stored.Extraction.PromptVersion.Should().Be("v3");
        stored.EditedFields.Should().Equal(["jobTitle"]);
        stored.SourceImage.Should().Be("en-01.png");
        stored.ImageSha256.Should().Be(Sha);
    }

    // =====================================================================================
    // editedFields — PRD US-03
    // =====================================================================================

    [Fact]
    public async Task Khong_sua_gi_thi_editedFields_rong_ke_ca_khi_chi_them_khoang_trang_hay_dong_trong()
    {
        using var harness = new Harness();
        var draft = await harness.ExtractAsync("ja-07.png");

        draft.FullName = $"  {draft.FullName} ";
        draft.Emails.Add(new EditableValue("   "));

        draft.EditedFields().Should().BeEmpty();
        draft.MarkFor(ReviewDraft.FullNameField).Should().NotBe(FieldMark.Edited);
    }

    [Fact]
    public async Task Them_hay_bot_so_dien_thoai_thi_editedFields_ghi_phones()
    {
        using var harness = new Harness();
        var draft = await harness.ExtractAsync("ja-07.png");
        draft.Phones.Should().HaveCount(2, "ja-07 là thẻ hai số của bộ mẫu");

        draft.Phones.Add(new EditableValue("03-5550-0000"));
        draft.EditedFields().Should().Equal([ReviewDraft.PhonesField]);

        draft.Phones.RemoveAt(2);
        draft.EditedFields().Should().BeEmpty("thêm rồi bỏ đúng dòng đó là trở về như cũ");

        draft.Phones.RemoveAt(0);
        draft.EditedFields().Should().Equal([ReviewDraft.PhonesField]);
        draft.MarkFor(ReviewDraft.PhonesField).Should().Be(FieldMark.Edited);
    }

    // =====================================================================================
    // Cảnh báo — hai kênh (BACKLOG T-13)
    // =====================================================================================

    [Fact]
    public async Task Doc_ca_hai_kenh_canh_bao_guard_va_normalizer()
    {
        var card = CardBuilder.Valid() with
        {
            Phones = ["03-5550-1284"],
            Emails = ["m.feld@halbrook-logistics.example", "khong-phai-email"],
        };

        // Khoá lạ để có một cảnh báo SG-1 — nó không gắn với ô nào trên form.
        var json = CardJson.Serialize(card).Insert(1, "\"note\":\"khoá lạ\",");

        var draft = await ExtractRawAsync(json);

        // Kênh 1 — SchemaGuard (outcome.Warnings).
        draft.NotesFor(ReviewDraft.EmailsField).Should().Contain(n => n.Code == "SG-4");
        draft.GeneralNotes.Should().Contain(n => n.Code == "SG-1");

        // Kênh 2 — Normalizer (outcome.Card.Warnings). Chỉ đọc kênh 1 là đánh rơi đúng ca này.
        draft.NotesFor(ReviewDraft.PhonesField).Should().Contain(n => n.Code == Normalizer.UnnormalizedPhoneWarning);
    }

    // =====================================================================================
    // Tô trường — quyết định chốt ở T-13: trường rỗng "chưa có", không tô
    // =====================================================================================

    [Fact]
    public async Task Truong_co_gia_tri_duoi_nguong_can_kiem_truong_rong_chi_la_chua_co()
    {
        var card = CardBuilder.Valid() with
        {
            FieldConfidence = new Dictionary<string, double>(CardBuilder.Valid().FieldConfidence)
            {
                ["jobTitle"] = 0.6,
            },
        };

        var draft = await ExtractRawAsync(CardJson.Serialize(card));

        draft.MarkFor(ReviewDraft.JobTitleField).Should().Be(FieldMark.NeedsCheck);
        draft.MarkFor(ReviewDraft.SearchAliasField).Should().Be(FieldMark.Missing,
            "searchAlias rỗng luôn 0 điểm — tô nó là thẻ tiếng Anh nào cũng có một ô vàng vô nghĩa");
        draft.MarkFor(ReviewDraft.FullNameField).Should().Be(FieldMark.None);

        draft.JobTitle = "Operations Lead";
        draft.MarkFor(ReviewDraft.JobTitleField).Should().Be(FieldMark.Edited, "người đã sửa thì thôi tô vàng");
    }

    // =====================================================================================
    // Nút Lưu — PRD US-03, và kiểm cùng luật với guard
    // =====================================================================================

    [Fact]
    public async Task Nut_Luu_tat_khi_thieu_ca_ho_ten_lan_cong_ty_hoac_co_email_sai()
    {
        using var harness = new Harness();
        var draft = await harness.ExtractAsync("en-01.png");
        draft.CanSave.Should().BeTrue();

        var company = draft.Company;
        draft.FullName = "";
        draft.Company = " ";
        draft.HasNameOrCompany.Should().BeFalse();
        draft.CanSave.Should().BeFalse();

        draft.Company = company;
        draft.Emails.Add(new EditableValue("abc"));
        draft.CanSave.Should().BeFalse();
        draft.Problems.Should().ContainSingle(p => p.Field == ReviewDraft.EmailsField && p.Code == "SG-4");

        draft.Emails.RemoveAt(draft.Emails.Count - 1);
        draft.Phones.Add(new EditableValue("03-5550-1284 máy lẻ 12"));
        draft.Problems.Should().ContainSingle(p => p.Field == ReviewDraft.PhonesField && p.Code == "SG-5");
    }

    // =====================================================================================
    // Mở hồ sơ đã lưu để sửa — T-14 bấm thẻ ở Lịch sử
    // =====================================================================================

    [Fact]
    public async Task Sua_ho_so_da_luu_ghi_de_dung_ho_so_va_giu_nguyen_xuat_xu_trich_xuat()
    {
        using var harness = new Harness();
        var original = await harness.Store.UpsertAsync(
            PartnerFactory.New() with
            {
                Extraction = new ExtractionMeta("gemini-test", "v3", DateTimeOffset.UnixEpoch, 2345),
                EditedFields = ["company"],
            },
            CancellationToken.None);

        var draft = ReviewDraft.FromPartner(original);
        ReviewDraft.FormFields.Select(draft.MarkFor).Should().NotContain(FieldMark.NeedsCheck,
            "hồ sơ đã có người xác nhận");

        draft.JobTitle = "Head of Operations";
        var saved = await harness.SaveAsync(draft);

        saved.Ok.Should().BeTrue("email trùng với chính hồ sơ đang sửa không phải là trùng");
        saved.PartnerId.Should().Be(original.PartnerId);

        var all = await harness.Store.SearchAsync(new PartnerQuery(), CancellationToken.None);
        all.Should().ContainSingle();

        var stored = all[0];
        stored.JobTitle.Should().Be("Head of Operations");
        stored.CreatedAt.Should().Be(original.CreatedAt);
        stored.Extraction.Model.Should().Be("gemini-test",
            "sửa một chức danh không được biến hồ sơ do mô hình đọc thành hồ sơ không rõ nguồn");
        stored.Extraction.LatencyMs.Should().Be(2345);
        stored.EditedFields.Should().Equal(["jobTitle", "company"]);
        stored.SourceImage.Should().Be(original.SourceImage);
        stored.ImageSha256.Should().Be(original.ImageSha256);
    }

    [Fact]
    public async Task Ho_so_moi_khong_anh_khong_to_vang_va_luu_duoc_ma_van_la_dau_gach()
    {
        using var harness = new Harness();

        // Đúng hình dạng hồ sơ mồi (SPEC 19.3): không ảnh, fieldConfidence rỗng, extraction kiểu Untracked.
        var seed = await harness.Store.UpsertAsync(
            PartnerFactory.New() with
            {
                SourceImage = string.Empty,
                ImageSha256 = string.Empty,
                FieldConfidence = new Dictionary<string, double>(),
                Extraction = new ExtractionMeta("-", "-", DateTimeOffset.UnixEpoch, 0),
            },
            CancellationToken.None);

        var draft = ReviewDraft.FromPartner(seed);

        ReviewDraft.FormFields.Select(draft.MarkFor).Should().NotContain(FieldMark.NeedsCheck,
            "mọi điểm của hồ sơ mồi là 0 — tô theo điểm thì cả form vàng");
        draft.MarkFor(ReviewDraft.SearchAliasField).Should().Be(FieldMark.Missing);

        draft.Address = "12 Kestrel Avenue";
        var saved = await harness.SaveAsync(draft);
        saved.Ok.Should().BeTrue();

        var stored = await harness.Store.GetAsync(seed.PartnerId, CancellationToken.None);
        stored!.Extraction.Model.Should().Be("-", "không ai trích xuất hồ sơ này — không được thành \"fake\"");
        stored.ImageSha256.Should().BeEmpty();
    }
}
