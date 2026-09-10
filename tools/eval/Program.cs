using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using PartnerCard.Eval;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // Đầu ra bị chuyển hướng đi chỗ khác thì không đặt được encoding. Không phải lý do để dừng.
}

var parsed = EvalOptions.Parse(args);

if (parsed.Error is not null)
{
    Console.Error.WriteLine(parsed.Error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(EvalOptions.Usage);
    return 1;
}

var cli = parsed.Options!;

if (cli.Help)
{
    Console.WriteLine(EvalOptions.Usage);
    return 0;
}

PartnerCardOptions options;
SecretOptions secrets;
IExtractor extractor;

try
{
    // Đọc chung cấu hình của ứng dụng, không dựng một bản riêng: bộ đo phải đo đúng thứ đang chạy
    // thật. appsettings.Development.json là chỗ chứa khoá và nó nằm trong .gitignore (SPEC mục 13).
    var configuration = new ConfigurationBuilder()
        .SetBasePath(RepoPaths.WebProjectDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    options = configuration.GetSection(PartnerCardOptions.SectionName).Get<PartnerCardOptions>()
        ?? new PartnerCardOptions();

    if (cli.Extractor is not null)
    {
        options.Extractor = cli.Extractor;
    }

    // Cùng phép kiểm khoá mà server dùng lúc khởi động, nên thiếu khoá thì hỏng ở đây chứ không
    // hỏng ở lời gọi thứ nhất sau khi đã đọc xong 19 file ảnh.
    secrets = SecretLoader.Load(configuration, options);
    extractor = ExtractorFactory.Create(options, secrets, RepoPaths.ExpectedJson);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Không chạy được bộ đo: {ex.Message}");
    return 1;
}

var isFake = !options.RequiresApiKey;

// Ảnh chụp thật là con số nghiệm thu. Rơi về bản render chỉ khi thư mục kia rỗng (SPEC mục 14).
var directory = cli.Dir ?? RepoPaths.RealCards;

if (cli.Dir is null && !HasImages(directory))
{
    directory = RepoPaths.RenderedCards;
}

// Cảnh báo bám vào **thư mục đang đo**, không bám vào việc có rơi về hay không: truyền tay
// --dir …/cards cũng là đo trên file render, và cũng phải mang biển cảnh báo đó.
var renderedFallback = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
    == Path.GetFullPath(RepoPaths.RenderedCards).TrimEnd(Path.DirectorySeparatorChar);

if (!HasImages(directory))
{
    Console.Error.WriteLine($"Không có ảnh nào trong {directory}.");
    return 1;
}

var images = ImagesIn(directory);

if (cli.Cards.Count > 0)
{
    var wanted = cli.Cards.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var found = images.Select(path => CardScorer.CardCodeOf(Path.GetFileName(path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var missing = cli.Cards.Where(code => !found.Contains(code)).ToList();

    if (missing.Count > 0)
    {
        Console.Error.WriteLine(
            $"--cards nêu mã không có ảnh trong {directory}: {string.Join(", ", missing)}");
        return 1;
    }

    images = images
        .Where(path => wanted.Contains(CardScorer.CardCodeOf(Path.GetFileName(path))))
        .ToList();
}

// Hạn mức có trục RPM nên chế độ gemini nghỉ 3 giây giữa các lượt. Chế độ fake không có gì để tôn
// trọng, và 19 × 3 giây là gần một phút chờ vô ích trong mỗi lượt kiểm offline.
var delay = cli.Delay ?? (isFake ? TimeSpan.Zero : TimeSpan.FromSeconds(3));

var answers = ExpectedCards.LoadFrom(RepoPaths.ExpectedJson);
var runner = new EvalRunner(extractor, answers);

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

var index = 0;
var progress = new Progress<CardRun>(run => Console.WriteLine(Line(++index, images.Count, run)));

Console.WriteLine(
    $"Đo {images.Count} ảnh trong {Display(directory)} · extractor={options.Extractor} · nghỉ {delay.TotalSeconds:0}s");
Console.WriteLine();

IReadOnlyList<CardRun> runs;

try
{
    runs = await runner.RunAsync(images, delay, progress, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Đã huỷ. Không ghi file kết quả.");
    return 1;
}

var context = new ReportContext(
    Model: isFake ? "fake (FakeExtractor — đọc expected.json)" : options.Model,
    PromptVersion: isFake ? "-" : Prompts.Version,
    // Đọc thẳng hằng, không chép lại chuỗi (SPEC mục 14).
    ThinkingLevel: isFake ? "-" : GeminiExtractor.ThinkingLevel,
    ImageDirectory: Display(directory) + (renderedFallback ? " (bản render!)" : " (ảnh chụp thật)"),
    IsFake: isFake,
    IsRenderedFallback: renderedFallback,
    Delay: delay,
    At: DateTimeOffset.Now);

var outPath = cli.OutPath is null
    ? Path.Combine(RepoPaths.Root, "EVAL.md")
    : Path.GetFullPath(cli.OutPath);

try
{
    MarkdownReport.Write(outPath, MarkdownReport.Build(context, runs), cli.Overwrite);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Không ghi được {outPath}: {ex.Message}");
    return 1;
}

Summarise(runs, outPath, isFake);
return 0;

static bool HasImages(string directory) =>
    Directory.Exists(directory) && ImagesIn(directory).Count > 0;

static List<string> ImagesIn(string directory) =>
[
    .. Directory.EnumerateFiles(directory)
        .Where(path => Path.GetExtension(path).ToLowerInvariant()
            is ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" or ".heif")
        .OrderBy(Path.GetFileName, StringComparer.Ordinal),
];

static string Display(string path) =>
    Path.GetRelativePath(RepoPaths.Root, path).Replace('\\', '/');

static string Line(int index, int total, CardRun run)
{
    var head = $"[{index,3}/{total}] {run.FileName,-22}";

    if (run.Skipped)
    {
        return $"{head} — chưa gọi ({run.ErrorCode})";
    }

    if (!run.Called)
    {
        return $"{head} ✗ {run.ErrorCode}";
    }

    var core = run.Role == CardRole.Core
        ? $"{run.Fields.Count(f => CardScorer.CoreFields.Contains(f.Field) && f.Matched)}/{CardScorer.CoreFields.Count}"
        : run.SpecialPass switch { true => "ĐẠT", false => "TRƯỢT", null => "-" };

    return $"{head} {run.LatencyMs,7} ms   {core}";
}

static void Summarise(IReadOnlyList<CardRun> runs, string outPath, bool isFake)
{
    var core = runs.Where(run => run.Role == CardRole.Core).ToList();
    var total = core.Count * CardScorer.CoreFields.Count;
    var hit = core.Sum(run => run.Fields.Count(f => CardScorer.CoreFields.Contains(f.Field) && f.Matched));
    var percent = total == 0 ? 0 : 100.0 * hit / total;

    Console.WriteLine();
    Console.WriteLine(
        $"{hit}/{total} trường đúng ({percent.ToString("F1", CultureInfo.InvariantCulture).Replace('.', ',')}%)");
    Console.WriteLine($"Đã ghi {outPath}");

    if (isFake && hit != total)
    {
        Console.WriteLine();
        Console.WriteLine(
            "⚠ Chế độ fake mà không đạt 100%: FakeExtractor đọc chính expected.json, nên chênh lệch này");
        Console.WriteLine(
            "  là LỖI CỦA BỘ ĐO, không phải của mô hình. Sửa xong hãy gọi Gemini thật.");
    }
}
