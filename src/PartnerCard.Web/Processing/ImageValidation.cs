namespace PartnerCard.Web.Processing;

/// <summary>
/// Mắt xích **Validate** — SPEC mục 2. Nhận chuỗi base64 và tự giải mã, tự kiểm mime và kích thước.
///
/// Cố ý không nhận <c>byte[]</c>: nếu tool tự giải base64 rồi truyền byte vào thì nó đã đi tắt
/// qua đúng bước kiểm này, và mọi bảo đảm của đường ống thành lời hứa suông.
/// </summary>
public static class ImageValidation
{
    /// <summary>Định dạng Gemini nhận (SPEC mục 4.2). Mọi mime khác bị từ chối.</summary>
    public static readonly IReadOnlyList<string> AcceptedMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

    public sealed record Result(
        bool Ok, ReadOnlyMemory<byte> Bytes, string? ErrorCode, string? Message);

    public static Result Validate(string? imageBase64, string? mimeType, int maxImageBytes)
    {
        if (string.IsNullOrWhiteSpace(mimeType)
            || !AcceptedMimeTypes.Contains(mimeType.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return Fail("unsupported_mime",
                $"Định dạng ảnh không được nhận. Chỉ nhận: {string.Join(", ", AcceptedMimeTypes)}.");
        }

        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return Fail("invalid_base64", "Nội dung ảnh rỗng.");
        }

        // Ước lượng kích thước từ độ dài base64 để chặn TRƯỚC khi cấp phát mảng lớn,
        // và trước khi tốn một request hạn mức.
        if (EstimatedBytes(imageBase64) > maxImageBytes)
        {
            return Fail("image_too_large",
                $"Ảnh vượt trần {maxImageBytes / (1024 * 1024)} MB. Thu nhỏ trước khi gửi.");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(imageBase64);
        }
        catch (FormatException)
        {
            return Fail("invalid_base64",
                "Nội dung ảnh không phải base64 hợp lệ. Gửi base64 thuần, không kèm tiền tố data URI.");
        }

        return bytes.Length > maxImageBytes
            ? Fail("image_too_large",
                $"Ảnh vượt trần {maxImageBytes / (1024 * 1024)} MB. Thu nhỏ trước khi gửi.")
            : new Result(true, bytes, null, null);
    }

    private static long EstimatedBytes(string base64) => (long)base64.Length * 3 / 4;

    private static Result Fail(string code, string message) =>
        new(false, ReadOnlyMemory<byte>.Empty, code, message);
}
