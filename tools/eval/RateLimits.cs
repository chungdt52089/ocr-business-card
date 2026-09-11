namespace PartnerCard.Eval;

/// <summary>
/// Hạn mức **quan sát được trên khoá free tier của dự án**, ngày 11/09/2026 — không phải con số
/// Google công bố, và nó đổi theo model lẫn theo thời gian (SPEC mục 4.6). Đây chỉ là cơ sở chọn
/// nhịp nghỉ mặc định; <c>--delay</c> luôn thắng.
///
/// | Model | RPM | RPD |
/// |---|---|---|
/// | <c>gemini-3.8-flash</c> | 5 | 20 |
/// | <c>gemini-3.5-flash-lite</c> | 15 | 500 |
///
/// **Model lạ thì lấy nhịp chặt nhất trong bảng, không lấy nhịp thoáng nhất.** Sai theo hướng
/// chờ lâu thì tốn thời gian; sai theo hướng gọi nhanh thì ăn 429 giữa lượt đo và mất cả bảng số.
/// </summary>
public static class RateLimits
{
    private static readonly Dictionary<string, int> RequestsPerMinute =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-3.8-flash"] = 5,
            ["gemini-3.5-flash-lite"] = 15,
        };

    /// <summary>
    /// Nhịp nghỉ vừa đủ dưới trần phút, cộng một giây biên: 5 RPM → 13 giây · 15 RPM → 5 giây.
    /// Biên cần có vì trần tính theo cửa sổ trượt, không theo phút tròn trên đồng hồ của ta.
    /// </summary>
    public static TimeSpan DelayFor(string model)
    {
        var rpm = RequestsPerMinute.TryGetValue(model, out var known)
            ? known
            : RequestsPerMinute.Values.Min();

        return TimeSpan.FromSeconds(Math.Ceiling(60.0 / rpm) + 1);
    }

    /// <summary>Mô tả cơ sở của nhịp nghỉ, để <c>EVAL.md</c> không in ra một con số không rõ ở đâu.</summary>
    public static string Explain(string model) =>
        RequestsPerMinute.TryGetValue(model, out var rpm)
            ? $"{rpm} RPM quan sát ngày 11/09"
            : $"model lạ — lấy nhịp chặt nhất trong bảng ({RequestsPerMinute.Values.Min()} RPM)";
}
