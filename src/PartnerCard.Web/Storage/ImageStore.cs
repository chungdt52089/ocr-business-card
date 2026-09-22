using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PartnerCard.Web.Storage;

/// <summary>
/// Ảnh gốc đã thu nhỏ, trên đĩa — SPEC mục 4.1, quyết định D-1 (giữ ảnh gốc).
///
/// **Tên file là mã băm, không phải tên gốc.** iPhone đặt tên mọi ảnh là <c>image.jpg</c>, nên lưu
/// theo tên gốc là đè nhau ngay tấm thứ hai. Đặt theo SHA-256 thì nội dung tự định địa chỉ: chụp
/// lại đúng tấm thẻ đó bao nhiêu lần cũng chỉ một file.
///
/// Trả về **mã băm**, không trả đường dẫn. Đường dẫn suy ra được từ nó, mà trả cả hai là mời người
/// sau lưu cả hai vào hồ sơ — đúng chỗ trùng lặp mà bảng "hai trường, hai việc" ở SPEC mục 4.1
/// vừa gỡ: <c>SourceImage</c> giữ tên gốc vì đó là thông tin không có ở đâu khác trong hồ sơ.
///
/// Đường ống **không** gọi class này: nhánh trích xuất không persist gì (SPEC mục 2), và nhánh lưu
/// nhận sẵn mã băm trong <c>PartnerDraft</c>. Chỗ gọi là tầng giao diện, nơi có byte ảnh trong tay.
/// </summary>
public sealed partial class ImageStore(string dataDirectory)
{
    public const string FolderName = "images";

    public string FolderPath => Path.Combine(dataDirectory, FolderName);

    /// <summary>
    /// Đường dẫn trên đĩa của một ảnh đã lưu, hoặc <c>null</c>. Đây là chốt kiểm đường dẫn của endpoint
    /// ảnh (SPEC mục 11.3): **chỉ nhận đúng dạng <see cref="SaveAsync"/> ghi ra** — 64 ký tự hex thường
    /// cộng <c>.jpg</c>. Không cố "làm sạch" tên lạ: <c>../partners.json</c>, tên viết HOA, đuôi khác hay
    /// một file tự đặt vào <c>images/</c> đều trả <c>null</c> như nhau.
    /// </summary>
    public string? PathOf(string? fileName)
    {
        if (fileName is null || !FileNamePattern().IsMatch(fileName))
        {
            return null;
        }

        var path = Path.Combine(FolderPath, fileName);

        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Ảnh của mã băm này còn trên đĩa không. Hồ sơ mồi và hồ sơ lưu qua MCP có mã băm rỗng; trên
    /// Cloud Run ảnh mất khi instance được thay (SPEC 19.3) — màn hình xác nhận hiện ô trống thay vì ảnh vỡ.
    /// </summary>
    public bool Exists(string? imageSha256) =>
        !string.IsNullOrEmpty(imageSha256) && PathOf($"{imageSha256}.jpg") is not null;

    /// <summary>
    /// Ghi ảnh đã thu nhỏ, trả SHA-256 của nó — vừa là danh tính nội dung, vừa là tên file trên đĩa.
    /// </summary>
    public async Task<string> SaveAsync(string imageBase64, CancellationToken ct)
    {
        var bytes = Convert.FromBase64String(imageBase64);
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));

        // Tạo thư mục ở mỗi lần ghi, y như JsonlAuditLogger: data/images/ nằm trong .gitignore nên
        // bản clone mới không có nó, và một lần dọn tay giữa chừng cũng không được làm hỏng lần
        // chụp kế tiếp.
        Directory.CreateDirectory(FolderPath);

        var path = Path.Combine(FolderPath, $"{sha}.jpg");
        if (File.Exists(path))
        {
            // Cùng mã băm là cùng byte. Ghi lại không sai, chỉ thừa.
            return sha;
        }

        // Ghi file tạm rồi đổi tên, cùng lý do JsonStore.WriteAtomicAsync làm vậy: tắt máy giữa
        // chừng thì không để lại một file JPEG cụt mang đúng tên của nội dung đầy đủ — mà tên ấy
        // là mã băm, tức là một lời hứa về nội dung.
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, ct);
        File.Move(temporary, path, overwrite: true);

        return sha;
    }

    // \z chứ không $: $ của .NET còn khớp trước một ký tự xuống dòng ở cuối chuỗi.
    [GeneratedRegex(@"^[0-9a-f]{64}\.jpg\z")]
    private static partial Regex FileNamePattern();
}
