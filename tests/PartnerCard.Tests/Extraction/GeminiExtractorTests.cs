using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PartnerCard.Tests.Fakes;
using PartnerCard.Web.Audit;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Extraction;
using PartnerCard.Web.Processing;
using PartnerCard.Web.Storage;

namespace PartnerCard.Tests.Extraction;

/// <summary>
/// X-14, X-15a, X-15b, X-16, X-17 — TEST-SPEC mục 2.
///
/// Chạy trên <see cref="GeminiExtractor"/> thật với <see cref="StubHttpHandler"/> thay cho mạng.
/// **Không socket, không DNS**, nên ca I-09 vẫn xanh khi đã ngắt Wi-Fi.
/// </summary>
[Trait("Category", "Extract")]
public sealed class GeminiExtractorTests
{
    private const string DummyKey = "khoa-gia-cho-test-khong-phai-khoa-that";

    /// <summary>Phong bì <c>generateContent</c> trả về, bọc quanh JSON của tấm thẻ.</summary>
    private static string Envelope(string cardJson, int tokensIn = 1102, int tokensOut = 168)
    {
        var text = JsonValue.Create(cardJson)!.ToJsonString();

        return $$"""
            {
              "candidates": [
                { "content": { "parts": [ { "text": {{text}} } ], "role": "model" },
                  "finishReason": "STOP" }
              ],
              "usageMetadata": { "promptTokenCount": {{tokensIn}}, "candidatesTokenCount": {{tokensOut}} }
            }
            """;
    }

    private static string ModelCard(string? drop = null, bool addUnknownKey = false)
    {
        var card = JsonNode.Parse(CardJson.Serialize(CardBuilder.Valid()))!.AsObject();

        if (drop is not null)
        {
            card.Remove(drop);
        }

        if (addUnknownKey)
        {
            card["note"] = "mô hình tự thêm khoá này";
        }

        return card.ToJsonString();
    }

    private static GeminiExtractor Create(StubHttpHandler handler, int timeoutSeconds = 20) =>
        new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new SecretOptions { GeminiApiKey = DummyKey },
            Options.Create(new PartnerCardOptions
            {
                Extractor = ExtractorNames.Gemini,
                ExtractTimeoutSeconds = timeoutSeconds,
            }));

    private static Task<RawExtraction> ExtractRaw(GeminiExtractor extractor, CancellationToken ct = default) =>
        extractor.ExtractRawAsync(new byte[] { 1, 2, 3 }, "image/jpeg", null, "ja-01.jpg", ct);

    // =====================================================================================
    // X-16 · X-17 — lỗi phân biệt được
    // =====================================================================================

    [Fact]
    public async Task X16_het_han_muc_nem_ExtractorQuotaException_va_khong_thu_lai()
    {
        var handler = StubHttpHandler.Returns(
            HttpStatusCode.TooManyRequests,
            """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED","message":"Quota exceeded"}}""");

        var act = async () => await ExtractRaw(Create(handler));

        var thrown = await act.Should().ThrowAsync<ExtractorQuotaException>();
        thrown.Which.Code.Should().Be("quota_exhausted");
        handler.Calls.Should().Be(1, "thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế");
    }

    [Fact]
    public async Task X17_khoa_sai_nem_ExtractorAuthException()
    {
        var handler = StubHttpHandler.Returns(
            HttpStatusCode.BadRequest,
            """{"error":{"code":400,"status":"INVALID_ARGUMENT","message":"API_KEY_INVALID"}}""");

        var act = async () => await ExtractRaw(Create(handler));

        var thrown = await act.Should().ThrowAsync<ExtractorAuthException>();
        thrown.Which.Code.Should().Be("extractor_auth");
    }

    [Fact]
    public async Task Loi_khac_khong_bi_gan_nham_thanh_ba_kieu_rieng()
    {
        var handler = StubHttpHandler.Returns(
            HttpStatusCode.InternalServerError, """{"error":{"code":500}}""");

        var thrown = await Record.ExceptionAsync(async () => await ExtractRaw(Create(handler)));

        // 500 là lỗi lạ — đường ống biến nó thành extract_failed, không phải quota hay auth.
        thrown.Should().NotBeNull().And.NotBeAssignableTo<ExtractorException>();
        handler.Calls.Should().Be(1);
    }

    // =====================================================================================
    // X-14 — timeout, và ranh giới với việc người gọi huỷ
    // =====================================================================================

    [Fact]
    public async Task X14_qua_thoi_gian_cho_nem_ExtractorTimeoutException()
    {
        var act = async () => await ExtractRaw(Create(StubHttpHandler.Hangs(), timeoutSeconds: 1));

        var thrown = await act.Should().ThrowAsync<ExtractorTimeoutException>();
        thrown.Which.Code.Should().Be("extract_timeout");

        // Kế thừa OperationCanceledException là rơi vào mắt rethrow của CardPipeline
        // và bay thẳng ra khỏi đường ống (SPEC mục 4.1).
        thrown.Which.Should().NotBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task Nguoi_goi_huy_thi_khong_bi_doi_thanh_timeout()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var thrown = await Record.ExceptionAsync(
            async () => await ExtractRaw(Create(StubHttpHandler.Hangs()), cts.Token));

        thrown.Should().BeAssignableTo<OperationCanceledException>();
        thrown.Should().NotBeOfType<ExtractorTimeoutException>();
    }

    // =====================================================================================
    // Đường thuận
    // =====================================================================================

    [Fact]
    public async Task Chi_tra_phan_text_cua_mo_hinh_chu_khong_tra_ca_phong_bi()
    {
        var raw = await ExtractRaw(Create(StubHttpHandler.Ok(Envelope(ModelCard()))));

        // Trả cả phong bì thì SG-1 nổ vì "candidates" và "usageMetadata" là khoá lạ.
        JsonNode.Parse(raw.Json)!.AsObject().Select(pair => pair.Key)
            .Should().BeEquivalentTo(CardSchema.AllowedKeys);
    }

    [Fact]
    public async Task Doc_dung_token_va_ghi_dung_model_voi_promptVersion()
    {
        var raw = await ExtractRaw(Create(StubHttpHandler.Ok(Envelope(ModelCard()))));

        raw.Usage.TokensIn.Should().Be(1102);
        raw.Usage.TokensOut.Should().Be(168);
        raw.Usage.Model.Should().Be("gemini-3.8-flash");
        raw.Usage.PromptVersion.Should().Be(Prompts.Version);
        raw.Usage.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExtractAsync_chi_ton_dung_MOT_loi_goi_HTTP()
    {
        var handler = StubHttpHandler.Ok(Envelope(ModelCard()));

        var card = await Create(handler).ExtractAsync(
            new byte[] { 1, 2, 3 }, "image/jpeg", null, "ja-01.jpg", CancellationToken.None);

        card.FullName.Should().Be("Marcus Feld");
        handler.Calls.Should().Be(1, "cài thành hai lời gọi là tiêu gấp đôi hạn mức mỗi tấm thẻ");
    }

    [Fact]
    public async Task Than_request_dung_hinh_dang_SPEC_4_2()
    {
        var handler = StubHttpHandler.Ok(Envelope(ModelCard()));

        await ExtractRaw(Create(handler));

        var body = JsonNode.Parse(handler.LastBody!)!.AsObject();
        var parts = body["contents"]![0]!["parts"]!.AsArray();

        parts[0]!["text"]!.GetValue<string>().Should().Contain("KHÔNG DỊCH CHỨC DANH");
        parts[1]!["inline_data"]!["mime_type"]!.GetValue<string>().Should().Be("image/jpeg");
        parts[1]!["inline_data"]!["data"]!.GetValue<string>().Should().Be("AQID");

        var config = body["generationConfig"]!;
        config["response_mime_type"]!.GetValue<string>().Should().Be("application/json");

        // Schema sinh từ CardSchema, không chép tay — hai bản lệch nhau là guard chặn nhầm.
        config["response_schema"]!.ToJsonString()
            .Should().Be(CardSchema.ResponseSchema().ToJsonString());

        handler.LastApiKey.Should().Be(DummyKey, "khoá đi trong header x-goog-api-key, không trong URL");
    }

    // =====================================================================================
    // X-15a · X-15b — mô hình trả JSON không đúng schema, đi qua đủ đường ống
    // =====================================================================================

    [Fact]
    public async Task X15a_thieu_khoa_bat_buoc_thi_guard_chan_va_khong_nem()
    {
        using var pipeline = new GeminiPipeline(
            StubHttpHandler.Ok(Envelope(ModelCard(drop: "company"))));

        var outcome = await pipeline.ExtractAsync();

        outcome.Ok.Should().BeFalse();
        outcome.ErrorCode.Should().Be("guard_blocked");
        pipeline.Audit.Entries.Should().ContainSingle().Which.BlockCode.Should().Be("SG-2");
    }

    [Fact]
    public async Task X15b_khoa_la_thi_bo_khoa_va_di_tiep()
    {
        using var pipeline = new GeminiPipeline(
            StubHttpHandler.Ok(Envelope(ModelCard(addUnknownKey: true))));

        var outcome = await pipeline.ExtractAsync();

        outcome.Ok.Should().BeTrue("SG-1 bỏ khoá lạ rồi đi tiếp, không chặn");
        outcome.Card!.FullName.Should().Be("Marcus Feld");
        outcome.Warnings.Should().ContainSingle().Which.Code.Should().Be("SG-1");
    }

    [Fact]
    public async Task Het_han_muc_di_ra_toi_man_hinh_thanh_quota_exhausted()
    {
        using var pipeline = new GeminiPipeline(StubHttpHandler.Returns(
            HttpStatusCode.TooManyRequests, """{"error":{"status":"RESOURCE_EXHAUSTED"}}"""));

        var outcome = await pipeline.ExtractAsync();

        // Không được rơi vào "Thử chụp lại rõ hơn" — chụp lại chỉ tiêu thêm quota.
        outcome.ErrorCode.Should().Be("quota_exhausted");
        outcome.Message.Should().NotContain("chụp lại");
        pipeline.Audit.Entries.Should().ContainSingle().Which.Level.Should().Be(AuditLevel.Error);
    }

    /// <summary>Đường ống thật, cắm <see cref="GeminiExtractor"/> chạy trên handler giả.</summary>
    private sealed class GeminiPipeline : IDisposable
    {
        private readonly TempDataDirectory _data = new();
        private readonly JsonPartnerStore _store;
        private readonly CardPipeline _pipeline;

        public GeminiPipeline(StubHttpHandler handler)
        {
            _store = JsonPartnerStore.LoadFrom(_data.Path, new SteppingClock());
            Audit = new RecordingAuditLogger();

            _pipeline = new CardPipeline(
                Create(handler), new SchemaGuard(), _store, Audit,
                Options.Create(new PartnerCardOptions()), new SteppingClock());
        }

        public RecordingAuditLogger Audit { get; }

        public Task<ExtractOutcome> ExtractAsync() => _pipeline.ExtractAsync(
            Convert.ToBase64String([1, 2, 3]), "image/jpeg", null, "ja-01.jpg", CancellationToken.None);

        public void Dispose()
        {
            _store.Dispose();
            _data.Dispose();
        }
    }
}
