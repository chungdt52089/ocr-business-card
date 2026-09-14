using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PartnerCard.Tests.Fakes;
using PartnerCard.Tests.Tools;
using PartnerCard.Web.Audit;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Audit;

/// <summary>
/// T-11 — nhật ký JSONL (SPEC mục 12): A-01…A-04, cộng ba ca độ bền không đánh mã.
///
/// Mọi ca gọi qua **tool thật trên đường ống thật**, rồi đọc **file thật** trên đĩa. Không ca nào soi
/// <c>AuditEntry</c> trong bộ nhớ — cái đó <c>CardPipelineTests</c> đã làm; ở đây hỏi thứ nằm trong file.
///
/// Không có ca ghi đồng thời: A-06/A-07 cắt vì khối lượng MVP chỉ vài chục dòng một buổi (SPEC mục 12),
/// chỗ của chúng là F-07.
/// </summary>
[Trait("Category", "Integration")]
public sealed class JsonlAuditLoggerTests
{
    /// <summary>Các khoá của ví dụ ở SPEC mục 12 — luôn có mặt, <c>null</c> thì ghi <c>null</c>.</summary>
    private static readonly string[] Spec12Keys =
    [
        "ts", "sessionId", "tool", "partnerId", "imageSha256", "isBusinessCard",
        "fieldsFilled", "avgConfidence", "warnings", "model", "promptVersion",
        "tokensIn", "tokensOut", "latencyMs", "level",
    ];

    // =====================================================================================
    // A-01 → A-04
    // =====================================================================================

    [Fact]
    public async Task A01_goi_mot_tool_sinh_dung_mot_dong_JSON()
    {
        using var harness = new ToolHarness();

        var extracted = await harness.ExtractAsync("en-01.png");

        extracted.Ok.Should().BeTrue();
        harness.LogFiles().Should().ContainSingle();

        // Đọc ngay khi tool trả về: ghi đồng bộ nên dòng đã nằm trong file, không chờ ai xả hàng đợi.
        var line = harness.LogLines().Should().ContainSingle().Subject;
        using (var document = JsonDocument.Parse(line))
        {
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        }

        var saved = await harness.SaveAsync(extracted.Card!);

        saved.Ok.Should().BeTrue();
        harness.LogLines().Should().HaveCount(2, "save_partner cũng đúng một dòng — tầng tool không ghi thêm");
    }

    [Fact]
    public async Task A02_dong_Info_co_du_truong_theo_SPEC_12()
    {
        using var harness = new ToolHarness();

        await harness.ExtractAsync("en-01.png");

        using var document = JsonDocument.Parse(harness.LogLines().Single());
        var line = document.RootElement;

        line.EnumerateObject().Select(property => property.Name).Should().Contain(Spec12Keys);
        line.GetProperty("level").GetString().Should().Be("Info");
        line.GetProperty("tool").GetString().Should().Be("extract_business_card");
        line.GetProperty("sessionId").GetString().Should().Be(ToolHarness.SessionHeader);
        line.GetProperty("model").GetString().Should().Be("fake");
        line.GetProperty("isBusinessCard").GetBoolean().Should().BeTrue();
        DateTimeOffset.TryParse(line.GetProperty("ts").GetString(), out _).Should().BeTrue();

        line.TryGetProperty("blockCode", out _).Should().BeFalse();
        line.TryGetProperty("errorCode", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A02_dong_GuardBlock_mang_blockCode_khong_mang_errorCode()
    {
        using var harness = new ToolHarness(new HostileExtractor());

        await harness.ExtractAsync("en-01.png");

        using var document = JsonDocument.Parse(harness.LogLines().Single());
        var line = document.RootElement;

        line.EnumerateObject().Select(property => property.Name).Should().Contain(Spec12Keys);
        line.GetProperty("level").GetString().Should().Be("GuardBlock");
        line.GetProperty("blockCode").GetString().Should().Be("SG-7");
        line.TryGetProperty("errorCode", out _).Should().BeFalse("một dòng không bao giờ mang cả hai mã");
    }

    [Fact]
    public async Task A02_dong_Error_mang_errorCode_khong_mang_blockCode()
    {
        using var harness = new ToolHarness(new ThrowingExtractor(new ExtractorQuotaException()));

        await harness.ExtractAsync("en-01.png");

        using var document = JsonDocument.Parse(harness.LogLines().Single());
        var line = document.RootElement;

        line.EnumerateObject().Select(property => property.Name).Should().Contain(Spec12Keys);
        line.GetProperty("level").GetString().Should().Be("Error");
        line.GetProperty("errorCode").GetString().Should().Be("quota_exhausted");
        line.TryGetProperty("blockCode", out _).Should().BeFalse("một dòng không bao giờ mang cả hai mã");
    }

    /// <summary>
    /// Cách thẳng thắn nhất (TEST-SPEC mục 8): lấy mọi giá trị trong <c>partners.json</c> của bộ test,
    /// tìm từng giá trị trong toàn bộ file log.
    ///
    /// Tìm **hai lớp** — văn bản thô, và chuỗi đã giải mã sau khi parse từng dòng. Chỉ tìm thô thì một
    /// bản ghi escape <c>田中</c> thành <c>\u7530\u4E2D</c> sẽ xanh giả dù dữ liệu đã lọt.
    /// </summary>
    [Fact]
    public async Task A03_file_audit_khong_chua_ten_so_dien_thoai_email_dia_chi()
    {
        using var harness = new ToolHarness();

        foreach (var file in Directory.GetFiles(ToolHarness.CardsDirectory, "*.png").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);

            var extracted = await harness.ExtractAsync(name);
            extracted.Ok.Should().BeTrue(name);

            if (extracted.Card!.IsBusinessCard)
            {
                (await harness.SaveAsync(extracted.Card, allowDuplicate: true)).Ok.Should().BeTrue(name);
            }
        }

        var partners = JsonSerializer.Deserialize<List<Partner>>(
            File.ReadAllText(harness.PartnersFile), JsonStore.Options)!;
        partners.Should().HaveCount(18, "bộ mẫu có 18 thẻ dương tính");

        var lines = harness.LogLines();
        lines.Should().HaveCount(20 + partners.Count, "mỗi lời gọi extract và save chạy trọn là đúng một dòng");

        var raw = string.Join("\n", lines);
        var decoded = lines.SelectMany(StringValues).ToList();

        var cardContent = partners
            .SelectMany(partner => new[] { partner.FullName, partner.Address }
                .Concat(partner.Phones)
                .Concat(partner.Emails))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        cardContent.Should().NotBeEmpty();

        foreach (var value in cardContent)
        {
            raw.Should().NotContain(value);
            decoded.Should().NotContain(text => text.Contains(value, StringComparison.Ordinal), value);
        }
    }

    [Fact]
    public async Task A04_trich_xuat_thanh_cong_ghi_tokensIn_tokensOut_latencyMs()
    {
        var usage = new ExtractionUsage(1102, 168, 0, "gemini-test", "v-test");
        using var harness = new ToolHarness(new MeteredExtractor(
            ToolHarness.CreateFakeExtractor(), usage, TimeSpan.FromMilliseconds(30)));

        (await harness.ExtractAsync("en-01.png")).Ok.Should().BeTrue();

        using var document = JsonDocument.Parse(harness.LogLines().Single());
        var line = document.RootElement;

        line.GetProperty("tokensIn").GetInt32().Should().Be(1102);
        line.GetProperty("tokensOut").GetInt32().Should().Be(168);
        line.GetProperty("latencyMs").GetInt32().Should().BePositive();
        line.GetProperty("model").GetString().Should().Be("gemini-test");
    }

    // =====================================================================================
    // Độ bền — nhật ký hỏng KHÔNG được làm hỏng lời gọi tool (không đánh mã)
    // =====================================================================================

    [Fact]
    public void Thu_muc_log_chua_ton_tai_thi_tu_tao()
    {
        using var temp = new TempDataDirectory();
        var nested = Path.Combine(temp.Path, "a", "b", "logs");
        var audit = new JsonlAuditLogger(nested, TimeProvider.System, NullLogger<JsonlAuditLogger>.Instance);

        audit.Log(SampleEntry());

        Directory.GetFiles(nested, "audit-*.jsonl").Should().ContainSingle();
    }

    /// <summary>
    /// Ca chứng minh thiết kế nuốt exception. <c>audit.Log</c> nằm **trong** khối <c>try</c> của đường
    /// ống: để nó ném thì một lần trích xuất thành công thành <c>extract_failed</c>, còn một lần lưu đã
    /// vào kho thành <c>save_failed</c> — người dùng bấm lại và nhận cảnh báo trùng với chính hồ sơ vừa lưu.
    /// </summary>
    [Fact]
    public async Task File_audit_bi_khoa_thi_loi_goi_tool_van_thanh_cong()
    {
        using var harness = new ToolHarness();

        (await harness.ExtractAsync("en-01.png")).Ok.Should().BeTrue();
        var file = harness.LogFiles().Single();

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var extracted = await harness.ExtractAsync("en-01.png");
            extracted.Ok.Should().BeTrue("nhật ký hỏng không được làm hỏng lời gọi tool");
            extracted.Card.Should().NotBeNull();

            var saved = await harness.SaveAsync(extracted.Card!);
            saved.Ok.Should().BeTrue("hồ sơ đã vào kho thì không được báo là lưu hỏng");
            saved.PartnerId.Should().Be("PTN0001");

            harness.Logger.Entries.Should().Contain(line => line.Level == LogLevel.Error);
        }

        (await harness.ExtractAsync("en-01.png")).Ok.Should().BeTrue();

        harness.LogLines().Should().HaveCount(2, "hai dòng lúc bị khoá mất, nhưng logger không kẹt: lần sau ghi bình thường");
    }

    [Fact]
    public void Duong_dan_thu_muc_log_tro_vao_mot_file_thi_Log_khong_nem()
    {
        using var temp = new TempDataDirectory();
        var notADirectory = Path.Combine(temp.Path, "logs");
        File.WriteAllText(notADirectory, "không phải thư mục");

        var recording = new RecordingLogger();
        using var loggers = new LoggerFactory([recording]);
        var audit = new JsonlAuditLogger(notADirectory, TimeProvider.System, loggers.CreateLogger<JsonlAuditLogger>());

        var log = () => audit.Log(SampleEntry());

        log.Should().NotThrow();
        recording.Entries.Should().Contain(line => line.Level == LogLevel.Error);
    }

    private static AuditEntry SampleEntry() =>
        new(DateTimeOffset.UtcNow, "sample-session", "extract_business_card", AuditLevel.Info);

    private static IEnumerable<string> StringValues(string line)
    {
        using var document = JsonDocument.Parse(line);

        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .Select(property => property.Value.GetString()!)
            .ToList();
    }
}
