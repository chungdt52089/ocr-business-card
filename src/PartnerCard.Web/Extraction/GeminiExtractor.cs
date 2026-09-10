using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PartnerCard.Web.Configuration;
using PartnerCard.Web.Models;

namespace PartnerCard.Web.Extraction;

/// <summary>
/// Bản cài thật: gọi <c>generateContent</c> của Gemini — SPEC mục 4.2, quyết định D-6.
///
/// **Đây là class duy nhất trong hệ thống nói chuyện với Gemini.** Lý do ở BRD mục 4: nếu phải
/// ngừng gửi ảnh ra nước ngoài thì việc thay thế phải gói gọn trong một file. Không được để
/// <c>HttpClient</c> gọi Gemini xuất hiện ở bất kỳ chỗ nào khác.
///
/// Ba điều dễ làm sai, đã khoá bằng ca kiểm thử:
/// <list type="number">
/// <item><see cref="ExtractAsync"/> cài bằng cách gọi <see cref="ExtractRawAsync"/> rồi
/// deserialize — **một lời gọi HTTP duy nhất**. Hai lời gọi là tiêu gấp đôi hạn mức mỗi tấm thẻ.</item>
/// <item>Chỉ trả <c>candidates[0].content.parts[0].text</c>, **không** trả cả phong bì: trả cả
/// phong bì thì SG-1 nổ vì <c>candidates</c> và <c>usageMetadata</c> là khoá lạ.</item>
/// <item>Timeout của chính nó phải thành <see cref="ExtractorTimeoutException"/>, còn việc người
/// gọi huỷ thì để nguyên — xem <see cref="SendAsync"/>.</item>
/// </list>
/// </summary>
/// <param name="retryDelay">
/// Chờ bao lâu trước lần thử lại duy nhất cho <c>5xx</c>. Mặc định 2 giây; ca kiểm thử truyền
/// <see cref="TimeSpan.Zero"/> để không phải ngồi đợi.
/// </param>
public sealed class GeminiExtractor(
    HttpClient http,
    SecretOptions secrets,
    IOptions<PartnerCardOptions> options,
    TimeSpan? retryDelay = null) : IExtractor
{
    private const string EndpointFormat =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    /// <summary>Khoá đi trong header, **không bao giờ trong URL** — URL bị ghi vào log ở mọi tầng.</summary>
    private const string ApiKeyHeader = "x-goog-api-key";

    /// <summary>Dấu hiệu Google dùng cho khoá không dùng được (SPEC mục 4.6).</summary>
    private const string InvalidKeyMarker = "API_KEY_INVALID";

    /// <summary>
    /// Mức suy luận. <c>gemini-3.8-flash</c> mặc định <c>medium</c> và **không cho tắt hẳn**
    /// (dòng Flash không hỗ trợ <c>minimal</c> lẫn thinking-off), nên <c>low</c> là cửa thấp nhất có.
    ///
    /// Chọn <c>low</c> vì số đo: cùng một tấm thẻ, cùng prompt, ba lượt ở mức <c>medium</c> cho
    /// 20,4s hỏng · 23s xanh · 108,7s hỏng. Độ trễ dao động năm lần như vậy không dùng được cho
    /// một buổi demo trực tiếp.
    ///
    /// **Đây là <c>generationConfig</c>, không phải prompt, nên <c>promptVersion</c> không ghi
    /// lại được nó.** Bộ đo phải in nó thành một dòng riêng trong <c>EVAL.md</c> cạnh
    /// <c>promptVersion</c> (SPEC mục 14), nếu không thì hai lượt đo lệch nhau mà không có gì
    /// nói tại sao. Vì vậy hằng này <c>public</c>: T-08 đọc thẳng, không chép lại chuỗi.
    /// </summary>
    public const string ThinkingLevel = "low";

    /// <summary>
    /// **Token suy luận dùng chung ngân sách này với token đầu ra.** Đó chính là cách sinh ra
    /// cảnh "<c>finishReason: MAX_TOKENS</c> mà <c>parts</c> rỗng": mô hình nghĩ hết ngân sách
    /// rồi không còn chỗ viết câu trả lời. JSON của một tấm thẻ chưa tới 1.000 token, nên để
    /// rộng hẳn cho phần suy luận là cách rẻ nhất để loại bỏ hẳn kiểu hỏng đó.
    /// </summary>
    private const int MaxOutputTokens = 16384;

    /// <summary>
    /// Số lần gửi tối đa cho **một** tấm thẻ: một lần đầu, cộng đúng **một** lần thử lại, và
    /// **chỉ cho <c>5xx</c>**.
    ///
    /// <c>429</c> thì tuyệt đối không — xem <see cref="ExtractorUnavailableException"/> cho lý do
    /// hai mã này đi ngược nhau. Ca X-16 khẳng định <c>429</c> vẫn đúng một lời gọi.
    /// </summary>
    private const int MaxAttempts = 2;

    /// <summary>Cắt bớt thông điệp lỗi của API trước khi đưa vào exception, kẻo log phình.</summary>
    private const int MaxErrorMessageLength = 500;

    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _retryDelay = retryDelay ?? DefaultRetryDelay;

    private PartnerCardOptions Options => options.Value;

    public async Task<RawExtraction> ExtractRawAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        string? languageHint,
        string? sourceName,
        CancellationToken ct)
    {
        // sourceName cố ý không dùng: mô hình không cần biết tên file (SPEC mục 4.1).
        var started = Stopwatch.GetTimestamp();
        var body = await SendAsync(BuildRequestBody(imageBytes, mimeType, languageHint), ct);

        return ReadResult(body, Elapsed(started));
    }

    /// <summary>
    /// Deserialize kết quả của <see cref="ExtractRawAsync"/> — **không** gọi HTTP lần thứ hai
    /// (SPEC mục 4.1). Không đi qua guard, nên chỉ dùng cho lời gọi lẻ và cho bộ đo T-08.
    /// </summary>
    public async Task<CardExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        string? languageHint,
        string? sourceName,
        CancellationToken ct)
    {
        var raw = await ExtractRawAsync(imageBytes, mimeType, languageHint, sourceName, ct);

        return CardJson.Deserialize(raw.Json)
            ?? throw new InvalidOperationException("Không dựng được kết quả từ JSON mô hình trả về.");
    }

    /// <summary>
    /// Gửi request và trả thân phản hồi.
    ///
    /// **Timeout do <see cref="CancellationTokenSource"/> ở đây quyết định**, không phải
    /// <c>HttpClient.Timeout</c> — vì ta cần phân biệt được ai đã huỷ. Bộ lọc
    /// <c>when (!ct.IsCancellationRequested)</c> là chỗ làm việc đó: token của người gọi chưa
    /// huỷ mà lời gọi bị huỷ thì chỉ có thể là hạn giờ của chính ta.
    ///
    /// Nếu để <c>TaskCanceledException</c> thoát nguyên dạng thì <c>CardPipeline</c> sẽ rethrow
    /// nó ở mắt <c>catch (OperationCanceledException)</c> và exception bay ra khỏi đường ống,
    /// trái SPEC mục 10.3 — đúng cái bẫy SPEC mục 4.1 nêu.
    /// </summary>
    private async Task<string> SendAsync(string requestBody, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Options.ExtractTimeoutSeconds));

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var (status, body) = await SendOnceAsync(requestBody, timeout.Token);

                if (IsSuccess(status))
                {
                    return body;
                }

                // Ném ngay với mọi mã KHÔNG đáng thử lại — 429 nằm trong số đó.
                ThrowIfNotRetryable(status, body);

                // Tới đây chỉ còn 5xx.
                if (attempt >= MaxAttempts)
                {
                    throw new ExtractorUnavailableException(Detail(status, body));
                }

                // Quá tải là chuyện tạm thời, nên đợi một nhịp rồi thử đúng một lần nữa.
                // Hạn giờ chung vẫn bao trọn cả hai lần gửi lẫn nhịp đợi này.
                await Task.Delay(_retryDelay, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExtractorTimeoutException();
        }
    }

    /// <summary>Một lần gửi. <c>HttpRequestMessage</c> không dùng lại được nên dựng mới mỗi lần.</summary>
    private async Task<(HttpStatusCode Status, string Body)> SendOnceAsync(
        string requestBody, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, string.Format(EndpointFormat, Options.Model))
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(ApiKeyHeader, secrets.GeminiApiKey);

        using var response = await http.SendAsync(request, ct);

        return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// Ba tình huống thành ba kiểu riêng (SPEC mục 4.6). Mọi mã lỗi khác thành exception thường,
    /// và <c>CardPipeline</c> biến nó thành <c>extract_failed</c>.
    ///
    /// **Thông báo không bao giờ mang thân phản hồi của Google** — nó đi thẳng ra màn hình người
    /// dùng, và ta không biết trước trong đó có gì.
    /// </summary>
    private static void ThrowIfNotRetryable(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            // Không bao giờ thử lại. Hết hạn mức là chuyện của cả ngày, không phải của giây này —
            // thử lại chỉ tiêu thêm quota mà kết quả vẫn thế.
            throw new ExtractorQuotaException(Detail(status, body));
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || (status == HttpStatusCode.BadRequest
                && body.Contains(InvalidKeyMarker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ExtractorAuthException(Detail(status, body));
        }

        if (IsServerError(status))
        {
            // Đáng thử lại — người gọi quyết định, vì chỉ nó biết đây là lần thứ mấy.
            return;
        }

        throw Detail(status, body);
    }

    /// <summary>
    /// Lý do thật, để nhét làm <c>InnerException</c>.
    ///
    /// Mang **mã HTTP cùng <c>error.status</c> và <c>error.message</c> của API** — đó là thông
    /// điệp lỗi của Google, không phải nội dung tấm thẻ, nên đưa vào đây là an toàn. Chỉ bóc đúng
    /// hai trường ấy chứ không đổ cả thân phản hồi.
    ///
    /// Người dùng vẫn chỉ thấy câu trung tính: <c>CardPipeline</c> trả
    /// <see cref="Exception.Message"/> của **kiểu ngoài cùng**, không đụng tới inner.
    /// </summary>
    private static InvalidOperationException Detail(HttpStatusCode status, string body) =>
        new($"Gemini trả mã {(int)status} ({status}). {ApiError(body)}");

    private static string ApiError(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];

            if (error is null)
            {
                return "Thân phản hồi không có trường error.";
            }

            var message = error["message"]?.GetValue<string>() ?? "(không có)";

            return $"error.status={Field(error, "status")} error.message="
                 + message[..Math.Min(message.Length, MaxErrorMessageLength)];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return "Thân phản hồi không đọc được thành JSON.";
        }
    }

    /// <summary>
    /// Bóc lấy đúng phần văn bản mô hình sinh, cộng số đo từ <c>usageMetadata</c>.
    ///
    /// Thiếu phần văn bản là chuyện có thật, không phải lỗi lập trình: <c>finishReason</c> bằng
    /// <c>SAFETY</c> hay <c>MAX_TOKENS</c> thì <c>parts</c> rỗng. Ném ở đây để đường ống trả
    /// <c>extract_failed</c>, thay vì đi tiếp với chuỗi rỗng rồi chết ở chỗ khó lần hơn.
    /// </summary>
    private RawExtraction ReadResult(string body, int latencyMs)
    {
        var root = JsonNode.Parse(body) as JsonObject
            ?? throw new InvalidOperationException("Phản hồi của Gemini không phải một đối tượng JSON.");

        var text = root["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(Diagnose(root, text));
        }

        var usage = root["usageMetadata"];

        return new RawExtraction(text, new ExtractionUsage(
            TokensIn: usage?["promptTokenCount"]?.GetValue<int>() ?? 0,
            TokensOut: usage?["candidatesTokenCount"]?.GetValue<int>() ?? 0,
            LatencyMs: latencyMs,
            Model: Options.Model,
            PromptVersion: Prompts.Version));
    }

    /// <summary>
    /// Thân request theo SPEC mục 4.2. <c>response_schema</c> **sinh từ <see cref="CardSchema"/>**,
    /// không chép tay: hai bản danh sách khoá lệch nhau là guard chặn nhầm kết quả hợp lệ.
    /// </summary>
    private static string BuildRequestBody(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = PromptFor(languageHint) },
                        new JsonObject
                        {
                            ["inline_data"] = new JsonObject
                            {
                                ["mime_type"] = mimeType,
                                ["data"] = Convert.ToBase64String(imageBytes.Span),
                            },
                        },
                    },
                },
            },
            // Hai khoá dưới viết camelCase đúng như tài liệu Gemini đặt tên chúng; hai khoá
            // response_* giữ snake_case theo SPEC mục 4.2. REST API nhận cả hai lối.
            ["generationConfig"] = new JsonObject
            {
                ["response_mime_type"] = "application/json",
                ["response_schema"] = CardSchema.ResponseSchema(),
                ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = ThinkingLevel },
                ["maxOutputTokens"] = MaxOutputTokens,
            },
        };

        return body.ToJsonString();
    }

    /// <summary>
    /// Gợi ý ngôn ngữ chỉ thêm **một dòng** vào cuối prompt, không sửa chữ nào của
    /// <see cref="Prompts.ExtractCard"/> — nếu không thì <c>promptVersion</c> ghi vào hồ sơ
    /// không còn tả đúng chuỗi đã gửi.
    /// </summary>
    private static string PromptFor(string? languageHint) =>
        string.IsNullOrWhiteSpace(languageHint)
            ? Prompts.ExtractCard
            : $"{Prompts.ExtractCard}{Environment.NewLine}{Environment.NewLine}Gợi ý ngôn ngữ của thẻ: {languageHint}.";

    /// <summary>
    /// Vì sao phản hồi không có văn bản — **chỉ siêu dữ liệu của phong bì**, không một chữ nào
    /// của tấm thẻ (mục 12 cấm ghi nội dung danh thiếp, và ở đây thì cũng không có gì để ghi:
    /// <c>parts</c> rỗng mới sinh ra lời gọi này).
    ///
    /// Thông báo này **không tới người dùng**: <c>CardPipeline</c> thay nó bằng câu trung tính
    /// của <c>extract_failed</c> (SPEC mục 10.3). Nó chỉ hiện ra ở ca <c>Live</c> và trong log
    /// gỡ rối — và đó là chỗ duy nhất phân biệt được "mô hình nghĩ hết ngân sách" với "mạng lỗi".
    /// </summary>
    private static string Diagnose(JsonObject root, string? text)
    {
        var candidates = root["candidates"] as JsonArray;
        var parts = candidates?[0]?["content"]?["parts"] as JsonArray;
        var usage = root["usageMetadata"];

        return "Phản hồi của Gemini không có phần văn bản nào. "
            + $"finishReason={Field(candidates?[0], "finishReason")} "
            + $"candidates={candidates?.Count ?? 0} parts={parts?.Count ?? 0} "
            + $"textLength={text?.Length ?? 0} "
            + $"promptTokenCount={Field(usage, "promptTokenCount")} "
            + $"candidatesTokenCount={Field(usage, "candidatesTokenCount")} "
            + $"thoughtsTokenCount={Field(usage, "thoughtsTokenCount")} "
            + $"totalTokenCount={Field(usage, "totalTokenCount")}";
    }

    /// <summary><c>ToJsonString</c> chứ không <c>GetValue&lt;T&gt;</c>: nó không ném khi kiểu lệch.</summary>
    private static string Field(JsonNode? node, string name) =>
        node?[name]?.ToJsonString() ?? "(không có)";

    private static bool IsSuccess(HttpStatusCode status) =>
        (int)status is >= 200 and < 300;

    /// <summary>Cả họ 5xx, không chỉ 500/502/503/504 — mọi mã trong họ đều là phía máy chủ.</summary>
    private static bool IsServerError(HttpStatusCode status) =>
        (int)status is >= 500 and < 600;

    private static int Elapsed(long started) =>
        (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
