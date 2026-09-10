using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;

namespace PartnerCard.Tests.Extraction;

/// <summary>
/// TEST-SPEC mục 2 — các ca thuộc T-04.
///
/// **Phạm vi:** chạy qua <see cref="FakeExtractor"/> nên nhóm này không đo được chất lượng đọc
/// của mô hình; nó kiểm **hợp đồng** — hình dạng kết quả, số phần tử của mảng, trường rỗng đúng
/// chỗ, ca âm tính đúng cấu trúc, và cơ chế tra <c>expected.json</c> theo <c>sourceName</c>.
/// Chất lượng đọc thật đo ở bộ đo T-08 trên <c>realcards/</c>.
/// </summary>
[Trait("Category", "Extract")]
public sealed class FakeExtractorTests
{
    private static string CardsDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "cards");

    private static FakeExtractor Create() =>
        new(ExpectedCards.LoadFrom(Path.Combine(CardsDirectory, "expected.json")));

    private static Task<CardExtractionResult> ExtractAsync(string fileName, string mimeType = "image/png")
    {
        var path = Path.Combine(CardsDirectory, fileName);
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];

        return Create().ExtractAsync(bytes, mimeType, null, fileName, CancellationToken.None);
    }

    // ---- X-01 → X-06 · thẻ đọc được -----------------------------------------------

    [Fact]
    public async Task X01_the_tieng_anh_day_du_dien_du_8_truong()
    {
        var card = await ExtractAsync("en-01.png");

        card.IsBusinessCard.Should().BeTrue();
        card.FullName.Should().Be("Marcus Feld");
        card.JobTitle.Should().NotBeEmpty();
        card.Company.Should().NotBeEmpty();
        card.Phones.Should().NotBeEmpty();
        card.Emails.Should().NotBeEmpty();
        card.Website.Should().NotBeEmpty();
        card.Address.Should().NotBeEmpty();
        card.DetectedLanguage.Should().Be("en");
    }

    [Theory]
    [InlineData("en-07.png")]
    [InlineData("ja-07.png")]
    public async Task X02_the_hai_so_dien_thoai_tra_ve_hai_phan_tu_khong_ghep_chuoi(string fileName)
    {
        var card = await ExtractAsync(fileName);

        card.Phones.Should().HaveCount(2);
        card.Phones.Should().OnlyContain(p => !p.Contains(',') && !p.Contains('/'));
    }

    [Fact]
    public async Task X03_the_tieng_nhat_giu_nguyen_tung_ky_tu_va_searchAlias_co_ca_hai_phien_am()
    {
        var card = await ExtractAsync("ja-01.png");

        card.FullName.Should().Be("田中 太郎");
        card.Company.Should().Be("株式会社青葉精工");
        card.DetectedLanguage.Should().Be("ja");
        card.SearchAlias.Should().Contain("Tanaka Taro").And.Contain("Aoba Seiko");
    }

    [Fact]
    public async Task X04_the_nhat_bo_cuc_doc_van_ra_dung_truong()
    {
        var card = await ExtractAsync("ja-06.png");

        card.IsBusinessCard.Should().BeTrue();
        card.FullName.Should().Be("伊藤 千尋");
        card.Company.Should().Be("株式会社北斗機械");
        card.Phones.Should().ContainSingle().Which.Should().Be("011-555-2263");
    }

    [Theory]
    [InlineData("en-03.png", "Tomás Herrera")]
    [InlineData("ja-05.png", "渡辺 拓真")]
    public async Task X05_the_nen_toi_chu_sang_van_ra_dung_truong(string fileName, string expectedName)
    {
        var card = await ExtractAsync(fileName);

        card.IsBusinessCard.Should().BeTrue();
        card.FullName.Should().Be(expectedName);
        card.Address.Should().NotBeEmpty();
    }

    [Fact]
    public async Task X06_the_song_ngu_bao_mixed_giu_chu_nhat_va_searchAlias_la_ban_latin()
    {
        var card = await ExtractAsync("bi-01.png");

        card.DetectedLanguage.Should().Be("mixed");
        card.FullName.Should().Be("中川 寛");
        card.SearchAlias.Should().Be("Hiroshi Nakagawa Sakuragawa Trading K.K. Tokyo Chuo Ginza");
    }

    // ---- X-07 · không bịa email ----------------------------------------------------

    [Theory]
    [InlineData("en-08.png")]
    [InlineData("ja-08.png")]
    public async Task X07_the_khong_co_email_thi_emails_rong_va_confidence_bang_0(string fileName)
    {
        var card = await ExtractAsync(fileName);

        card.Emails.Should().BeEmpty();
        card.FieldConfidence["emails"].Should().Be(0.0);

        // Dạng bịa phổ biến nhất: thấy website abc.example rồi tự tạo info@abc.example.
        card.Website.Should().NotBeEmpty("thẻ vẫn có website — chính là mồi cho việc bịa email");
    }

    // ---- X-08, X-09 · ca âm tính ---------------------------------------------------

    [Theory]
    [InlineData("neg-01.png")]
    [InlineData("neg-02.png")]
    public async Task X08_X09_anh_khong_phai_danh_thiep_thi_tu_choi_va_moi_truong_rong(string fileName)
    {
        var card = await ExtractAsync(fileName);

        card.IsBusinessCard.Should().BeFalse();
        card.RejectReason.Should().NotBeEmpty();

        card.FullName.Should().BeEmpty();
        card.JobTitle.Should().BeEmpty();
        card.Company.Should().BeEmpty();
        card.Website.Should().BeEmpty();
        card.Address.Should().BeEmpty();
        card.SearchAlias.Should().BeEmpty();
        card.Phones.Should().BeEmpty();
        card.Emails.Should().BeEmpty();
    }

    // ---- Cơ chế tra: theo tên file, KHÔNG theo mã băm -------------------------------

    [Fact]
    public async Task Tra_theo_ten_file_chu_khong_theo_ma_bam()
    {
        // Cùng mã thẻ, hai file khác nhau hoàn toàn về byte: PNG render và JPG chụp lại.
        // Khoá theo hash thì hai bên ra hai kết quả khác nhau — đó chính là cái bẫy cần chặn.
        var fromPng = await ExtractAsync("ja-06.png");

        var jpgPath = Path.Combine(AppContext.BaseDirectory, "TestData", "realcards", "ja-06.jpg");
        var fromJpg = await Create().ExtractAsync(
            File.ReadAllBytes(jpgPath), "image/jpeg", null, "ja-06.jpg", CancellationToken.None);

        File.ReadAllBytes(Path.Combine(CardsDirectory, "ja-06.png"))
            .Should().NotEqual(File.ReadAllBytes(jpgPath), "hai file phải khác byte thì ca này mới có nghĩa");

        fromJpg.Should().BeEquivalentTo(fromPng);
    }

    [Fact]
    public async Task Ten_file_co_duong_dan_day_du_van_tra_ra_dung_ma_the()
    {
        // Trình duyệt gửi lên "C:\fakepath\ja-01.png" chứ không phải tên trần.
        var card = await Create().ExtractAsync(
            ReadOnlyMemory<byte>.Empty, "image/png", null, @"C:\fakepath\ja-01.png", CancellationToken.None);

        card.FullName.Should().Be("田中 太郎");
    }

    [Theory]
    [InlineData("khong-co-ma-nay.jpg")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Anh_khong_khop_ma_the_nao_thi_tra_isBusinessCard_false(string? sourceName)
    {
        var card = await Create().ExtractAsync(
            ReadOnlyMemory<byte>.Empty, "image/jpeg", null, sourceName, CancellationToken.None);

        card.IsBusinessCard.Should().BeFalse();
        card.RejectReason.Should().NotBeEmpty();
        card.FullName.Should().BeEmpty();
    }

    // ---- fieldConfidence và rejectReason do FakeExtractor tự sinh (SPEC 4.1) --------

    [Fact]
    public async Task Confidence_tu_sinh_1_cho_truong_co_gia_tri_va_0_cho_truong_rong()
    {
        var card = await ExtractAsync("en-01.png");

        card.FieldConfidence["fullName"].Should().Be(1.0);
        card.FieldConfidence["emails"].Should().Be(1.0);

        // en-01 không có searchAlias (thẻ tiếng Anh) nên trường rỗng phải nhận 0.
        card.SearchAlias.Should().BeEmpty();
        card.FieldConfidence["searchAlias"].Should().Be(0.0);
    }

    [Fact]
    public async Task Confidence_co_du_tam_truong_ma_SG3_bat_buoc()
    {
        var card = await ExtractAsync("ja-01.png");

        card.FieldConfidence.Keys.Should().BeEquivalentTo(CardSchema.ConfidenceRequiredFields);
    }
}
