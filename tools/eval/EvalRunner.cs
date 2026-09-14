using System.Diagnostics;
using System.Text.Json;
using PartnerCard.Web.Extraction;

namespace PartnerCard.Eval;

/// <summary>
/// Vòng lặp đo: từng tấm ảnh một, mỗi tấm một try/catch riêng.
///
/// **Một thẻ hỏng không được làm sập cả lượt đo.** Gemini trả <c>503</c> giữa chừng đã xảy ra hai
/// lần trong bốn lượt gọi thật hôm 10/09, nên đây là tình huống thường gặp chứ không phải giả
/// thuyết. Lỗi của một thẻ được ghi thành mã lỗi rồi đi tiếp.
///
/// Hai ngoại lệ, và cả hai đều vì đi tiếp là chắc chắn vô ích:
/// <list type="bullet">
/// <item><c>quota_exhausted</c> — hạn mức kéo dài tới khi reset (SPEC mục 4.6), nên 18 lời gọi sau
/// chắc chắn hỏng y hệt và mỗi lời gọi lại tiêu thêm một request của hạn mức đã cạn.</item>
/// <item><c>extractor_auth</c> — khoá không dùng được thì không tấm nào dùng được.</item>
/// </list>
/// Các thẻ chưa chạy được đánh dấu <c>skipped_*</c> và **không** tính là "hỏng": chúng chưa từng
/// được gọi, gộp chung sẽ nói dối về việc mô hình đã đọc hỏng bao nhiêu tấm.
/// </summary>
/// <param name="rateLimitWait">
/// Chờ bao lâu trước khi gọi lại thẻ dính trần phút. Mặc định 60 giây: cửa sổ hạn mức là một phút
/// trượt nên chờ đủ 60 giây là chắc chắn qua, còn chờ ngắn hơn thì ăn 429 lần nữa và tiêu thêm một
/// request — thời gian rẻ hơn hạn mức. Ca kiểm thử truyền <see cref="TimeSpan.Zero"/> để không phải
/// ngồi đợi, cùng lý do <c>GeminiExtractor</c> nhận <c>retryDelay</c>.
/// </param>
public sealed class EvalRunner(IExtractor extractor, ExpectedCards answers, TimeSpan? rateLimitWait = null)
{
    private readonly TimeSpan _rateLimitWait = rateLimitWait ?? TimeSpan.FromSeconds(60);

    /// <summary>
    /// <param name="retry">
    /// Bộ đo tự thử lại **đúng một tình huống**: trần phút. Nó hợp lệ vì chính bộ đo gây ra tình
    /// huống đó — nó bắn 19 lời gọi liên tiếp, còn người dùng thật thì chụp từng tấm cách nhau cả
    /// phút. Chờ hết cửa sổ rồi gọi lại là cách duy nhất đi hết được danh sách.
    ///
    /// **Mọi thứ khác thì không.** <c>5xx</c> đã được <c>GeminiExtractor</c> thử lại đúng một lần
    /// (<c>MaxAttempts = 2</c>); chồng thêm một vòng ở đây là âm thầm biến "một lần" thành "bốn
    /// lần" và tiêu hạn mức gấp đôi. Trần **ngày** thì không bao giờ — chờ bao lâu cũng vô ích.
    ///
    /// <c>--no-retry</c> tắt cả lớp này, để một lượt đo đo đúng thứ xảy ra ở lần gọi đầu tiên.
    /// </param>
    /// </summary>
    public async Task<IReadOnlyList<CardRun>> RunAsync(
        IReadOnlyList<string> imagePaths,
        TimeSpan delay,
        IProgress<CardRun>? progress,
        CancellationToken ct,
        bool retry = true)
    {
        var runs = new List<CardRun>(imagePaths.Count);
        string? abortCode = null;
        var called = 0;

        foreach (var path in imagePaths)
        {
            ct.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(path);
            var code = CardScorer.CardCodeOf(fileName);
            var expected = answers.Find(code);
            var run = new CardRun(code, fileName, CardScorer.GroupOf(code), CardScorer.RoleOf(code, expected));

            if (abortCode is not null)
            {
                run = run with { ErrorCode = abortCode, Skipped = true };
                runs.Add(run);
                progress?.Report(run);
                continue;
            }

            // Nghỉ giữa các lượt gọi, không nghỉ trước lượt đầu và không nghỉ sau lượt cuối:
            // hạn mức có trục RPM, nhưng một lượt kiểm offline thì không có gì để tôn trọng.
            if (called > 0 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }

            called++;
            run = await CallAsync(run, path, ct);

            // Trần PHÚT: chờ hết cửa sổ rồi gọi lại đúng một lần. Đây là lớp thử lại duy nhất bộ
            // đo tự thêm, và nó hợp lệ vì chính bộ đo là thứ gây ra tình huống — nó bắn 19 lời gọi
            // liên tiếp. 5xx thì GeminiExtractor đã tự thử lại một lần, chồng thêm ở đây là âm
            // thầm biến "một lần" thành "bốn lần".
            if (run.ErrorCode == "rate_limited" && retry)
            {
                await Task.Delay(_rateLimitWait, ct);

                called++;
                run = (await CallAsync(run with { ErrorCode = null, ErrorDetail = null }, path, ct))
                    with { Retried = true };
            }

            abortCode = run.ErrorCode switch
            {
                "quota_exhausted" => "skipped_quota",
                "extractor_auth" => "skipped_auth",
                _ => null,
            };

            runs.Add(run);
            progress?.Report(run);
        }

        return runs;
    }

    private async Task<CardRun> CallAsync(CardRun run, string path, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();

        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);

            // ExtractRawAsync chứ không phải ExtractAsync: ExtractAsync trả CardExtractionResult,
            // record đó không mang ExtractionUsage, nên tokensIn/tokensOut không có chỗ nào đi ra —
            // mà SPEC mục 14 đòi in tổng token. Vẫn đúng một lời gọi HTTP, vẫn không qua guard.
            var raw = await extractor.ExtractRawAsync(bytes, MimeOf(path), null, run.FileName, ct);

            started.Stop();

            var card = CardJson.Deserialize(raw.Json);

            if (card is null)
            {
                return run with { ErrorCode = "bad_json", LatencyMs = (int)started.ElapsedMilliseconds };
            }

            // Bản cài offline không đo độ trễ nên trả 0; lúc đó lấy đồng hồ của bộ đo để hai chế
            // độ vẫn in ra được cùng một loại số.
            var latency = raw.Usage.LatencyMs > 0 ? raw.Usage.LatencyMs : (int)started.ElapsedMilliseconds;

            return Grade(run, card) with
            {
                LatencyMs = latency,
                TokensIn = raw.Usage.TokensIn,
                TokensOut = raw.Usage.TokensOut,
            };
        }
        catch (OperationCanceledException)
        {
            // Người dùng bấm Ctrl-C thì phải thoát được, không nuốt thành một mã lỗi.
            throw;
        }
        catch (ExtractorException ex)
        {
            return run with
            {
                ErrorCode = ex.Code,
                // Lý do thật nằm ở InnerException; Message của kiểu ngoài cùng là câu trung tính
                // viết cho người dùng cuối, không nói được vấp vào trần nào (SPEC mục 4.1).
                ErrorDetail = ex.InnerException?.Message,
                LatencyMs = (int)started.ElapsedMilliseconds,
            };
        }
        catch (JsonException)
        {
            // Mô hình trả về thứ không phải JSON. Khác hẳn extract_failed: ở đây đã có phản hồi,
            // chỉ là phản hồi không dùng được — và đó là tín hiệu để sửa prompt, không phải để đợi mạng.
            return run with { ErrorCode = "bad_json", LatencyMs = (int)started.ElapsedMilliseconds };
        }
        catch (Exception)
        {
            return run with { ErrorCode = "extract_failed", LatencyMs = (int)started.ElapsedMilliseconds };
        }
    }

    private CardRun Grade(CardRun run, Web.Models.CardExtractionResult card)
    {
        var expected = answers.Find(run.CardCode);

        return run.Role switch
        {
            CardRole.Core => run with { Card = card, Fields = CardScorer.Score(expected!, card) },

            // Chấm theo một tiêu chí khác hẳn, nhưng vẫn ghi lại bảng trường để biết nó đọc đúng
            // phần nhìn thấy được hay không. Bảng đó không bao giờ cộng vào 72 điểm.
            CardRole.AntiFabrication => run with
            {
                Card = card,
                Fields = CardScorer.Score(expected!, card),
                SpecialPass = CardScorer.AntiFabricationPasses(card),
            },

            CardRole.Negative => run with { Card = card, SpecialPass = CardScorer.NegativePasses(card) },

            // Có ảnh nhưng expected.json không có đáp án. Lời gọi vẫn tính là gọi được — nó đã tiêu
            // một request — nhưng không có gì để chấm. Hôm nay không thẻ nào rơi vào đây; luật này
            // để lần sau thêm ảnh mà quên đáp án thì lộ ra thay vì âm thầm tụt số.
            _ => run with { Card = card },
        };
    }

    private static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".heif" => "image/heif",
        _ => "image/jpeg",
    };
}
