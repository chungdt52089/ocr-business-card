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

    /// <summary>Đè <c>PartnerCard:Model</c> — để so hai model mà chỉ khác đúng một biến.</summary>
    public string? Model { get; init; }

    /// <summary>Tắt việc thử lại thẻ dính trần phút.</summary>
    public bool NoRetry { get; init; }

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
          --model <id>            Đè PartnerCard:Model. Để so hai model trong EVAL.md mà chỉ
                                  khác đúng một biến (vd: gemini-3.5-flash-lite)
          --cards a,b,c           Chỉ chạy các mã thẻ này (vd: en-01,ja-03)
          --delay <giây>          Nghỉ giữa các lượt gọi. Mặc định 0 ở fake; ở gemini thì suy
                                  từ trần phút của model: 3.8 Flash 13s · 3.5 Flash Lite 5s
          --no-retry              Không thử lại thẻ dính trần phút (mặc định: chờ 60s, thử lại
                                  đúng một lần). Trần NGÀY thì không bao giờ thử lại
          --dir <path>            Đè thư mục ảnh. Mặc định TestData/realcards
          --help

        Cổng bắt buộc: chạy --extractor fake phải ra 72/72 (100%) trước mọi lời gọi thật.
        FakeExtractor đọc chính expected.json, nên con số nào khác 100% là lỗi của bộ đo.

        Hạn mức quan sát ngày 11/09: 3.8 Flash 5 RPM / 20 RPD · 3.5 Flash Lite 15 RPM / 500 RPD.
        Một lượt trọn bộ tốn 19 request, tức VƯỢT trần ngày của 3.8 Flash.
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

                case "--no-retry":
                    options = options with { NoRetry = true };
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

                case "--model":
                    options = options with { Model = value };
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
