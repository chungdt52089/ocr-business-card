using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Models;
using PartnerCard.Web.Observability;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Observability;

/// <summary>
/// Span của H-02 — chuỗi mắt xích đủ, và **không thuộc tính nào mang nội dung thẻ** (luật cứng 9).
/// Chạy offline: fake extractor và <see cref="StubHttpHandler"/>, không exporter.
///
/// Cô lập khi các ca chạy song song: mỗi ca tự mở một <see cref="Activity"/> cha (kiểu Internal, nên
/// <see cref="Telemetry.StartPipelineSpan"/> giữ nó làm cha) và chỉ giữ span cùng <c>TraceId</c>.
/// </summary>
[Trait("Category", "Observability")]
public sealed class TelemetryTests
{
    private static string CardsDirectory => Path.Combine(AppContext.BaseDirectory, "TestData", "cards");

    /// <summary>Nghe source <c>PartnerCard</c>, thu mọi span đã đóng thuộc trace của ca này.</summary>
    private sealed class SpanRecorder : IDisposable
    {
        private readonly ConcurrentQueue<Activity> _stopped = new();
        private readonly ActivityListener _listener;
        private readonly Activity _root;

        public SpanRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == Telemetry.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);

            _root = new Activity("test-root").SetIdFormat(ActivityIdFormat.W3C).Start();
        }

        public IReadOnlyList<Activity> Spans =>
            _stopped.Where(span => span.TraceId == _root.TraceId).ToList();

        public Activity Single(string name) => Spans.Should().ContainSingle(span => span.OperationName == name).Subject;

        public IEnumerable<string> ChildrenOf(Activity parent) =>
            Spans.Where(span => span.ParentSpanId == parent.SpanId).Select(span => span.OperationName);

        public void Dispose()
        {
            _root.Stop();
            _listener.Dispose();
        }
    }

    /// <summary>Mọi chuỗi nội dung của tấm thẻ — thứ không bao giờ được xuất hiện trong thuộc tính span.</summary>
    private static IEnumerable<string> ContentOf(CardExtractionResult card) =>
        new[] { card.FullName, card.JobTitle, card.Company, card.Website, card.Address, card.SearchAlias }
            .Concat(card.Phones)
            .Concat(card.Emails)
            .Where(value => value.Length >= 3);

    private static void ShouldNotCarryCardContent(IEnumerable<Activity> spans, CardExtractionResult card)
    {
        var content = ContentOf(card).ToList();
        content.Should().NotBeEmpty("ca này phải chạy trên một thẻ có dữ liệu thì khẳng định bên dưới mới có nghĩa");

        foreach (var span in spans)
        {
            foreach (var (key, value) in span.TagObjects)
            {
                var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

                content.Should().NotContain(
                    item => text.Contains(item, StringComparison.OrdinalIgnoreCase),
                    $"thuộc tính {key} trên span {span.OperationName} không được mang nội dung thẻ (luật cứng 9)");
            }
        }
    }

    private static CardPipeline CreatePipeline(IPartnerStore store) =>
        new(
            new FakeExtractor(ExpectedCards.LoadFrom(Path.Combine(CardsDirectory, "expected.json"))),
            new SchemaGuard(),
            store,
            new RecordingAuditLogger(),
            Options.Create(new PartnerCardOptions()),
            new SteppingClock());

    [Fact]
    public async Task O01_extract_sinh_du_chuoi_span_con_va_khong_mang_noi_dung_the()
    {
        using var data = new TempDataDirectory();
        using var store = JsonPartnerStore.LoadFrom(data.Path, new SteppingClock());
        using var recorder = new SpanRecorder();

        var image = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(CardsDirectory, "en-01.png")));
        var outcome = await CreatePipeline(store).ExtractAsync(
            image, "image/png", null, "en-01.png", CancellationToken.None);

        outcome.Ok.Should().BeTrue();

        var root = recorder.Single("pipeline.extract");
        recorder.ChildrenOf(root).Should().Equal("validate", "guard", "normalize", "confidence");

        root.GetTagItem("partnercard.outcome").Should().Be("extracted");
        root.GetTagItem("partnercard.is_business_card").Should().Be(true);
        root.GetTagItem("partnercard.guard.warnings").Should().BeOfType<int>();
        root.GetTagItem("partnercard.fields_below_threshold").Should().BeOfType<int>();

        ShouldNotCarryCardContent(recorder.Spans, outcome.Card!);
    }

    [Fact]
    public async Task O02_save_co_span_persist_va_khong_mang_noi_dung_the()
    {
        using var data = new TempDataDirectory();
        using var store = JsonPartnerStore.LoadFrom(data.Path, new SteppingClock());
        using var recorder = new SpanRecorder();

        var card = CardBuilder.Valid();
        var outcome = await CreatePipeline(store).SaveAsync(
            new PartnerDraft(null, card, string.Empty, string.Empty, ["jobTitle"]), "s1", CancellationToken.None);

        outcome.Ok.Should().BeTrue();

        var root = recorder.Single("pipeline.save");
        recorder.ChildrenOf(root).Should().Equal("normalize", "confidence", "guard", "persist");
        root.GetTagItem("partnercard.outcome").Should().Be("saved");

        ShouldNotCarryCardContent(recorder.Spans, card);
    }

    [Fact]
    public async Task O04_metric_do_tre_canh_bao_guard_va_so_truong_nguoi_sua()
    {
        using var data = new TempDataDirectory();
        using var store = JsonPartnerStore.LoadFrom(data.Path, new SteppingClock());
        using var recorder = new SpanRecorder();

        // Metric là toàn cục, ca khác chạy song song cũng ghi. Callback của MeterListener chạy đồng bộ
        // ngay trong span đường ống, nên lọc theo trace của ca này là đủ cô lập.
        var traceId = Activity.Current!.TraceId;
        var measurements = new ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)>();

        using var meters = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == Telemetry.Name)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };

        void OnMeasurement(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (Activity.Current?.TraceId == traceId)
            {
                measurements.Enqueue((instrument.Name, value, new Dictionary<string, object?>(tags.ToArray())));
            }
        }

        meters.SetMeasurementEventCallback<double>((i, v, t, _) => OnMeasurement(i, v, t));
        meters.SetMeasurementEventCallback<long>((i, v, t, _) => OnMeasurement(i, v, t));
        meters.Start();

        var pipeline = CreatePipeline(store);
        var image = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(CardsDirectory, "en-01.png")));
        var extracted = await pipeline.ExtractAsync(image, "image/png", null, "en-01.png", CancellationToken.None);
        await pipeline.SaveAsync(
            new PartnerDraft(null, extracted.Card!, string.Empty, string.Empty, ["jobTitle", "phones"]),
            "s1", CancellationToken.None);

        var duration = measurements.Should().ContainSingle(m => m.Name == "partnercard.extract.duration").Subject;
        duration.Tags["gen_ai.request.model"].Should().Be(ExtractorNames.Fake);
        duration.Tags["partnercard.outcome"].Should().Be("extracted");

        measurements.Where(m => m.Name == "partnercard.guard.warnings")
            .Select(m => m.Tags["partnercard.branch"]).Should().BeEquivalentTo(["extract", "save"]);

        measurements.Should().ContainSingle(m => m.Name == "partnercard.human.edits")
            .Which.Value.Should().Be(2);
    }

    [Fact]
    public async Task O03_span_gemini_theo_quy_uoc_GenAI_co_token_va_so_lan_thu_lai()
    {
        var card = CardBuilder.Valid();
        var text = JsonValue.Create(CardJson.Serialize(card))!.ToJsonString();
        var envelope = $$"""
            {
              "candidates": [ { "content": { "parts": [ { "text": {{text}} } ] }, "finishReason": "STOP" } ],
              "usageMetadata": { "promptTokenCount": 1102, "candidatesTokenCount": 168 }
            }
            """;

        // 503 rồi 200: đúng một lần thử lại.
        var responses = new Queue<HttpResponseMessage>(
        [
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"error":{"message":"overloaded"}}"""),
            },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope) },
        ]);
        var handler = new StubHttpHandler((_, _) => Task.FromResult(responses.Dequeue()));

        var extractor = new GeminiExtractor(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new SecretOptions { GeminiApiKey = "khoa-gia-cho-test-khong-phai-khoa-that" },
            Options.Create(new PartnerCardOptions { Extractor = ExtractorNames.Gemini, Model = "gemini-3.8-flash" }),
            TimeSpan.Zero);

        using var recorder = new SpanRecorder();

        await extractor.ExtractRawAsync(new byte[] { 1, 2, 3 }, "image/jpeg", null, null, CancellationToken.None);

        var span = recorder.Single("gen_ai.generate_content");
        span.Kind.Should().Be(ActivityKind.Client);
        span.GetTagItem("gen_ai.operation.name").Should().Be("generate_content");
        span.GetTagItem("gen_ai.system").Should().Be("gemini");
        span.GetTagItem("gen_ai.request.model").Should().Be("gemini-3.8-flash");
        span.GetTagItem("gen_ai.usage.input_tokens").Should().Be(1102);
        span.GetTagItem("gen_ai.usage.output_tokens").Should().Be(168);
        span.GetTagItem("partnercard.prompt_version").Should().Be(Prompts.Version);
        span.GetTagItem("partnercard.retry_count").Should().Be(1);
        span.GetTagItem("http.response.status_code").Should().Be(200);
        span.Status.Should().Be(ActivityStatusCode.Unset);

        ShouldNotCarryCardContent([span], card);
    }
}
