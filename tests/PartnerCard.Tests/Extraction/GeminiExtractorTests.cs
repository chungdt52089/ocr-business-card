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

    /// <summary>
    /// <paramref name="retryDelay"/> mặc định bằng 0 trong test: nhịp đợi 2 giây thật chỉ làm bộ
    /// test chậm đi mà không kiểm thêm được gì. Riêng một ca dưới đây truyền giá trị thật để
    /// khẳng định nhịp đợi có xảy ra.
    /// </summary>
    private static GeminiExtractor Create(
        StubHttpHandler handler, int timeoutSeconds = 20, TimeSpan? retryDelay = null) =>
        new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new SecretOptions { GeminiApiKey = DummyKey },
            Options.Create(new PartnerCardOptions
            {
                Extractor = ExtractorNames.Gemini,
                ExtractTimeoutSeconds = timeoutSeconds,

                // Đặt tường minh chứ không dựa vào giá trị mặc định: nhóm ca này kiểm đường dây
                // "model trong cấu hình đi vào Usage", không kiểm dự án đang ship model nào. Dựa
                // vào mặc định thì đổi model mặc định là ca đỏ trong khi chẳng có gì hỏng.
                Model = TestModel,
            }),
            retryDelay ?? TimeSpan.Zero);

    private const string TestModel = "gemini-3.8-flash";

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
    public async Task Loi_khong_thuoc_ho_nao_khong_bi_gan_nham_thanh_kieu_rieng()
    {
        // 404 không phải quota, không phải auth, cũng không phải 5xx đáng thử lại.
        var handler = StubHttpHandler.Returns(
            HttpStatusCode.NotFound,
            """{"error":{"code":404,"status":"NOT_FOUND","message":"models/abc is not found"}}""");

        var thrown = await Record.ExceptionAsync(async () => await ExtractRaw(Create(handler)));

        thrown.Should().NotBeNull().And.NotBeAssignableTo<ExtractorException>();
        handler.Calls.Should().Be(1, "chỉ 5xx mới được thử lại");
        thrown!.Message.Should().Contain("404")
            .And.Contain("NOT_FOUND")
            .And.Contain("models/abc is not found");
    }

    // =====================================================================================
    // 5xx — quá tải phía Google, xử lý NGƯỢC với 429
    // =====================================================================================

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Ca_ho_5xx_thu_lai_dung_MOT_lan_roi_nem_ExtractorUnavailableException(
        HttpStatusCode status)
    {
        var handler = StubHttpHandler.Returns(
            status, """{"error":{"status":"UNAVAILABLE","message":"The model is overloaded."}}""");

        var act = async () => await ExtractRaw(Create(handler));

        var thrown = await act.Should().ThrowAsync<ExtractorUnavailableException>();
        thrown.Which.Code.Should().Be("extract_unavailable");

        handler.Calls.Should().Be(2, "một lần đầu cộng đúng một lần thử lại");
    }

    [Fact]
    public async Task Het_han_muc_van_dung_MOT_loi_goi_du_5xx_duoc_thu_lai()
    {
        // Hai mã này cố ý đi ngược nhau: quá tải là tạm thời, hết hạn mức là chuyện cả ngày.
        var handler = StubHttpHandler.Returns(
            HttpStatusCode.TooManyRequests, """{"error":{"status":"RESOURCE_EXHAUSTED"}}""");

        await Record.ExceptionAsync(async () => await ExtractRaw(Create(handler)));

        handler.Calls.Should().Be(1, "thử lại 429 chỉ tiêu thêm quota mà kết quả vẫn thế");
    }

    [Fact]
    public async Task Lan_thu_lai_an_thi_tra_ve_ket_qua_binh_thuong()
    {
        var responses = new Queue<HttpResponseMessage>(
        [
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"error":{"message":"overloaded"}}"""),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Envelope(ModelCard())),
            },
        ]);

        var handler = new StubHttpHandler((_, _) => Task.FromResult(responses.Dequeue()));

        var raw = await ExtractRaw(Create(handler));

        JsonNode.Parse(raw.Json)!.AsObject().Select(pair => pair.Key)
            .Should().BeEquivalentTo(CardSchema.AllowedKeys);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Truoc_khi_thu_lai_co_doi_mot_nhip()
    {
        var handler = StubHttpHandler.Returns(
            HttpStatusCode.ServiceUnavailable, """{"error":{"message":"overloaded"}}""");

        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        await Record.ExceptionAsync(async () =>
            await ExtractRaw(Create(handler, retryDelay: TimeSpan.FromMilliseconds(300))));

        System.Diagnostics.Stopwatch.GetElapsedTime(started)
            .Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250),
                "thử lại tức thì thì lần hai gặp đúng cái máy chủ vẫn đang quá tải");
    }

    [Fact]
    public async Task Nguoi_dung_thay_cau_trung_tinh_con_ly_do_that_nam_o_inner()
    {
        var handler = StubHttpHandler.Returns(
            HttpStatusCode.ServiceUnavailable,
            """{"error":{"status":"UNAVAILABLE","message":"The model is overloaded. Try again later."}}""");

        var thrown = (ExtractorUnavailableException)(await Record.ExceptionAsync(
            async () => await ExtractRaw(Create(handler))))!;

        // Thứ đi ra màn hình: trung tính, và KHÔNG bảo người dùng chụp lại.
        thrown.Message.Should().Be("Dịch vụ đang quá tải. Thử lại sau vài giây.");
        thrown.Message.Should().NotContain("503").And.NotContain("overloaded");

        // Thứ đi vào log gỡ rối và ca Live: lý do thật.
        thrown.InnerException!.Message.Should().Contain("503")
            .And.Contain("UNAVAILABLE")
            .And.Contain("The model is overloaded.");
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
        raw.Usage.Model.Should().Be(TestModel);
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

    [Fact]
    public async Task generationConfig_ha_muc_suy_luan_va_mo_rong_ngan_sach_dau_ra()
    {
        var handler = StubHttpHandler.Ok(Envelope(ModelCard()));

        await ExtractRaw(Create(handler));

        var config = JsonNode.Parse(handler.LastBody!)!["generationConfig"]!;

        // gemini-3.8-flash mặc định medium và không cho tắt hẳn, nên low là cửa thấp nhất có.
        config["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>()
            .Should().Be(GeminiExtractor.ThinkingLevel).And.Be("low");

        // Token suy luận dùng chung ngân sách với token đầu ra — để chật là mời gọi cảnh
        // "finishReason MAX_TOKENS mà parts rỗng".
        config["maxOutputTokens"]!.GetValue<int>().Should().BeGreaterThanOrEqualTo(4096);
    }

    [Fact]
    public async Task Thieu_parts_thi_thong_bao_noi_ro_vi_sao_nhung_khong_lo_noi_dung_the()
    {
        // Đúng hình dạng đã làm hỏng lần chạy thật thứ ba: nghĩ hết ngân sách, không còn chỗ viết.
        var handler = StubHttpHandler.Ok("""
            {
              "candidates": [ { "content": { "role": "model" }, "finishReason": "MAX_TOKENS" } ],
              "usageMetadata": { "promptTokenCount": 1801, "thoughtsTokenCount": 8192,
                                 "totalTokenCount": 9993 }
            }
            """);

        var thrown = await Record.ExceptionAsync(async () => await ExtractRaw(Create(handler)));

        // Thông báo này KHÔNG tới người dùng — CardPipeline thay bằng câu trung tính của
        // extract_failed. Nó chỉ để ca Live và log gỡ rối phân biệt được nguyên nhân.
        thrown!.Message.Should().Contain("MAX_TOKENS")
            .And.Contain("thoughtsTokenCount=8192")
            .And.Contain("parts=0");

        // Không phải quota/auth/timeout — đường ống trả extract_failed.
        thrown.Should().NotBeAssignableTo<ExtractorException>();
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
