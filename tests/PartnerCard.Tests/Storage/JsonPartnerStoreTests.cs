using System.Text.Json;
using PartnerCard.Web.Models;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Storage;

/// <summary>TEST-SPEC mục 1 — các ca thuộc T-02.</summary>
[Trait("Category", "Store")]
public sealed class JsonPartnerStoreTests
{
    private static JsonPartnerStore Open(TempDataDirectory data) =>
        JsonPartnerStore.LoadFrom(data.Path, new SteppingClock());

    // ---- S-01 → S-03 · lấy theo mã -------------------------------------------------

    [Fact]
    public async Task S01_lay_ho_so_theo_ma_ton_tai()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);
        var saved = await store.UpsertAsync(PartnerFactory.New(), CancellationToken.None);

        var found = await store.GetAsync(saved.PartnerId, CancellationToken.None);

        found.Should().NotBeNull();
        found!.FullName.Should().Be("Marcus Feld");
    }

    [Fact]
    public async Task S02_ma_khong_ton_tai_tra_null_khong_nem_exception()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);

        var found = await store.GetAsync("PTN9999", CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task S03_ma_chu_thuong_van_tim_thay()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);
        await store.UpsertAsync(PartnerFactory.New(), CancellationToken.None);

        var found = await store.GetAsync("ptn0001", CancellationToken.None);

        found.Should().NotBeNull();
        found!.PartnerId.Should().Be("PTN0001");
    }

    // ---- S-04 → S-06 · cấp mã và vòng đời ------------------------------------------

    [Fact]
    public async Task S04_ho_so_moi_duoc_cap_ma_tang_dan_va_hai_moc_thoi_gian_bang_nhau()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);

        var first = await store.UpsertAsync(PartnerFactory.New(), CancellationToken.None);
        var second = await store.UpsertAsync(
            PartnerFactory.New(fullName: "Priya Raman"), CancellationToken.None);

        first.PartnerId.Should().Be("PTN0001");
        second.PartnerId.Should().Be("PTN0002");
        first.CreatedAt.Should().Be(first.UpdatedAt);
    }

    [Fact]
    public async Task S05_ghi_de_giu_nguyen_createdAt_va_doi_updatedAt()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);
        var original = await store.UpsertAsync(PartnerFactory.New(), CancellationToken.None);

        var updated = await store.UpsertAsync(
            original with { JobTitle = "Chief Operations Officer" }, CancellationToken.None);

        updated.PartnerId.Should().Be(original.PartnerId);
        updated.CreatedAt.Should().Be(original.CreatedAt);
        updated.UpdatedAt.Should().BeAfter(original.UpdatedAt);
        updated.JobTitle.Should().Be("Chief Operations Officer");
    }

    [Fact]
    public async Task S06_ma_da_xoa_khong_duoc_cap_lai()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);
        var first = await store.UpsertAsync(PartnerFactory.New(), CancellationToken.None);

        var removed = await store.DeleteAsync(first.PartnerId, "ca kiểm thử", CancellationToken.None);
        var second = await store.UpsertAsync(
            PartnerFactory.New(fullName: "Priya Raman"), CancellationToken.None);

        removed.Should().BeTrue();
        second.PartnerId.Should().NotBe(first.PartnerId);
    }

    // ---- S-07 → S-09 · giới hạn số bản ghi trả về ----------------------------------

    [Fact]
    public async Task S07_khong_truyen_tieu_chi_tra_20_ban_ghi_moi_nhat()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);
        await SeedAsync(store, 25);

        var results = await store.SearchAsync(new PartnerQuery(), CancellationToken.None);

        results.Should().HaveCount(20);
        results[0].FullName.Should().Be("Đối tác 25", "bản mới nhất phải đứng đầu");
        results[^1].FullName.Should().Be("Đối tác 6");
    }

    [Fact]
    public async Task S08_take_500_bi_cat_ve_100()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);
        await SeedAsync(store, 105);

        var results = await store.SearchAsync(new PartnerQuery(Take: 500), CancellationToken.None);

        results.Should().HaveCount(100);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task S09_take_khong_hop_le_dung_mac_dinh_20(int take)
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);
        await SeedAsync(store, 25);

        var results = await store.SearchAsync(new PartnerQuery(Take: take), CancellationToken.None);

        results.Should().HaveCount(20);
    }

    // ---- S-12, S-13 · nạp file lúc khởi động ---------------------------------------

    [Fact]
    public void S12_file_hong_cu_phap_thi_bao_loi_ro_va_khong_chay_tiep()
    {
        using var data = new TempDataDirectory();
        File.WriteAllText(data.PartnersFile, "{ đây không phải JSON");

        var act = () => JsonPartnerStore.LoadFrom(data.Path, new SteppingClock());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*partners.json*không phải JSON hợp lệ*");
    }

    [Fact]
    public async Task S13_file_chua_ton_tai_thi_tu_tao_mang_rong_va_khoi_dong_binh_thuong()
    {
        using var data = new TempDataDirectory();
        File.Exists(data.PartnersFile).Should().BeFalse();

        using var store = Open(data);
        var results = await store.SearchAsync(new PartnerQuery(), CancellationToken.None);
        var saved = await store.UpsertAsync(PartnerFactory.New(), CancellationToken.None);

        results.Should().BeEmpty();
        saved.PartnerId.Should().Be("PTN0001");
        File.Exists(data.PartnersFile).Should().BeTrue();
    }

    // ---- S-14, S-15 · ghi đồng thời và ghi nguyên tử -------------------------------
    //
    // Hai ca này chưa được BACKLOG giao cho task nào, nhưng chúng đúng là thứ chứng minh
    // SemaphoreSlim và luật ghi-file-tạm-rồi-đổi-tên có tác dụng. Bỏ chúng đi thì hai
    // yêu cầu đó của SPEC mục 3.1 và 3.3 không có gì bảo vệ.

    [Fact]
    public async Task S14_ghi_dong_thoi_50_ho_so_du_50_ban_ghi_ma_khong_trung_file_khong_hong()
    {
        using var data = new TempDataDirectory();
        using var store = Open(data);

        var writes = Enumerable.Range(1, 50).Select(i => Task.Run(
            () => store.UpsertAsync(
                PartnerFactory.New(fullName: $"Đối tác {i}", email: $"p{i}@example.example"),
                CancellationToken.None),
            CancellationToken.None));

        var saved = await Task.WhenAll(writes);

        saved.Select(p => p.PartnerId).Should().OnlyHaveUniqueItems();
        saved.Should().HaveCount(50);

        var onDisk = JsonSerializer.Deserialize<List<Partner>>(
            await File.ReadAllTextAsync(data.PartnersFile, CancellationToken.None),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        onDisk.Should().NotBeNull();
        onDisk!.Should().HaveCount(50);
    }

    [Fact]
    public async Task S15_file_tam_con_sot_lai_khong_lam_hong_file_cu()
    {
        using var data = new TempDataDirectory();

        // Lần ghi trước tắt giữa chừng: file tạm còn lại với nội dung dở dang,
        // file thật vẫn là bản hoàn chỉnh của lần ghi thành công gần nhất.
        using (var first = Open(data))
        {
            await first.UpsertAsync(PartnerFactory.New(), CancellationToken.None);
        }

        await File.WriteAllTextAsync(
            data.PartnersFile + ".tmp", "[ { \"partnerId\": \"PTN00",
            CancellationToken.None);

        using var store = Open(data);
        var reloaded = await store.GetAsync("PTN0001", CancellationToken.None);
        var next = await store.UpsertAsync(
            PartnerFactory.New(fullName: "Priya Raman", email: "priya@example.example"),
            CancellationToken.None);

        reloaded.Should().NotBeNull("file cũ phải còn nguyên vẹn sau một lần ghi hỏng");
        next.PartnerId.Should().Be("PTN0002");
    }

    private static async Task SeedAsync(IPartnerStore store, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await store.UpsertAsync(
                PartnerFactory.New(fullName: $"Đối tác {i}", email: $"p{i}@example.example"),
                CancellationToken.None);
        }
    }
}
