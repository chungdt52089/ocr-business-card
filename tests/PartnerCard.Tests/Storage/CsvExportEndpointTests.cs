using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using PartnerCard.Web.Models;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Storage;

/// <summary>
/// T-14 — <c>/export/partners.csv</c> (PRD US-06, SPEC mục 3.1). Nửa tự động của I-06: I-06 mở file bằng Excel
/// thật, ở đây kiểm những gì làm cho Excel mở đúng — BOM, chữ Nhật nguyên vẹn, số điện thoại không bị đọc thành số.
/// </summary>
[Trait("Category", "Store")]
public sealed class CsvExportEndpointTests : IDisposable
{
    private const string Header =
        "partnerId,fullName,jobTitle,company,phones,emails,website,address,detectedLanguage,searchAlias,createdAt,updatedAt";

    private readonly TempDataDirectory _data = new();
    private readonly JsonPartnerStore _store;

    public CsvExportEndpointTests() =>
        _store = JsonPartnerStore.LoadFrom(_data.Path, new SteppingClock());

    public void Dispose()
    {
        _store.Dispose();
        _data.Dispose();
    }

    private async Task<FileContentHttpResult> ExportAsync() =>
        (await CsvExportEndpoint.ServeAsync(_store, CancellationToken.None))
            .Should().BeOfType<FileContentHttpResult>().Subject;

    private static string TextAfterBom(FileContentHttpResult file) =>
        Encoding.UTF8.GetString(file.FileContents.Span[3..]);

    [Fact]
    public async Task Csv_bat_dau_bang_BOM_UTF8_va_chu_Nhat_nguyen_ven()
    {
        await _store.UpsertAsync(
            PartnerFactory.New(fullName: "藤井 玲奈", company: "株式会社常磐電子", email: "r.fujii@tokiwa-denshi.example"),
            CancellationToken.None);

        var file = await ExportAsync();

        file.FileContents.Span[..3].ToArray().Should().Equal([0xEF, 0xBB, 0xBF],
            "thiếu BOM thì Excel trên Windows đọc UTF-8 thành mã trang ANSI — chữ Nhật vỡ (PRD US-06)");
        file.ContentType.Should().Be("text/csv; charset=utf-8");
        file.FileDownloadName.Should().Be("partners.csv");

        var lines = TextAfterBom(file).Split("\r\n");
        lines[0].Should().Be(Header);
        lines[1].Should().StartWith("PTN0001,藤井 玲奈,").And.Contain(",株式会社常磐電子,");
    }

    [Fact]
    public async Task Csv_chi_xuat_ho_so_confirmed_va_so_dien_thoai_giu_nguyen_trong_Excel()
    {
        await _store.UpsertAsync(
            PartnerFactory.New(fullName: "Kenji Arai", email: "k.arai@arai.example")
                with { Phones = ["0335620914", "+81355502277"] },
            CancellationToken.None);
        await _store.UpsertAsync(
            PartnerFactory.New(fullName: "Bản nháp chưa xác nhận", email: "draft@draft.example")
                with { Status = PartnerStatus.Draft },
            CancellationToken.None);

        var csv = TextAfterBom(await ExportAsync());

        csv.Should().NotContain("Bản nháp chưa xác nhận", "chỉ hồ sơ confirmed mới xuất ra CSV (SPEC mục 3.1)");

        // ="…" để Excel giữ số 0 đầu và dấu +; nhiều số chung một ô, ngăn bằng dấu chấm phẩy (PRD US-06).
        csv.Should().Contain(",\"=\"\"0335620914;+81355502277\"\"\",");

        // Giờ Việt Nam cố định +07:00: đồng hồ kiểm thử bắt đầu 08:00 UTC.
        csv.Should().Contain(",2026-09-09T15:00:00+07:00,");
    }

    [Theory]
    [InlineData("fullName", "=1+2", "'=1+2")]
    [InlineData("company", "+SUM(A1)", "'+SUM(A1)")]
    [InlineData("jobTitle", "-2+3", "'-2+3")]
    [InlineData("address", "@SUM(A1)", "'@SUM(A1)")]
    [InlineData("searchAlias", "\t=1+2", "'\t=1+2")]
    [InlineData("address", "\r=1+2", "\"'\r=1+2\"")]
    public async Task Csv_o_van_ban_bat_dau_bang_ky_tu_cong_thuc_bi_vo_hieu(string field, string raw, string expectedCell)
    {
        // Sáu ký tự đầu ô của OWASP (CSV injection). Tên trên danh thiếp là dữ liệu người ngoài đưa vào: một tấm thẻ
        // in "=HYPERLINK(…)" không được thành công thức sống khi mở bằng Excel.
        var partner = PartnerFactory.New();
        partner = field switch
        {
            "fullName" => partner with { FullName = raw },
            "company" => partner with { Company = raw },
            "jobTitle" => partner with { JobTitle = raw },
            "address" => partner with { Address = raw },
            "searchAlias" => partner with { SearchAlias = raw },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };
        await _store.UpsertAsync(partner, CancellationToken.None);

        var csv = TextAfterBom(await ExportAsync());

        csv.Should().Contain($",{expectedCell},", $"ô {field} phải có ' đứng đầu");

        // phones cũng bắt đầu bằng "+" nhưng đã bọc ="…" — không được thêm ' vào đó.
        csv.Should().Contain(",\"=\"\"+15550142887\"\"\",");
    }
}
