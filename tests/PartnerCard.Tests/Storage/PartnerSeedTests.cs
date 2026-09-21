using System.Text.Json;
using PartnerCard.Web.Models;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Storage;

/// <summary>
/// TEST-SPEC I-12 — luật nạp dữ liệu mồi (SPEC mục 19.3). Thuộc T-18. Chạy offline.
///
/// Hai nhóm, và chúng kiểm hai thứ khác nhau:
///
/// * **Luật** — dựng file mồi riêng trong thư mục tạm. Ca kiểm luật mà đọc file mồi thật thì sửa
///   một hồ sơ mồi là đỏ một ca không liên quan gì tới cái đang sửa.
/// * **File mồi thật** — đọc bản mà csproj chép cạnh binary, kiểm bốn ràng buộc nội dung mà SPEC
///   mục 19.3 nêu đích danh.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PartnerSeedTests
{
    private static readonly JsonSerializerOptions SeedJsonOptions = new(JsonSerializerDefaults.Web);

    // ---- Nhóm luật ------------------------------------------------------------------

    [Fact]
    public async Task I12_kho_rong_thi_nap_du_ba_ho_so_moi()
    {
        using var data = new TempDataDirectory();
        var seedPath = WriteSeedFile(data, 3);

        var result = PartnerSeed.EnsureSeeded(data.Path, seedPath);

        result.Action.Should().Be(SeedAction.Seeded);
        result.Count.Should().Be(3);
        (await LoadAll(data)).Should().HaveCount(3);
    }

    [Fact]
    public async Task I12_kho_da_co_du_lieu_thi_khong_dung_vao()
    {
        using var data = new TempDataDirectory();
        var seedPath = WriteSeedFile(data, 3);

        using (var store = JsonPartnerStore.LoadFrom(data.Path, new SteppingClock()))
        {
            await store.UpsertAsync(PartnerFactory.New(fullName: "San co"), CancellationToken.None);
        }

        var before = await File.ReadAllTextAsync(data.PartnersFile);

        var result = PartnerSeed.EnsureSeeded(data.Path, seedPath);

        result.Action.Should().Be(SeedAction.StoreNotEmpty);
        (await File.ReadAllTextAsync(data.PartnersFile)).Should().Be(before);
        (await LoadAll(data)).Should().ContainSingle().Which.FullName.Should().Be("San co");
    }

    /// <summary>
    /// Ca tái hiện đúng trạng thái máy Chung ngày 21/09 — và là ca duy nhất bắt được lỗi thứ tự:
    /// nạp mồi sau <c>LoadFrom</c> thì hồ sơ mới đầu tiên nhận <c>PTN0003</c> và **ghi đè hồ sơ mồi
    /// thứ ba**, không báo gì.
    /// </summary>
    [Fact]
    public async Task I12_kho_rong_nhung_bo_dem_con_so_cu_thi_ho_so_moi_nhan_PTN0004()
    {
        using var data = new TempDataDirectory();
        var seedPath = WriteSeedFile(data, 3);
        await File.WriteAllTextAsync(data.CounterFile, "{ \"last\": 2 }");

        PartnerSeed.EnsureSeeded(data.Path, seedPath);

        using var store = JsonPartnerStore.LoadFrom(data.Path, new SteppingClock());
        var saved = await store.UpsertAsync(
            PartnerFactory.New(fullName: "Ho so moi dau tien", email: "moi@khong-trung.example"),
            CancellationToken.None);

        saved.PartnerId.Should().Be("PTN0004");

        var all = await LoadAll(data);
        all.Should().HaveCount(4);
        all.Select(p => p.PartnerId).Should().Contain(["PTN0001", "PTN0002", "PTN0003"]);
    }

    [Fact]
    public async Task I12_partners_json_la_mang_rong_cung_duoc_coi_la_kho_rong()
    {
        using var data = new TempDataDirectory();
        var seedPath = WriteSeedFile(data, 3);
        await File.WriteAllTextAsync(data.PartnersFile, "[]");

        var result = PartnerSeed.EnsureSeeded(data.Path, seedPath);

        result.Action.Should().Be(SeedAction.Seeded);
        (await LoadAll(data)).Should().HaveCount(3);
    }

    /// <summary>
    /// Đè lên một file hỏng là xoá dữ liệu người dùng để giấu một lỗi. Để nguyên thì
    /// <c>LoadFrom</c> chết với thông báo S-12 của nó — mất dữ liệu thì cũng phải ồn ào.
    /// </summary>
    [Fact]
    public async Task I12_partners_json_hong_cu_phap_thi_khong_bi_de_len()
    {
        using var data = new TempDataDirectory();
        var seedPath = WriteSeedFile(data, 3);
        await File.WriteAllTextAsync(data.PartnersFile, "{ khong phai json");

        var result = PartnerSeed.EnsureSeeded(data.Path, seedPath);

        result.Action.Should().Be(SeedAction.StoreNotEmpty);
        (await File.ReadAllTextAsync(data.PartnersFile)).Should().Be("{ khong phai json");

        var act = () => JsonPartnerStore.LoadFrom(data.Path, new SteppingClock());
        act.Should().Throw<InvalidOperationException>().WithMessage("*partners.json*");
    }

    // ---- Nhóm file mồi thật ---------------------------------------------------------

    [Fact]
    public void File_moi_di_cung_binary_va_co_dung_ba_ho_so()
    {
        File.Exists(PartnerSeed.DefaultSeedPath).Should().BeTrue();

        RealSeed().Should().HaveCount(3);
    }

    /// <summary>
    /// Hai ràng buộc của SPEC mục 19.3 gộp một ca vì chúng nói cùng một điều: hồ sơ mồi không do mô
    /// hình đọc ra. <c>model</c> và <c>promptVersion</c> là <c>"-"</c> chứ không phải <c>"fake"</c>
    /// — <c>"fake"</c> nghĩa là <c>FakeExtractor</c> đã chạy thật.
    ///
    /// <c>ExtractedAt</c> phải có mốc thật: <see cref="ExtractionMeta.ExtractedAt"/> không nullable,
    /// thiếu trong JSON thì lặng lẽ thành <c>0001-01-01</c>.
    /// </summary>
    [Fact]
    public void Ho_so_moi_khong_co_anh_va_khong_khai_la_do_mo_hinh_doc()
    {
        foreach (var partner in RealSeed())
        {
            partner.SourceImage.Should().BeEmpty();
            partner.ImageSha256.Should().BeEmpty();
            partner.Extraction.Model.Should().Be("-");
            partner.Extraction.PromptVersion.Should().Be("-");
            partner.Extraction.LatencyMs.Should().Be(0);
            partner.Extraction.ExtractedAt.Should().NotBe(default);
        }
    }

    [Fact]
    public void Ho_so_moi_co_ca_the_Anh_va_the_Nhat()
    {
        var languages = RealSeed().Select(p => p.DetectedLanguage).ToList();

        languages.Should().Contain("en");
        languages.Should().Contain("ja");
    }

    /// <summary>
    /// Ca giữ kịch bản demo: trùng email thì chụp một thẻ mẫu sẽ bật cảnh báo trùng ngoài kịch bản
    /// (SPEC mục 19.3). Muốn có cảnh báo trùng thì dàn nó có chủ ý, không để nó tự xảy ra.
    /// </summary>
    [Fact]
    public void Email_ho_so_moi_khong_trung_email_cua_bo_the_mau()
    {
        var sampleEmails = SampleCardEmails();
        sampleEmails.Should().NotBeEmpty("ca này vô nghĩa nếu không đọc được expected.json");

        var seedEmails = RealSeed().SelectMany(p => p.Emails).ToList();

        seedEmails.Should().NotBeEmpty();
        seedEmails.Should().OnlyContain(email => !sampleEmails.Contains(email));
    }

    // ---- Dựng cảnh -------------------------------------------------------------------

    /// <summary>File mồi riêng cho ca kiểm luật, đặt ngoài thư mục dữ liệu như lúc chạy thật.</summary>
    private static string WriteSeedFile(TempDataDirectory data, int count)
    {
        var partners = Enumerable.Range(1, count)
            .Select(i => PartnerFactory.New(
                fullName: $"Ho so moi {i}",
                email: $"moi-{i}@hat-giong.example",
                partnerId: $"PTN{i:D4}"))
            .ToList();

        // Thư mục riêng, không phải thư mục dữ liệu — đúng như lúc chạy thật, nơi file mồi nằm cạnh
        // binary chứ không nằm trong data/. Nằm dưới TempDataDirectory nên được dọn cùng nó.
        var directory = Path.Combine(data.Path, "seeddata");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, PartnerSeed.SeedFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(partners, SeedJsonOptions));

        return path;
    }

    private static IReadOnlyList<Partner> RealSeed() =>
        JsonSerializer.Deserialize<List<Partner>>(
            File.ReadAllText(PartnerSeed.DefaultSeedPath), SeedJsonOptions) ?? [];

    private static async Task<IReadOnlyList<Partner>> LoadAll(TempDataDirectory data)
    {
        using var store = JsonPartnerStore.LoadFrom(data.Path, new SteppingClock());

        return await store.SearchAsync(new PartnerQuery(Take: 100), CancellationToken.None);
    }

    /// <summary>Email của 18 thẻ mẫu, đọc thẳng từ bảng đáp án mà FakeExtractor dùng.</summary>
    private static HashSet<string> SampleCardEmails()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "cards", "expected.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in document.RootElement.EnumerateObject())
        {
            if (!card.Value.TryGetProperty("emails", out var list))
            {
                continue;
            }

            foreach (var email in list.EnumerateArray())
            {
                if (email.GetString() is { Length: > 0 } value)
                {
                    emails.Add(value);
                }
            }
        }

        return emails;
    }
}
