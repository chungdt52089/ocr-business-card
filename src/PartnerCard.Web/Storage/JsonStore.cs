using System.Text.Encodings.Web;
using System.Text.Json;

namespace PartnerCard.Web.Storage;

/// <summary>Hai thứ dùng chung cho mọi file JSON trong <c>data/</c>: tuỳ chọn và cách ghi an toàn.</summary>
internal static class JsonStore
{
    /// <summary>
    /// <c>UnsafeRelaxedJsonEscaping</c> để chữ Nhật và tiếng Việt nằm nguyên trong file thay vì
    /// thành <c>\u5F35</c> — file dữ liệu này người sẽ đọc bằng mắt khi gỡ rối.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Ghi ra file tạm rồi đổi tên (SPEC mục 3.1). Tắt máy giữa chừng thì file cũ còn nguyên vẹn,
    /// vì đổi tên là thao tác nguyên tử còn ghi đè trực tiếp thì không (S-15).
    /// </summary>
    public static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Options), ct);
        File.Move(temporary, path, overwrite: true);
    }
}
