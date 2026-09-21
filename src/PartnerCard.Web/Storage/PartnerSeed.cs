using System.Text.Json;
using PartnerCard.Web.Models;

namespace PartnerCard.Web.Storage;

/// <summary>
/// Nạp dữ liệu mồi khi kho rỗng — SPEC mục 19.3.
///
/// > Lúc khởi động, nếu <c>partners.json</c> không tồn tại hoặc rỗng, nạp <c>partners.seed.json</c>.
/// > Có dữ liệu rồi thì không đụng vào.
///
/// **Viết "nạp khi rỗng" chứ không "nạp đè mỗi lần khởi động"** để luật đúng bất kể đĩa có bền hay
/// không: đĩa tạm thì mỗi lần khởi động đều rỗng nên luôn nạp; đĩa bền thì chỉ nạp lần đầu và giữ
/// nguyên những gì đã lưu.
///
/// **Hai chỗ đặt, cả hai đều là điều kiện chứ không phải sở thích:**
///
/// 1. **Chạy TRƯỚC <see cref="JsonPartnerStore.LoadFrom"/>, không phải sau.** <c>LoadFrom</c> tính mã
///    tiếp theo bằng số lớn hơn giữa bộ đếm và mã cao nhất trong kho, nên chép file mồi vào trước thì
///    nó thấy luôn mã của mồi. Nạp mồi sau thì mã tiếp theo đã chốt từ bộ đếm cũ, và hồ sơ mới đầu
///    tiên **đè lên một hồ sơ mồi, không báo gì** — đúng thứ đã suýt xảy ra trên máy Chung ngày
///    21/09: kho rỗng nhưng <c>counter.json</c> còn <c>{"last": 2}</c> từ buổi kiểm MCP.
/// 2. **Gọi từ tầng khởi động, không nằm trong <c>LoadFrom</c>.** Các ca <c>S-</c> dựng kho bằng thư
///    mục tạm rồi đếm hồ sơ; <c>LoadFrom</c> tự nạp mồi là làm đỏ cả nhóm.
/// </summary>
internal static class PartnerSeed
{
    public const string SeedFileName = "partners.seed.json";

    /// <summary>Tên biến môi trường nêu trong thông báo lỗi, để người đọc log biết chỗ sửa.</summary>
    private const string DataDirectoryVariable = "PartnerCard__DataDirectory";

    private const string PartnersFileName = "partners.json";

    /// <summary>Bản file mồi mà csproj chép sang cạnh binary — cùng luật với <c>fakedata\expected.json</c>.</summary>
    public static string DefaultSeedPath =>
        Path.Combine(AppContext.BaseDirectory, "seeddata", SeedFileName);

    /// <summary>
    /// Chép file mồi vào thư mục dữ liệu nếu kho đang rỗng. Trả về chuyện đã xảy ra để tầng khởi
    /// động in ra log — không tự ghi log, vì lúc này host chưa dựng nên chưa có <c>ILogger</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// File mồi hỏng, hoặc không ghi được thư mục dữ liệu. Cả hai đều làm tiến trình thoát mã 1:
    /// chạy tiếp thì trang danh sách trống mà không ai biết vì sao.
    /// </exception>
    public static SeedResult EnsureSeeded(string dataDirectory, string seedPath)
    {
        var partnersPath = Path.Combine(dataDirectory, PartnersFileName);

        if (!IsEmptyStore(partnersPath))
        {
            return new SeedResult(SeedAction.StoreNotEmpty, 0, "Kho đã có dữ liệu — không nạp mồi.");
        }

        if (!File.Exists(seedPath))
        {
            return new SeedResult(
                SeedAction.SeedFileMissing, 0,
                $"Không thấy file mồi {seedPath} — chạy tiếp với kho rỗng.");
        }

        // Đọc trước khi chép: file mồi hỏng thì thông báo phải chỉ vào file mồi, chứ không phải
        // vào partners.json sau khi nó đã bị chép đè bằng rác.
        var count = CountSeedPartners(seedPath);

        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.Copy(seedPath, partnersPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Không ghi được thư mục dữ liệu {dataDirectory}. " +
                $"Đặt {DataDirectoryVariable} thành một đường dẫn tuyệt đối mà tiến trình ghi được.",
                ex);
        }

        return new SeedResult(SeedAction.Seeded, count, $"Kho rỗng — đã nạp {count} hồ sơ mồi.");
    }

    /// <summary>
    /// Rỗng = không có file, chỉ có khoảng trắng, hoặc là mảng <c>[]</c>.
    ///
    /// File **hỏng cú pháp** cố ý trả <c>false</c>: không đè. <see cref="JsonPartnerStore.LoadFrom"/>
    /// sẽ chết ngay sau đó với thông báo S-12 của nó. Đè lên một file hỏng là xoá dữ liệu người dùng
    /// để giấu một lỗi.
    /// </summary>
    private static bool IsEmptyStore(string partnersPath)
    {
        if (!File.Exists(partnersPath))
        {
            return true;
        }

        string text;
        try
        {
            text = File.ReadAllText(partnersPath);
        }
        catch (IOException)
        {
            // Đọc không được thì càng không được đè.
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(text);

            return document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int CountSeedPartners(string seedPath)
    {
        List<Partner>? partners;

        try
        {
            partners = JsonSerializer.Deserialize<List<Partner>>(
                File.ReadAllText(seedPath), JsonStore.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Không đọc được file mồi {seedPath}: file không phải JSON hợp lệ. Sửa file rồi chạy lại.",
                ex);
        }

        if (partners is null || partners.Count == 0)
        {
            throw new InvalidOperationException(
                $"File mồi {seedPath} không có hồ sơ nào. Xoá file nếu không cần mồi, " +
                "đừng để lại một file rỗng — nó trông giống hệt một lần nạp đã chạy và không ra gì.");
        }

        return partners.Count;
    }
}

/// <summary>Chuyện đã xảy ra ở lần gọi <see cref="PartnerSeed.EnsureSeeded"/>.</summary>
internal enum SeedAction
{
    /// <summary>Kho rỗng, đã chép file mồi vào.</summary>
    Seeded,

    /// <summary>Kho đã có dữ liệu — không đụng vào.</summary>
    StoreNotEmpty,

    /// <summary>Không có file mồi. Không phải lỗi chết người: danh sách trống thì nhìn là thấy.</summary>
    SeedFileMissing,
}

/// <param name="Count">Số hồ sơ đã nạp. Bằng 0 ở mọi trường hợp không nạp.</param>
/// <param name="Message">Một dòng cho log khởi động.</param>
internal sealed record SeedResult(SeedAction Action, int Count, string Message);
