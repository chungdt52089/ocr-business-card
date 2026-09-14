using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Audit;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;
using PartnerCard.Web.Tools;

namespace PartnerCard.Tests.Tools;

/// <summary>
/// Hai lớp tool dựng trên **đường ống thật, kho thật và nhật ký JSONL thật** — tất cả nằm trong
/// thư mục tạm. Extractor mặc định là <see cref="FakeExtractor"/>, nên không có lời gọi mạng nào.
///
/// Dùng chung cho nhóm M- và nhóm A-: ca audit phải đi qua đúng tool, không gọi thẳng logger,
/// vì A-01 hỏi "một lời gọi **tool** ra mấy dòng".
/// </summary>
public sealed class ToolHarness : IDisposable
{
    /// <summary>Mã phiên trung tính — không chứa gì của tấm thẻ, để A-03 không báo oan.</summary>
    public const string SessionHeader = "harness-session";

    private readonly TempDataDirectory _data = new();
    private readonly TempDataDirectory _logs = new();
    private readonly LoggerFactory _loggers;

    public ToolHarness(
        IExtractor? extractor = null,
        IPartnerStore? store = null,
        IOptions<PartnerCardOptions>? options = null,
        SessionContext? session = null)
    {
        Logger = new RecordingLogger();
        _loggers = new LoggerFactory([Logger]);

        JsonStore = JsonPartnerStore.LoadFrom(_data.Path, new SteppingClock());
        Store = store ?? JsonStore;

        Audit = new JsonlAuditLogger(
            _logs.Path, TimeProvider.System, _loggers.CreateLogger<JsonlAuditLogger>());

        Session = session ?? SessionContext.From(McpRequest(SessionHeader), Logger);

        var pipeline = new CardPipeline(
            extractor ?? CreateFakeExtractor(),
            new SchemaGuard(),
            Store,
            Audit,
            options ?? Options.Create(new PartnerCardOptions()),
            new SteppingClock());

        Cards = new CardTools(pipeline, Session, _loggers.CreateLogger<CardTools>());
        Partners = new PartnerTools(Store, _loggers.CreateLogger<PartnerTools>());
    }

    public static string CardsDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "cards");

    /// <summary>Kênh chẩn đoán <c>ILogger</c> của mọi thành phần trong harness.</summary>
    public RecordingLogger Logger { get; }

    public JsonPartnerStore JsonStore { get; }

    /// <summary>Kho tool đang dùng — là <see cref="JsonStore"/>, trừ khi ca kiểm thử cắm bản khác.</summary>
    public IPartnerStore Store { get; }

    public JsonlAuditLogger Audit { get; }

    public SessionContext Session { get; }

    public CardTools Cards { get; }

    public PartnerTools Partners { get; }

    public string PartnersFile => _data.PartnersFile;

    public string LogsDirectory => _logs.Path;

    public static FakeExtractor CreateFakeExtractor() =>
        new(ExpectedCards.LoadFrom(Path.Combine(CardsDirectory, "expected.json")));

    public static string Base64Of(string cardFile) =>
        Convert.ToBase64String(File.ReadAllBytes(Path.Combine(CardsDirectory, cardFile)));

    public Task<ExtractCardToolResult> ExtractAsync(string cardFile) =>
        Cards.ExtractBusinessCardAsync(Base64Of(cardFile), "image/png", null, cardFile);

    /// <summary>Gọi <c>save_partner</c> với đúng các trường của một thẻ — như agent chép lại kết quả trích xuất.</summary>
    public Task<SavePartnerToolResult> SaveAsync(
        CardExtractionResult card, bool allowDuplicate = false, string? partnerId = null) =>
        Cards.SavePartnerAsync(
            card.FullName,
            card.JobTitle,
            card.Company,
            [.. card.Phones],
            [.. card.Emails],
            card.Website,
            card.Address,
            card.DetectedLanguage,
            card.SearchAlias,
            partnerId,
            new Dictionary<string, double>(card.FieldConfidence),
            [],
            allowDuplicate);

    public IReadOnlyList<string> LogFiles() => Directory.GetFiles(LogsDirectory, "audit-*.jsonl");

    /// <summary>Mọi dòng của mọi file log, **kể cả dòng trống** — A-01 đếm đúng số dòng thật.</summary>
    public IReadOnlyList<string> LogLines() =>
        LogFiles().SelectMany(file => File.ReadAllLines(file)).ToList();

    public void Dispose()
    {
        JsonStore.Dispose();
        _loggers.Dispose();
        _data.Dispose();
        _logs.Dispose();
    }

    private static HttpContext McpRequest(string sessionHeader)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Post;
        http.Request.Path = "/mcp";
        http.Request.Headers[SessionContext.HeaderName] = sessionHeader;

        return http;
    }
}
