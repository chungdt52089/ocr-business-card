using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Storage;

/// <summary>
/// T-13 — endpoint ảnh có kiểm đường dẫn (SPEC mục 11.3). Thư mục dữ liệu còn chứa kho, nên chỉ đúng dạng
/// tên <c>ImageStore</c> ghi ra mới được phục vụ; mọi thứ khác là 404 như nhau.
/// </summary>
[Trait("Category", "Store")]
public sealed class ImageEndpointTests : IDisposable
{
    private readonly TempDataDirectory _data = new();
    private readonly ImageStore _images;

    public ImageEndpointTests()
    {
        _images = new ImageStore(_data.Path);

        // Thứ kẻ dò đường dẫn nhắm tới: kho nằm ngay cạnh thư mục ảnh, và một file lạ nằm trong đó.
        File.WriteAllText(_data.PartnersFile, "[]");
        Directory.CreateDirectory(_images.FolderPath);
        File.WriteAllText(Path.Combine(_images.FolderPath, "evil.jpg"), "không phải ảnh do ImageStore ghi");
    }

    public void Dispose() => _data.Dispose();

    private Task<string> SaveSampleAsync() =>
        _images.SaveAsync(
            Convert.ToBase64String(File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "TestData", "cards", "en-01.png"))),
            CancellationToken.None);

    [Fact]
    public async Task Anh_da_luu_tra_ve_file_jpeg_kem_cache_vinh_vien()
    {
        var sha = await SaveSampleAsync();
        var http = new DefaultHttpContext();

        var result = ImageEndpoint.Serve($"{sha}.jpg", _images, http);

        var file = result.Should().BeOfType<PhysicalFileHttpResult>().Subject;
        file.ContentType.Should().Be("image/jpeg");
        file.FileName.Should().Be(Path.Combine(_images.FolderPath, $"{sha}.jpg"));
        http.Response.Headers.CacheControl.ToString().Should().Contain("immutable",
            "tên file là mã băm nội dung — cùng tên là cùng byte");

        ImageEndpoint.UrlFor(sha).Should().Be($"images/{sha}.jpg");
    }

    [Theory]
    [InlineData("../partners.json")]
    [InlineData("..%2Fpartners.json")]
    [InlineData("..\\partners.json")]
    [InlineData("partners.json")]
    [InlineData("counter.json")]
    [InlineData("evil.jpg")]
    [InlineData("")]
    public void Ten_file_sai_dang_bi_404_ke_ca_khi_file_co_that(string fileName)
    {
        var http = new DefaultHttpContext();

        var result = ImageEndpoint.Serve(fileName, _images, http);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(404);
        http.Response.Headers.CacheControl.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Gan_dung_dang_sha_van_bi_404()
    {
        var sha = await SaveSampleAsync();

        string[] nearMisses =
        [
            $"{sha.ToUpperInvariant()}.jpg",   // ImageStore ghi hex chữ thường
            $"{sha}.JPG",
            $"{sha}.png",
            $"{sha}.jpg\n",                    // $ của .NET khớp trước ký tự xuống dòng cuối — mẫu dùng \z
            $"{sha[..63]}.jpg",
            $"{new string('0', 64)}.jpg",      // đúng dạng nhưng không có file
        ];

        foreach (var name in nearMisses)
        {
            ImageEndpoint.Serve(name, _images, new DefaultHttpContext())
                .Should().BeAssignableTo<IStatusCodeHttpResult>()
                .Which.StatusCode.Should().Be(404, $"'{name.Replace("\n", "\\n")}' không phải tên ImageStore ghi ra");
        }
    }

    [Fact]
    public async Task Exists_chi_dung_khi_anh_con_tren_dia()
    {
        var sha = await SaveSampleAsync();

        _images.Exists(sha).Should().BeTrue();
        _images.Exists(string.Empty).Should().BeFalse("hồ sơ mồi và hồ sơ lưu qua MCP có mã băm rỗng");
        _images.Exists(null).Should().BeFalse();
        _images.Exists("../partners").Should().BeFalse();

        File.Delete(Path.Combine(_images.FolderPath, $"{sha}.jpg"));
        _images.Exists(sha).Should().BeFalse("Cloud Run thay instance là ảnh mất (SPEC 19.3)");
    }
}
