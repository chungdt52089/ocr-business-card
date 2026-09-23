using System.Text;
using PartnerCard.Web.Models;

namespace PartnerCard.Web.Storage;

/// <summary>
/// <c>GET /export/partners.csv</c> — PRD US-06, SPEC mục 3.1. REST thường, **không** phải tool MCP (SPEC mục 10.4:
/// không có tool xuất file). Chỉ đọc kho, như <see cref="ImageEndpoint"/>.
///
/// Người đọc file này là **Excel trên Windows**, và mọi luật dưới đây phục vụ đúng người đọc đó:
/// <list type="bullet">
/// <item>BOM UTF-8 — thiếu nó Excel đọc file theo mã trang ANSI, chữ Nhật và tiếng Việt vỡ hết.</item>
/// <item><c>phones</c> ghi dạng <c>="…"</c> — không thì Excel đọc <c>+442079460580</c> thành <c>4,42E+11</c> và bỏ
/// số 0 đầu của <c>0335620914</c>.</item>
/// <item>Ô văn bản bắt đầu bằng ký tự công thức được thêm <c>'</c> ở đầu (OWASP, CSV injection). Chữ trên danh thiếp
/// là dữ liệu người ngoài đưa vào: một tấm thẻ in <c>=HYPERLINK(…)</c> không được thành công thức sống.</item>
/// </list>
/// </summary>
public static class CsvExportEndpoint
{
    public const string Route = "/export/partners.csv";

    /// <summary>URL tương đối theo <c>&lt;base href="/"&gt;</c>, cho nút "Xuất CSV" của trang Lịch sử.</summary>
    public const string Url = "export/partners.csv";

    private const string ContentType = "text/csv; charset=utf-8";
    private const string FileName = "partners.csv";

    // searchAlias đứng cột riêng: SPEC 4.4 chỉ cấm đưa nó vào cột tên hay cột công ty — nó là phiên âm để tìm,
    // không phải tên thật.
    private static readonly string[] Header =
    [
        "partnerId", "fullName", "jobTitle", "company", "phones", "emails", "website", "address",
        "detectedLanguage", "searchAlias", "createdAt", "updatedAt",
    ];

    /// <summary>Sáu ký tự đầu ô mà Excel có thể coi là mở đầu công thức — danh sách của OWASP.</summary>
    private static readonly char[] FormulaStarters = ['=', '+', '-', '@', '\t', '\r'];

    private static readonly char[] NeedsQuoting = [',', ';', '"', '\r', '\n'];

    /// <summary>
    /// Đọc tối đa <see cref="QueryLimits.MaxTake"/> hồ sơ: <see cref="IPartnerStore"/> (SPEC 3.3) không có hàm
    /// "lấy tất cả". Đủ cho quy mô demo; kho vượt 100 hồ sơ thì file thiếu những hồ sơ lâu không cập nhật nhất.
    /// </summary>
    public static async Task<IResult> ServeAsync(IPartnerStore store, CancellationToken ct)
    {
        var partners = await store.SearchAsync(new PartnerQuery(Take: QueryLimits.MaxTake), ct);

        return Results.File(Build(partners), ContentType, FileName);
    }

    /// <summary>Chỉ hồ sơ <c>confirmed</c> (SPEC 3.1), mới quét nhất trên cùng — cùng thứ tự với trang Lịch sử.</summary>
    internal static byte[] Build(IEnumerable<Partner> partners)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Header);

        var confirmed = partners
            .Where(p => p.Status == PartnerStatus.Confirmed)
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.PartnerId, StringComparer.Ordinal);

        foreach (var p in confirmed)
        {
            AppendRow(csv,
            [
                Text(p.PartnerId),
                Text(p.FullName),
                Text(p.JobTitle),
                Text(p.Company),
                Phones(p.Phones),
                // Nhiều số, nhiều email chung một ô, ngăn bằng dấu chấm phẩy (PRD US-06).
                Text(string.Join(";", p.Emails)),
                Text(p.Website),
                Text(p.Address),
                Text(p.DetectedLanguage),
                Text(p.SearchAlias),
                VietnamTime.Iso(p.CreatedAt),
                VietnamTime.Iso(p.UpdatedAt),
            ]);
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        return [.. utf8.GetPreamble(), .. utf8.GetBytes(csv.ToString())];
    }

    /// <summary>Ô văn bản: thêm <c>'</c> khi bắt đầu bằng ký tự công thức.</summary>
    private static string Text(string? value) =>
        string.IsNullOrEmpty(value) || Array.IndexOf(FormulaStarters, value[0]) < 0
            ? value ?? string.Empty
            : "'" + value;

    /// <summary>
    /// <c>="…"</c>: Excel tính ra đúng chuỗi bên trong, không đổi thành số. Công thức do chính ta viết, và giá trị bên
    /// trong đã qua SG-5 (chỉ còn chữ số và <c>+</c>) — vẫn nhân đôi dấu nháy cho chắc. Không có số thì để ô trống.
    /// </summary>
    private static string Phones(IReadOnlyList<string> phones) =>
        phones.Count == 0
            ? string.Empty
            : "=\"" + string.Join(";", phones).Replace("\"", "\"\"") + "\"";

    /// <summary>RFC 4180: bọc ngoặc kép khi cần, nhân đôi dấu nháy bên trong, xuống dòng CRLF.</summary>
    private static void AppendRow(StringBuilder csv, IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                csv.Append(',');
            }

            var cell = cells[i];
            if (cell.IndexOfAny(NeedsQuoting) < 0)
            {
                csv.Append(cell);
                continue;
            }

            csv.Append('"').Append(cell.Replace("\"", "\"\"")).Append('"');
        }

        csv.Append("\r\n");
    }
}
