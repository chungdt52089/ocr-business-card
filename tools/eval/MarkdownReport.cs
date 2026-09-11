using System.Globalization;
using System.Text;

namespace PartnerCard.Eval;

/// <summary>Ba thứ định danh một lượt chạy, cộng bối cảnh cần cảnh báo — SPEC mục 14.</summary>
public sealed record ReportContext(
    string Model,
    string PromptVersion,
    string ThinkingLevel,
    string ImageDirectory,
    bool IsFake,
    bool IsRenderedFallback,
    TimeSpan Delay,
    DateTimeOffset At);

/// <summary>
/// Dựng khối markdown cho một lượt đo và nối nó vào <c>EVAL.md</c>.
///
/// **Nối thêm chứ không ghi đè**, vì SPEC mục 14 và T-09 đòi "ghi lại số của cả hai lần": một thay
/// đổi prompt không kèm số đo trước–sau là một thay đổi không ai biết tốt hay xấu, và ghi đè là
/// xoá mất vế trước.
/// </summary>
public static class MarkdownReport
{
    private const string FileHeader = """
        # EVAL — Kết quả đo PartnerCard

        Sinh bằng `dotnet run --project tools/eval`. Mỗi lượt chạy là một khối `##` ở dưới, khối mới
        nối vào cuối file — để so được số trước và số sau mỗi lần sửa prompt (SPEC mục 14).

        Báo cáo bằng **số đếm thô kèm phần trăm**. 18 thẻ × 4 trường bắt buộc = 72 điểm dữ liệu.

        """;

    public static void Write(string path, string block, bool overwrite)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (overwrite || !File.Exists(path))
        {
            File.WriteAllText(path, FileHeader + "\n" + block, new UTF8Encoding(false));
            return;
        }

        File.AppendAllText(path, "\n" + block, new UTF8Encoding(false));
    }

    public static string Build(ReportContext context, IReadOnlyList<CardRun> runs)
    {
        var report = new StringBuilder();

        report.AppendLine(CultureInfo.InvariantCulture, $"## Lượt đo · {context.At:yyyy-MM-dd HH:mm zzz}");
        report.AppendLine();

        AppendBanners(report, context);
        AppendIdentity(report, context);
        AppendCallCounts(report, runs);
        AppendCoreScore(report, runs);
        AppendSecondaryScore(report, runs);
        AppendMismatches(report, runs);
        AppendSpecialRows(report, runs);
        AppendConfidence(report, runs, context.IsFake);
        AppendTiming(report, runs);

        return report.ToString();
    }

    private static void AppendBanners(StringBuilder report, ReportContext context)
    {
        if (context.IsFake)
        {
            report.AppendLine(
                "> ⚠ **Lượt này chạy ở chế độ `fake`, không dùng để nghiệm thu.** `FakeExtractor` đọc");
            report.AppendLine(
                "> chính `expected.json`, nên con số dưới đây chỉ chứng minh **bộ đo tự nó đúng** — bất cứ");
            report.AppendLine("> kết quả nào khác 100% là lỗi của bộ đo, không phải của mô hình.");
            report.AppendLine();
        }

        if (context.IsRenderedFallback)
        {
            report.AppendLine(
                "> ⚠ **Đo trên `cards/*.png` — file render, không phải ảnh chụp.** PNG không có bóng,");
            report.AppendLine(
                "> không nghiêng, không loá, nên con số đẹp giả tạo. **Không dùng để nghiệm thu** (SPEC mục 14).");
            report.AppendLine();
        }
    }

    private static void AppendIdentity(StringBuilder report, ReportContext context)
    {
        report.AppendLine("```");
        report.AppendLine(CultureInfo.InvariantCulture, $"model         : {context.Model}");
        report.AppendLine(CultureInfo.InvariantCulture, $"promptVersion : {context.PromptVersion}");
        report.AppendLine(CultureInfo.InvariantCulture, $"thinkingLevel : {context.ThinkingLevel}");
        report.AppendLine(CultureInfo.InvariantCulture, $"ảnh           : {context.ImageDirectory}");
        report.AppendLine(CultureInfo.InvariantCulture, $"nghỉ giữa lượt: {Num((int)context.Delay.TotalSeconds)}s");
        report.AppendLine("```");
        report.AppendLine();

        report.AppendLine(
            "`thinkingLevel` nằm trong `generationConfig` chứ không trong prompt, nên `promptVersion`");
        report.AppendLine("không ghi lại được nó — vì vậy nó có một dòng riêng ở đây (SPEC mục 14).");
        report.AppendLine();
    }

    /// <summary>
    /// **Hai đơn vị đếm khác nhau, nên cả hai phải mang nhãn.** Số file và số thẻ tính điểm không
    /// bằng nhau: <c>realcards/</c> có 19 file nhưng chỉ 18 tấm cộng vào 72 điểm, vì
    /// <c>ja-01-partial</c> tiêu một request như mọi tấm khác mà lại được chấm theo tiêu chí khác
    /// hẳn (TEST-SPEC mục 12). Để trần hai con số cùng tên "gọi được" là mời người đọc so
    /// <c>19/19</c> với một mẫu số 18 rồi tự hỏi tấm nào biến mất.
    /// </summary>
    private static void AppendCallCounts(StringBuilder report, IReadOnlyList<CardRun> runs)
    {
        var called = runs.Count(run => run.Called);
        var failed = runs.Where(run => !run.Called && !run.Skipped).ToList();
        var skipped = runs.Where(run => run.Skipped).ToList();

        var core = runs.Where(run => run.Role == CardRole.Core).ToList();
        var calledCore = core.Count(run => run.Called);

        var line = new StringBuilder(
            $"**{Num(called)}/{Num(runs.Count)} file gọi được · " +
            $"{Num(calledCore)}/{Num(core.Count)} thẻ tính điểm");

        if (failed.Count > 0)
        {
            var detail = string.Join(" · ", failed.Select(run => $"{run.CardCode} {run.ErrorCode}"));
            line.Append(CultureInfo.InvariantCulture, $" · {Num(failed.Count)} hỏng: {detail}");
        }

        if (skipped.Count > 0)
        {
            line.Append(CultureInfo.InvariantCulture,
                $" · {Num(skipped.Count)} chưa gọi ({skipped[0].ErrorCode})");
        }

        line.Append("**");
        report.AppendLine(line.ToString());
        report.AppendLine();

        var extras = runs.Where(run => run.Role != CardRole.Core).ToList();

        if (extras.Count > 0)
        {
            report.AppendLine(
                $"**{Num(runs.Count)} file ≠ {Num(core.Count)} thẻ tính điểm.** Phần chênh là "
                + string.Join(", ", extras
                    .GroupBy(run => run.Role)
                    .Select(group =>
                        string.Join(" · ", group.Select(run => $"`{run.CardCode}`")) + $" ({RoleName(group.Key)})"))
                + ".");
            report.AppendLine(
                "Phần chênh vẫn tiêu một request như mọi tấm khác, nhưng được chấm theo tiêu chí khác hẳn và");
            report.AppendLine(
                "**nằm ngoài 72 điểm** (TEST-SPEC mục 12) — xem mục riêng ở dưới. **Mọi mẫu số điểm số trong");
            report.AppendLine("khối này đếm theo thẻ tính điểm, không theo file.**");
            report.AppendLine();
        }

        if (skipped.Count > 0)
        {
            report.AppendLine(
                "Các thẻ \"chưa gọi\" **không tính là hỏng**: chúng chưa từng chạm tới mô hình. Lượt đo tự");
            report.AppendLine(
                "dừng vì gọi tiếp là chắc chắn vô ích và mỗi lời gọi lại tiêu thêm hạn mức (SPEC mục 4.6).");
            report.AppendLine();
        }
    }

    private static void AppendCoreScore(StringBuilder report, IReadOnlyList<CardRun> runs)
    {
        report.AppendLine("### Bốn trường bắt buộc — 72 điểm");
        report.AppendLine();

        AppendScoreTable(report, runs, CardScorer.CoreFields, withTotal: true);

        var score = CoreScore.From(runs);

        // Dòng phụ chỉ có mặt khi thật sự có thẻ không gọi được. Lượt đo trọn vẹn thì hai mẫu số
        // bằng nhau, in cả hai chỉ là nhiễu.
        if (score.BrokenCards > 0)
        {
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine(
                $"tính cả thẻ hỏng   : {Num(score.Hit)}/{Num(score.Total)} ({Pct(score.Percent)}%)   "
                + $"← con số nghiệm thu · mẫu số {Num(score.Cards)} thẻ tính điểm");
            report.AppendLine(
                $"chỉ thẻ có kết quả : {Num(score.Hit)}/{Num(score.CalledTotal)} ({Pct(score.CalledPercent)}%)   "
                + $"← mẫu số {Num(score.CalledCards)}/{Num(score.Cards)} thẻ tính điểm có kết quả");
            report.AppendLine("```");
            report.AppendLine();

            report.AppendLine(CultureInfo.InvariantCulture,
                $"**Con số nghiệm thu là dòng trên**: {Num(score.BrokenCards)} thẻ không có kết quả được tính");
            report.AppendLine(
                "**0 điểm cho cả bốn trường** và mẫu số giữ nguyên. Bỏ chúng ra khỏi mẫu số thì tỷ lệ đẹp lên");
            report.AppendLine("đúng vì lượt đo hỏng nhiều hơn — con số tự thưởng cho chính thất bại của nó.");
            report.AppendLine();

            report.AppendLine(
                "Dòng dưới có mặt để chặn cái sai **ngược lại**: đọc mỗi con số nghiệm thu rồi kết luận mô hình");
            report.AppendLine(
                "đọc kém, trong khi thật ra mấy tấm kia không có kết quả vì mạng rớt chứ không phải vì đọc sai.");
            report.AppendLine("Xem dòng đếm ở đầu khối để biết chúng hỏng vì mã lỗi nào.");
        }

        report.AppendLine();
    }

    private static void AppendSecondaryScore(StringBuilder report, IReadOnlyList<CardRun> runs)
    {
        report.AppendLine("### Trường phụ — báo cáo, không gate");
        report.AppendLine();

        AppendScoreTable(report, runs, CardScorer.SecondaryFields, withTotal: false);
        report.AppendLine();
    }

    private static void AppendScoreTable(
        StringBuilder report,
        IReadOnlyList<CardRun> runs,
        IReadOnlyList<string> fields,
        bool withTotal)
    {
        // Chỉ vai Core cộng điểm. ja-01-partial và neg-* chấm riêng ở dưới (TEST-SPEC mục 12).
        var core = runs.Where(run => run.Role == CardRole.Core).ToList();

        report.AppendLine($"| Nhóm | Thẻ tính điểm | {string.Join(" | ", fields)} |{(withTotal ? " Tổng |" : string.Empty)}");
        report.AppendLine(
            $"|---|---|{string.Concat(Enumerable.Repeat("---|", fields.Count))}{(withTotal ? "---|" : string.Empty)}");

        foreach (var group in (CardGroup[])[CardGroup.English, CardGroup.Japanese, CardGroup.Unknown])
        {
            var cards = core.Where(run => run.Group == group).ToList();

            if (cards.Count == 0)
            {
                continue;
            }

            AppendRow(report, GroupName(group), cards, fields, withTotal);
        }

        if (core.Count > 0)
        {
            AppendRow(report, "**Tổng**", core, fields, withTotal, bold: true);
        }
    }

    private static void AppendRow(
        StringBuilder report,
        string label,
        IReadOnlyList<CardRun> cards,
        IReadOnlyList<string> fields,
        bool withTotal,
        bool bold = false)
    {
        var row = new StringBuilder($"| {label} | {Num(cards.Count)} |");
        var hit = 0;

        foreach (var field in fields)
        {
            var matched = cards.Count(run => run.Field(field)?.Matched == true);
            hit += matched;
            row.Append(CultureInfo.InvariantCulture, $" {Num(matched)}/{Num(cards.Count)} |");
        }

        if (withTotal)
        {
            var total = cards.Count * fields.Count;
            var cell = $"{Num(hit)}/{Num(total)} ({Pct(total == 0 ? 0 : 100.0 * hit / total)}%)";
            row.Append(CultureInfo.InvariantCulture, $" {(bold ? "**" + cell + "**" : cell)} |");
        }

        report.AppendLine(row.ToString());
    }

    private static void AppendMismatches(StringBuilder report, IReadOnlyList<CardRun> runs)
    {
        var scored = runs
            .Where(run => run.Role is CardRole.Core or CardRole.AntiFabrication && run.Called)
            .ToList();

        var rows = scored
            .SelectMany(run => run.Fields
                .Where(field => !field.Matched)
                .Select(field => (run.CardCode, Field: field)))
            .ToList();

        var broken = runs.Where(run => !run.Called).ToList();

        report.AppendLine("### Sai ở đâu");
        report.AppendLine();

        if (rows.Count == 0 && broken.Count == 0)
        {
            report.AppendLine("Không trường nào sai.");
            report.AppendLine();
            return;
        }

        report.AppendLine("| Thẻ | Trường | Kỳ vọng | Nhận được | Chỉ lỗi dấu? |");
        report.AppendLine("|---|---|---|---|---|");

        foreach (var (code, field) in rows)
        {
            report.AppendLine(
                $"| {code} | {field.Field} | {Cell(field.Expected)} | {Cell(field.Actual)} | " +
                $"{(field.MatchedLoose ? "có" : string.Empty)} |");
        }

        foreach (var run in broken)
        {
            report.AppendLine($"| {run.CardCode} | *(cả thẻ)* | — | `{run.ErrorCode}` | |");
        }

        report.AppendLine();
    }

    private static void AppendSpecialRows(StringBuilder report, IReadOnlyList<CardRun> runs)
    {
        foreach (var run in runs.Where(run => run.Role == CardRole.AntiFabrication))
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"### {run.CardCode} — ca chống bịa (ngoài 72 điểm)");
            report.AppendLine();

            if (!run.Called)
            {
                report.AppendLine($"Không có kết quả: `{run.ErrorCode}`.");
                report.AppendLine();
                continue;
            }

            var card = run.Card!;
            var emails = card.Emails.Count == 0 ? "rỗng ✔" : "**có giá trị ✘ — " + string.Join(" · ", card.Emails) + "**";
            var website = string.IsNullOrWhiteSpace(card.Website) ? "rỗng ✔" : $"**có giá trị ✘ — {card.Website}**";

            report.AppendLine($"`emails`: {emails} · `website`: {website} → **{(run.SpecialPass == true ? "ĐẠT" : "TRƯỢT")}**");
            report.AppendLine();
            report.AppendLine(
                "Tấm này bị cắt mất khối liên hệ. Mô hình thấy tên công ty nhưng **không hề thấy tên miền**,");
            report.AppendLine(
                "nên hai trường trên có giá trị nghĩa là nó đã suy ra thứ không nhìn thấy (TEST-SPEC mục 12).");
            report.AppendLine();
        }

        var negatives = runs.Where(run => run.Role == CardRole.Negative).ToList();

        if (negatives.Count > 0)
        {
            report.AppendLine("### Ca âm tính (ngoài 72 điểm)");
            report.AppendLine();

            foreach (var run in negatives)
            {
                var verdict = !run.Called ? $"`{run.ErrorCode}`" : run.SpecialPass == true ? "**ĐẠT**" : "**TRƯỢT**";
                report.AppendLine($"- `{run.CardCode}` → {verdict}");
            }

            report.AppendLine();
        }

        var orphans = runs.Where(run => run.Role == CardRole.NoAnswer).ToList();

        if (orphans.Count > 0)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"### {Num(orphans.Count)} ảnh không có đáp án trong `expected.json` — không chấm");
            report.AppendLine();
            report.AppendLine(string.Join(" · ", orphans.Select(run => $"`{run.FileName}`")));
            report.AppendLine();
        }
    }

    private static void AppendConfidence(StringBuilder report, IReadOnlyList<CardRun> runs, bool isFake)
    {
        var values = runs
            .Where(run => run.Card is not null)
            .SelectMany(run => CardScorer.FilledConfidences(run.Card!))
            .ToList();

        report.AppendLine("### fieldConfidence");
        report.AppendLine();

        if (values.Count == 0)
        {
            report.AppendLine("Không có mục nào để tính.");
            report.AppendLine();
            return;
        }

        var perfect = values.Count(value => value >= 1.0);

        report.AppendLine("```");
        report.AppendLine(CultureInfo.InvariantCulture, $"min (trường có giá trị)      : {Pct2(values.Min())}");
        report.AppendLine(CultureInfo.InvariantCulture, $"trung vị (trường có giá trị) : {Pct2(Stats.Median(values))}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"số mục bằng đúng 1.0         : {Num(perfect)}/{Num(values.Count)}");
        report.AppendLine("```");
        report.AppendLine();
        report.AppendLine(
            "Chỉ tính trên **trường có giá trị**: trường rỗng luôn được 0,0 theo SPEC mục 4.5, gộp vào thì");
        report.AppendLine(
            "`min` luôn bằng 0 và cả hai con số mất sạch ý nghĩa. `min` bằng 1,0 nghĩa là mô hình chấm 1.0");
        report.AppendLine(
            "cho mọi trường — khi ấy ngưỡng 0,9 của D-4 không bao giờ tô vàng gì, và điều 6 của prompt");
        report.AppendLine("(SPEC mục 4.5) chưa ăn.");

        if (isFake)
        {
            report.AppendLine();
            report.AppendLine(
                "Ở chế độ `fake` thì ba con số trên **không nói gì về mô hình**: `FakeExtractor` tự sinh 1,0");
            report.AppendLine("cho mọi trường có giá trị (SPEC mục 4.1), nên `min` bằng 1,0 ở đây là đương nhiên.");
        }

        report.AppendLine();
    }

    private static void AppendTiming(StringBuilder report, IReadOnlyList<CardRun> runs)
    {
        var latencies = runs.Where(run => run.Called).Select(run => run.LatencyMs).ToList();

        report.AppendLine("### Độ trễ và token");
        report.AppendLine();
        report.AppendLine("```");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"p50 {Num(Stats.Percentile(latencies, 0.50))} ms · p95 {Num(Stats.Percentile(latencies, 0.95))} ms");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"tokensIn {Num(runs.Sum(run => run.TokensIn))} · tokensOut {Num(runs.Sum(run => run.TokensOut))} " +
            $"· tổng {Num(runs.Sum(run => run.TokensIn + run.TokensOut))}");
        report.AppendLine("```");
        report.AppendLine();
        report.AppendLine(
            "TEST-SPEC mục 11 ghi ngưỡng P-01 là p95 ≤ 8.000 ms. **Đó là ngưỡng đặt trước khi có số đo thật**");
        report.AppendLine(
            "nên ở đây chỉ in số cạnh nó, không phán ĐẠT/TRƯỢT — mục 11 vốn nói rõ \"chỉ đo, không gate\".");
        report.AppendLine();
        report.AppendLine(
            "Bộ đo gửi ảnh **nguyên 2560px**, còn ứng dụng thu nhỏ về 1600px trong trình duyệt trước khi gửi");
        report.AppendLine(
            "(SPEC mục 11.3). Dự án không có thư viện ảnh nào (SPEC mục 1) nên bộ đo không thu nhỏ được, và");
        report.AppendLine("vì vậy `tokensIn` ở đây **cao hơn** lúc chạy thật.");
        report.AppendLine();
    }

    private static string RoleName(CardRole role) => role switch
    {
        CardRole.AntiFabrication => "ca chống bịa",
        CardRole.Negative => "ca âm tính",
        CardRole.NoAnswer => "không có đáp án trong expected.json",
        _ => "thẻ tính điểm",
    };

    private static string GroupName(CardGroup group) => group switch
    {
        CardGroup.English => "Anh",
        CardGroup.Japanese => "Nhật + song ngữ",
        _ => "Khác",
    };

    /// <summary>Ô bảng markdown: gạch dọc và xuống dòng phải được vô hiệu, kẻo vỡ bảng.</summary>
    private static string Cell(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "*(rỗng)*"
            : value.Replace("|", "\\|").ReplaceLineEndings(" ");

    /// <summary>Dấu phẩy thập phân kiểu Việt, không phụ thuộc culture của máy đang chạy.</summary>
    private static string Pct(double value) =>
        value.ToString("F1", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Pct2(double value) =>
        value.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Dấu chấm phân nhóm nghìn kiểu Việt: <c>3.240</c>.</summary>
    private static string Num(int value) =>
        value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');
}
