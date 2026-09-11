namespace PartnerCard.Eval;

/// <summary>
/// Tìm gốc repo bằng cách đi ngược từ thư mục chứa binary tới khi gặp <c>PartnerCard.slnx</c>.
///
/// **Vì sao không chép ảnh sang thư mục output** như <c>PartnerCard.Web.csproj</c> làm với
/// <c>expected.json</c>: thư mục <c>realcards/</c> nặng ~5,9 MB, chép chúng vào <c>bin/</c> mỗi
/// lần build cho một công cụ chỉ chạy trong repo là phí. Lối chép ấy tồn tại để **ứng dụng** chạy
/// được từ thư mục làm việc bất kỳ; bộ đo thì theo định nghĩa luôn chạy trong repo.
/// </summary>
public static class RepoPaths
{
    private const string Marker = "PartnerCard.slnx";

    public static string Root { get; } = FindRoot();

    /// <summary>Ảnh chụp thật — đây mới là con số nghiệm thu (SPEC mục 14).</summary>
    public static string RealCards =>
        Path.Combine(Root, "tests", "PartnerCard.Tests", "TestData", "realcards");

    /// <summary>Bản render. Chỉ dùng khi <see cref="RealCards"/> rỗng, và phải kèm cảnh báo.</summary>
    public static string RenderedCards =>
        Path.Combine(Root, "tests", "PartnerCard.Tests", "TestData", "cards");

    public static string ExpectedJson => Path.Combine(RenderedCards, "expected.json");

    /// <summary>Cấu hình của ứng dụng — bộ đo đọc chung để chạy đúng thứ đang chạy thật.</summary>
    public static string WebProjectDirectory =>
        Path.Combine(Root, "src", "PartnerCard.Web");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Không tìm thấy {Marker} từ {AppContext.BaseDirectory} đi ngược lên. " +
            "Bộ đo phải chạy bên trong repo; nếu cố ý chạy chỗ khác thì truyền --dir.");
    }
}
