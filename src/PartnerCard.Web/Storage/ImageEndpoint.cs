namespace PartnerCard.Web.Storage;

/// <summary>
/// Phục vụ ảnh gốc từ <c>data/images/</c> — SPEC mục 11.3. **Không** phục vụ <c>data/</c> như file tĩnh:
/// thư mục đó còn chứa <c>partners.json</c> và <c>counter.json</c>, và một <c>UseStaticFiles</c> trỏ nhầm
/// là mở cả kho ra ngoài.
///
/// Màn hình xác nhận dùng nó cho ảnh bên trái; T-14 dùng lại nguyên URL cho ảnh thu nhỏ (thu nhỏ bằng CSS,
/// server không đụng pixel — SPEC mục 1).
/// </summary>
public static class ImageEndpoint
{
    public const string Route = "/images/{fileName}";

    /// <summary>
    /// URL tương đối theo <c>&lt;base href="/"&gt;</c>. Chỉ gọi khi <see cref="ImageStore.Exists"/> đúng.
    /// </summary>
    public static string UrlFor(string imageSha256) => $"images/{imageSha256}.jpg";

    /// <summary>
    /// Sai dạng tên hay không có file đều ra <c>404</c> như nhau — không nói cho người dò đường dẫn biết
    /// mình trượt ở bước nào.
    /// </summary>
    public static IResult Serve(string fileName, ImageStore images, HttpContext http)
    {
        if (images.PathOf(fileName) is not { } path)
        {
            return Results.NotFound();
        }

        // Tên file là mã băm nội dung: cùng tên thì cùng byte, mãi mãi — cache vĩnh viễn là đúng.
        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return Results.File(path, "image/jpeg");
    }
}
