using Microsoft.Extensions.Options;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Audit;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Processing;

/// <summary>
/// Các ca thuộc T-06b — đường ống nối đủ mắt xích, chạy hoàn toàn offline.
///
/// Hai nhánh, hai chuỗi khác nhau (SPEC mục 2):
/// trích xuất <c>Validate → Extract → Guard(JSON thô) → deserialize → Normalize → Confidence → Audit</c>,
/// lưu <c>Validate → Normalize → Confidence → Guard(DTO đã serialize) → Persist → Audit</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CardPipelineTests
{
    private static string CardsDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "cards");

    private sealed class Harness : IDisposable
    {
        private readonly TempDataDirectory _data = new();

        public Harness(IExtractor? extractor = null)
        {
            Store = JsonPartnerStore.LoadFrom(_data.Path, new SteppingClock());
            Audit = new RecordingAuditLogger();

            Extractor = extractor ?? new FakeExtractor(
                ExpectedCards.LoadFrom(Path.Combine(CardsDirectory, "expected.json")));

            Pipeline = new CardPipeline(
                Extractor, new SchemaGuard(), Store, Audit,
                Options.Create(new PartnerCardOptions()), new SteppingClock());
        }

        public IExtractor Extractor { get; }

        public JsonPartnerStore Store { get; }

        public RecordingAuditLogger Audit { get; }

        public CardPipeline Pipeline { get; }

        public string PartnersFile => _data.PartnersFile;

        public void Dispose()
        {
            Store.Dispose();
            _data.Dispose();
        }
    }

    private static string Base64Of(string cardFile) =>
        Convert.ToBase64String(File.ReadAllBytes(Path.Combine(CardsDirectory, cardFile)));

    // =====================================================================================
    // Mắt xích Validate — X-11, X-12, X-13
    // =====================================================================================

    [Fact]
    public async Task X11_anh_vuot_tran_bi_tu_choi_truoc_khi_goi_mo_hinh()
    {
        var counting = new CountingExtractor(new FakeExtractor(
            ExpectedCards.LoadFrom(Path.Combine(CardsDirectory, "expected.json"))));
        using var harness = new Harness(counting);

        var tooBig = Convert.ToBase64String(new byte[9 * 1024 * 1024]);

        var outcome = await harness.Pipeline.ExtractAsync(
            tooBig, "image/jpeg", null, "en-01.jpg", CancellationToken.None);

        outcome.Ok.Should().BeFalse();
        outcome.ErrorCode.Should().Be("image_too_large");
        outcome.Message.Should().NotBeNullOrEmpty();
        counting.Calls.Should().Be(0, "phải từ chối TRƯỚC khi gọi mô hình — gọi rồi mới chặn là tốn hạn mức");
    }

    [Fact]
    public async Task X12_mime_khong_duoc_nhan_bi_tu_choi_va_neu_ro_dinh_dang_hop_le()
    {
        using var harness = new Harness();

        var outcome = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/gif", null, "en-01.gif", CancellationToken.None);

        outcome.Ok.Should().BeFalse();
        outcome.ErrorCode.Should().Be("unsupported_mime");
        outcome.Message.Should().Contain("image/jpeg").And.Contain("image/png");
    }

    [Fact]
    public async Task X13_base64_hong_bi_tu_choi_khong_nem_exception()
    {
        using var harness = new Harness();

        var outcome = await harness.Pipeline.ExtractAsync(
            "!!! đây không phải base64 !!!", "image/png", null, "en-01.png", CancellationToken.None);

        outcome.Ok.Should().BeFalse();
        outcome.ErrorCode.Should().Be("invalid_base64");
        outcome.Message.Should().NotBeNullOrEmpty();
    }

    // =====================================================================================
    // G-14 lần hai, ở dạng đầu-cuối
    // =====================================================================================

    [Fact]
    public async Task G14_HostileExtractor_bi_guard_chan_va_khong_co_gi_vao_kho()
    {
        using var harness = new Harness(new HostileExtractor());

        var outcome = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        outcome.Ok.Should().BeFalse();
        outcome.ErrorCode.Should().Be("guard_blocked");
        outcome.Card.Should().BeNull();

        var stored = await harness.Store.SearchAsync(new PartnerQuery(), CancellationToken.None);
        stored.Should().BeEmpty("nhánh trích xuất không persist, và bị chặn thì càng không");
    }

    // =====================================================================================
    // G-11, G-12, G-13 — guard chặn thì audit ghi gì
    // =====================================================================================

    [Fact]
    public async Task G11_guard_chan_thi_audit_ghi_muc_GuardBlock_kem_ma_ly_do()
    {
        using var harness = new Harness(new HostileExtractor());

        await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        var entry = harness.Audit.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(AuditLevel.GuardBlock);
        entry.BlockCode.Should().Be("SG-7");
    }

    [Fact]
    public async Task G12_audit_khong_chua_gia_tri_bi_chan()
    {
        using var harness = new Harness(new HostileExtractor());

        await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        var line = string.Join("\n", harness.Audit.Entries.Select(e => e.ToString()));

        // Đúng những giá trị HostileExtractor cố nhét ra ngoài.
        line.Should().NotContain("Nguyễn Văn An");
        line.Should().NotContain("an.nguyen");
        line.Should().NotContain("0912345678");
        line.Should().NotContain("12 Nguyễn Huệ");
        line.Should().NotContain("```json");
    }

    [Fact]
    public async Task G13_guard_chan_thi_khong_co_lan_thu_lai_nao()
    {
        var counting = new CountingExtractor(new HostileExtractor());
        using var harness = new Harness(counting);

        await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        counting.Calls.Should().Be(1, "thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế");
    }

    // =====================================================================================
    // I-02 — đường thuận, bằng chứng cho mốc v0.1
    // =====================================================================================

    [Fact]
    public async Task I02_chuoi_day_du_extract_save_search_chay_duoc_offline()
    {
        using var harness = new Harness();

        var extracted = await harness.Pipeline.ExtractAsync(
            Base64Of("ja-01.png"), "image/png", null, "ja-01.png", CancellationToken.None);

        extracted.Ok.Should().BeTrue();
        extracted.Card!.FullName.Should().Be("田中 太郎");

        var saved = await harness.Pipeline.SaveAsync(
            new PartnerDraft(
                PartnerId: null,
                Card: extracted.Card,
                SourceImage: "images/ja-01.png",
                ImageSha256: "abc123",
                EditedFields: []),
            sessionId: "test-001",
            CancellationToken.None);

        saved.Ok.Should().BeTrue();
        saved.PartnerId.Should().Be("PTN0001");

        var found = await harness.Store.GetAsync(saved.PartnerId!, CancellationToken.None);
        found.Should().NotBeNull();
        found!.Company.Should().Be("株式会社青葉精工", "giá trị gốc phải giữ nguyên từng ký tự");
        found.Status.Should().Be(PartnerStatus.Confirmed);

        File.Exists(harness.PartnersFile).Should().BeTrue();
    }

    [Fact]
    public async Task Nhanh_trich_xuat_khong_bao_gio_ghi_vao_kho()
    {
        using var harness = new Harness();

        await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        var stored = await harness.Store.SearchAsync(new PartnerQuery(), CancellationToken.None);

        stored.Should().BeEmpty("M-04: extract_business_card không ghi vào partners.json");
    }

    [Fact]
    public async Task Nhanh_trich_xuat_chay_du_Normalize_va_Confidence()
    {
        using var harness = new Harness();

        var outcome = await harness.Pipeline.ExtractAsync(
            Base64Of("ja-01.png"), "image/png", null, "ja-01.png", CancellationToken.None);

        // Normalize đã chạy: website được thêm scheme.
        outcome.Card!.Website.Should().Be("https://aoba-seiko.example");

        // Confidence đã chạy: số Nhật không có mã quốc gia vẫn được 1,0 (C-07),
        // nên nó KHÔNG nằm trong danh sách cần xem lại.
        outcome.ReviewFields.Should().NotContain("phones");

        // …và ghi chú unnormalizedPhone vẫn có mặt.
        outcome.Card.Warnings.Should().Contain(Normalizer.UnnormalizedPhoneWarning);
    }

    [Fact]
    public async Task The_am_tinh_di_qua_duoc_va_khong_mang_theo_du_lieu_nao()
    {
        using var harness = new Harness();

        var outcome = await harness.Pipeline.ExtractAsync(
            Base64Of("neg-01.png"), "image/png", null, "neg-01.png", CancellationToken.None);

        outcome.Ok.Should().BeTrue("ảnh không phải danh thiếp là kết quả hợp lệ, không phải lỗi");
        outcome.Card!.IsBusinessCard.Should().BeFalse();
        outcome.Card.RejectReason.Should().NotBeEmpty();
        outcome.Card.FullName.Should().BeEmpty();
    }

    [Fact]
    public async Task Luu_ho_so_trung_email_thi_tra_duplicateOf_va_chua_ghi_gi_them()
    {
        using var harness = new Harness();

        var extracted = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        var draft = new PartnerDraft(null, extracted.Card!, "images/en-01.png", "sha", []);

        var first = await harness.Pipeline.SaveAsync(draft, "s1", CancellationToken.None);
        var second = await harness.Pipeline.SaveAsync(draft, "s1", CancellationToken.None);

        first.Ok.Should().BeTrue();
        second.Ok.Should().BeFalse();
        second.DuplicateOf!.PartnerId.Should().Be(first.PartnerId);

        var stored = await harness.Store.SearchAsync(new PartnerQuery(), CancellationToken.None);
        stored.Should().HaveCount(1);
    }

    // =====================================================================================
    // Số đo của lần gọi — SPEC mục 4.1
    // =====================================================================================

    [Fact]
    public async Task Nhanh_trich_xuat_mang_theo_so_do_cua_ban_cai_dang_chay()
    {
        using var harness = new Harness();

        var outcome = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        // "fake" nghĩa là FakeExtractor đã chạy thật — nó chỉ không tốn token nào.
        outcome.Usage.Model.Should().Be("fake");
        outcome.Usage.TokensIn.Should().Be(0);
        outcome.Usage.TokensOut.Should().Be(0);
    }

    [Fact]
    public async Task Duong_loi_khong_bia_ra_so_do()
    {
        using var harness = new Harness();

        var outcome = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/gif", null, "en-01.gif", CancellationToken.None);

        // Validate chặn trước khi chạm tới extractor, nên không có lần gọi nào để mà đo.
        outcome.Usage.Should().Be(ExtractionUsage.Untracked);
    }

    // =====================================================================================
    // Ba kiểu lỗi của extractor — SPEC mục 4.1 và 4.6
    // =====================================================================================

    [Theory]
    [InlineData("quota")]
    [InlineData("timeout")]
    [InlineData("auth")]
    [InlineData("unavailable")]
    public async Task Bon_kieu_loi_ra_bon_ma_rieng_va_khong_exception_nao_thoat_ra(string kind)
    {
        var (thrown, expectedCode) = kind switch
        {
            "quota" => ((Exception)new ExtractorQuotaException(), "quota_exhausted"),
            "timeout" => (new ExtractorTimeoutException(), "extract_timeout"),
            "unavailable" => (new ExtractorUnavailableException(), "extract_unavailable"),
            _ => (new ExtractorAuthException(), "extractor_auth"),
        };

        var extractor = new ThrowingExtractor(thrown);
        using var harness = new Harness(extractor);

        var outcome = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        outcome.Ok.Should().BeFalse();
        outcome.ErrorCode.Should().Be(expectedCode);
        outcome.Message.Should().NotBeNullOrEmpty();
        outcome.Card.Should().BeNull();

        // Không có lần gọi nào thành công thì không có gì để đo.
        outcome.Usage.Should().Be(ExtractionUsage.Untracked);

        // Thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế — đúng với cả ba, nặng nhất ở 429.
        extractor.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Loi_extractor_ghi_mot_dong_audit_muc_Error_kem_ma_ly_do()
    {
        using var harness = new Harness(new ThrowingExtractor(new ExtractorQuotaException()));

        await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        var entry = harness.Audit.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(AuditLevel.Error);
        entry.ErrorCode.Should().Be("quota_exhausted");

        // errorCode và blockCode là hai chuyện khác nhau: chưa hề có kết quả nào để mà chặn.
        entry.BlockCode.Should().BeNull();
    }

    [Fact]
    public async Task Loi_la_khong_doan_duoc_van_thanh_ket_qua_co_cau_truc()
    {
        using var harness = new Harness(new ThrowingExtractor(new InvalidOperationException("chi tiết nội bộ")));

        var outcome = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        outcome.ErrorCode.Should().Be("extract_failed");
        outcome.Message.Should().NotContain("chi tiết nội bộ", "SPEC mục 10.3: thông báo trung tính");
        harness.Audit.Entries.Should().ContainSingle().Which.Level.Should().Be(AuditLevel.Error);
    }

    [Fact]
    public async Task Nguoi_dung_huy_that_thi_van_thoat_ra_nguyen_dang()
    {
        // Ranh giới cố ý: việc huỷ là của người gọi, không phải một lỗi để nuốt thành kết quả.
        // Chính vì mắt catch này mà GeminiExtractor phải tự đổi timeout của nó thành
        // ExtractorTimeoutException — để nguyên TaskCanceledException là bay ra khỏi đường ống.
        using var harness = new Harness(new ThrowingExtractor(new OperationCanceledException()));

        var act = async () => await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Trich_xuat_thanh_cong_ghi_token_va_do_tre_vao_audit()
    {
        using var harness = new Harness();

        await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        var entry = harness.Audit.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(AuditLevel.Info);
        entry.Model.Should().Be("fake");
        entry.PromptVersion.Should().Be("-");
        entry.TokensIn.Should().Be(0, "bản cài offline không tiêu token nào");
        entry.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void Ho_so_nhap_tay_khong_duoc_ghi_model_fake()
    {
        var draft = new PartnerDraft(
            PartnerId: null,
            Card: CardExtractionResult.NotACard("chưa dùng tới"),
            SourceImage: "images/tay.jpg",
            ImageSha256: "sha",
            EditedFields: []);

        // Không truyền Usage nghĩa là KHÔNG AI CHẠY CẢ, khác hẳn với "FakeExtractor đã chạy".
        draft.Usage.Should().Be(ExtractionUsage.Untracked);
        draft.Usage.Model.Should().Be("-");
        draft.Usage.Model.Should().NotBe(FakeExtractor.Usage.Model,
            "ghi model \"fake\" cho hồ sơ người tự gõ là nói dối về nguồn gốc dữ liệu");
    }

    [Fact]
    public async Task Ho_so_luu_mang_dung_so_do_cua_lan_trich_xuat()
    {
        using var harness = new Harness();

        var extracted = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        // Đây chính là chỗ màn hình /review phải nối lại ở T-13: giữ outcome.Usage rồi gửi kèm.
        var draft = new PartnerDraft(null, extracted.Card!, "images/en-01.png", "sha", [])
        {
            Usage = extracted.Usage,
        };

        var saved = await harness.Pipeline.SaveAsync(draft, "s1", CancellationToken.None);
        var stored = await harness.Store.GetAsync(saved.PartnerId!, CancellationToken.None);

        stored!.Extraction.Model.Should().Be("fake");
        stored.Extraction.PromptVersion.Should().Be("-");
    }

    [Fact]
    public async Task Quen_noi_Usage_thi_ho_so_ghi_dau_gach_chu_khong_bia_ra_gemini()
    {
        using var harness = new Harness();

        var extracted = await harness.Pipeline.ExtractAsync(
            Base64Of("en-01.png"), "image/png", null, "en-01.png", CancellationToken.None);

        // Cố tình KHÔNG nối Usage — đúng cái bẫy T-13 phải tránh.
        var saved = await harness.Pipeline.SaveAsync(
            new PartnerDraft(null, extracted.Card!, "images/en-01.png", "sha", []),
            "s1", CancellationToken.None);

        var stored = await harness.Store.GetAsync(saved.PartnerId!, CancellationToken.None);

        // Lưu vẫn êm — đó chính là lý do T-13 cần một ca riêng khẳng định việc nối đã xảy ra.
        stored!.Extraction.Model.Should().Be("-");
        stored.Extraction.LatencyMs.Should().Be(0);
    }
}
