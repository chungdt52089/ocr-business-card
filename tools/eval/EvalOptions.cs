namespace PartnerCard.Eval;

/// <summary>Tham số dòng lệnh của bộ đo.</summary>
public sealed record EvalOptions
{
    /// <summary>File kết quả. Rỗng thì Program dùng <c>&lt;gốc repo&gt;/EVAL.md</c>.</summary>
    public string? OutPath { get; init; }

    /// <summary>Ghi đè thay vì nối thêm khối mới.</summary>
    public bool Overwrite { get; init; }

    /// <summary><c>fake</c> hoặc <c>gemini</c>. Rỗng thì theo cấu hình.</summary>
    public string? Extractor { get; init; }

    /// <summary>Chỉ chạy các mã thẻ này — thử bộ đo bằng 3 request thay vì 19.</summary>
    public IReadOnlyList<string> Cards { get; init; } = [];

    /// <summary>Nghỉ giữa các lượt gọi. Rỗng thì Program chọn theo chế độ.</summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>Đè thư mục ảnh.</summary>
    public string? Dir { get; init; }

    public bool Help { get; init; }

    public sealed record Parsed(EvalOptions? Options, string? Error);

    public const string Usage = """
        Bộ đo PartnerCard (T-08) — chạy IExtractor trên bộ ảnh chụp thật và chấm theo expected.json.

          dotnet run --project tools/eval -- [cờ]

          --out <path>            File kết quả. Mặc định <gốc repo>/EVAL.md.
                                  Đường dẫn tường minh tính theo thư mục hiện tại, nên
                                  --out ..\PartnerCard\docs\EVAL.md chạy đúng khi đứng ở Code\
          --overwrite             Ghi đè cả file thay vì nối thêm một khối mới
          --extractor fake|gemini Đè cấu hình. Không truyền thì đọc appsettings
          --cards a,b,c           Chỉ chạy các mã thẻ này (vd: en-01,ja-03)
          --delay <giây>          Nghỉ giữa các lượt gọi. Mặc định 3 ở gemini, 0 ở fake
          --dir <path>            Đè thư mục ảnh. Mặc định TestData/realcards
          --help

        Cổng bắt buộc: chạy --extractor fake phải ra 72/72 (100%) trước mọi lời gọi thật.
        FakeExtractor đọc chính expected.json, nên con số nào khác 100% là lỗi của bộ đo.
        """;

    public static Parsed Parse(string[] args)
    {
        var options = new EvalOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];

            switch (flag)
            {
                case "--help" or "-h":
                    return new Parsed(options with { Help = true }, null);

                case "--overwrite":
                    options = options with { Overwrite = true };
                    continue;
            }

            if (i + 1 >= args.Length)
            {
                return new Parsed(null, $"Cờ {flag} thiếu giá trị đi kèm.");
            }

            var value = args[++i];

            switch (flag)
            {
                case "--out":
                    options = options with { OutPath = value };
                    break;

                case "--dir":
                    options = options with { Dir = value };
                    break;

                case "--extractor":
                    if (!string.Equals(value, "fake", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(value, "gemini", StringComparison.OrdinalIgnoreCase))
                    {
                        return new Parsed(null, $"--extractor chỉ nhận fake hoặc gemini, không nhận {value}.");
                    }

                    options = options with { Extractor = value.ToLowerInvariant() };
                    break;

                case "--cards":
                    var codes = value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();

                    if (codes.Count == 0)
                    {
                        return new Parsed(null, "--cards không có mã thẻ nào.");
                    }

                    options = options with { Cards = codes };
                    break;

                case "--delay":
                    if (!double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                        || seconds < 0)
                    {
                        return new Parsed(null, $"--delay phải là số giây không âm, không phải {value}.");
                    }

                    options = options with { Delay = TimeSpan.FromSeconds(seconds) };
                    break;

                default:
                    return new Parsed(null, $"Không hiểu cờ {flag}.");
            }
        }

        return new Parsed(options, null);
    }
}
