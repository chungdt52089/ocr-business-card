using System.Diagnostics;
using System.Net;
using System.Text;
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
public sealed class GeminiExtractor(
    HttpClient http,
    SecretOptions secrets,
    IOptions<PartnerCardOptions> options) : IExtractor
{
    private const string EndpointFormat =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    /// <summary>Khoá đi trong header, **không bao giờ trong URL** — URL bị ghi vào log ở mọi tầng.</summary>
    private const string ApiKeyHeader = "x-goog-api-key";

    /// <summary>Dấu hiệu Google dùng cho khoá không dùng được (SPEC mục 4.6).</summary>
    private const string InvalidKeyMarker = "API_KEY_INVALID";

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
        using var request = new HttpRequestMessage(
            HttpMethod.Post, string.Format(EndpointFormat, Options.Model))
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(ApiKeyHeader, secrets.GeminiApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Options.ExtractTimeoutSeconds));

        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            EnsureSuccess(response.StatusCode, body);

            return body;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExtractorTimeoutException();
        }
    }

    /// <summary>
    /// Ba tình huống thành ba kiểu riêng (SPEC mục 4.6). Mọi mã lỗi khác thành exception thường,
    /// và <c>CardPipeline</c> biến nó thành <c>extract_failed</c>.
    ///
    /// **Thông báo không bao giờ mang thân phản hồi của Google** — nó đi thẳng ra màn hình người
    /// dùng, và ta không biết trước trong đó có gì.
    /// </summary>
    private static void EnsureSuccess(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            // Không thử lại. Thử lại chỉ tiêu thêm hạn mức mà kết quả vẫn thế.
            throw new ExtractorQuotaException();
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || (status == HttpStatusCode.BadRequest
                && body.Contains(InvalidKeyMarker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ExtractorAuthException();
        }

        if (!IsSuccess(status))
        {
            throw new InvalidOperationException($"Gemini trả mã {(int)status}.");
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
            throw new InvalidOperationException(
                "Phản hồi của Gemini không có phần văn bản nào (kiểm finishReason).");
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
            ["generationConfig"] = new JsonObject
            {
                ["response_mime_type"] = "application/json",
                ["response_schema"] = CardSchema.ResponseSchema(),
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

    private static bool IsSuccess(HttpStatusCode status) =>
        (int)status is >= 200 and < 300;

    private static int Elapsed(long started) =>
        (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
