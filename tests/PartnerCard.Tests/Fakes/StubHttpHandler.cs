using System.Net;
using System.Text;

namespace PartnerCard.Tests.Fakes;

/// <summary>
/// Trả phản hồi HTTP dựng sẵn, **không mở socket, không tra DNS** — TEST-SPEC mục 2.
///
/// Đây là cách kiểm hành vi HTTP của <c>GeminiExtractor</c> (429, 400, treo tới timeout, JSON
/// sai schema) mà vẫn giữ được ca I-09: toàn bộ <c>dotnet test</c> xanh khi đã ngắt mạng.
/// </summary>
public sealed class StubHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    /// <summary>Ca 429 đòi đúng 1 — thử lại chỉ tiêu thêm hạn mức.</summary>
    public int Calls { get; private set; }

    /// <summary>Thân request đã gửi, để soi prompt và response_schema.</summary>
    public string? LastBody { get; private set; }

    /// <summary>Giá trị header <c>x-goog-api-key</c> đã gửi.</summary>
    public string? LastApiKey { get; private set; }

    public static StubHttpHandler Returns(HttpStatusCode status, string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));

    public static StubHttpHandler Ok(string body) => Returns(HttpStatusCode.OK, body);

    /// <summary>Treo cho tới khi bị huỷ — dùng cho ca X-14.</summary>
    public static StubHttpHandler Hangs() =>
        new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;

        if (request.Content is not null)
        {
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        LastApiKey = request.Headers.TryGetValues("x-goog-api-key", out var values)
            ? values.FirstOrDefault()
            : null;

        return await respond(request, cancellationToken);
    }
}
