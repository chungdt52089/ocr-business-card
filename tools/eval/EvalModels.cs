using PartnerCard.Web.Models;

namespace PartnerCard.Eval;

/// <summary>Hai nhóm của SPEC mục 14. Thẻ song ngữ có chữ Nhật nên nằm ở nhóm Nhật.</summary>
public enum CardGroup
{
    English,
    Japanese,
    Unknown,
}

/// <summary>
/// Một tấm ảnh được chấm theo luật nào. Ba vai dưới <see cref="Core"/> **không cộng vào 72 điểm**
/// (TEST-SPEC mục 12).
/// </summary>
public enum CardRole
{
    /// <summary>Một trong 18 tấm thẻ tính điểm.</summary>
    Core,

    /// <summary><c>ja-01-partial</c> — chấm "có bịa ra trường nào không", không phải "đúng mấy trường".</summary>
    AntiFabrication,

    /// <summary><c>neg-*</c> — đạt khi <c>isBusinessCard = false</c> và mọi trường rỗng.</summary>
    Negative,

    /// <summary>Có file ảnh nhưng <c>expected.json</c> không có mục nào cho mã thẻ này.</summary>
    NoAnswer,
}

/// <summary>Kết quả so một trường. <see cref="MatchedLoose"/> là cột "khớp sau khi bỏ dấu" của SPEC mục 14.</summary>
public sealed record FieldMatch(
    string Field,
    bool Matched,
    bool MatchedLoose,
    string Expected,
    string Actual);

/// <summary>Một tấm ảnh sau khi chạy xong — thành công, hỏng, hay chưa từng được gọi.</summary>
public sealed record CardRun(
    string CardCode,
    string FileName,
    CardGroup Group,
    CardRole Role)
{
    /// <summary>Mã lỗi khi lời gọi hỏng, hoặc <c>skipped_quota</c> khi nó chưa từng được gọi.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Đúng khi lời gọi trả về một kết quả đọc được.</summary>
    public bool Called => ErrorCode is null;

    /// <summary>Chỉ đúng khi thẻ này đã bị bỏ qua vì hạn mức đã cạn — nó không tiêu request nào.</summary>
    public bool Skipped { get; init; }

    public CardExtractionResult? Card { get; init; }

    public int LatencyMs { get; init; }

    public int TokensIn { get; init; }

    public int TokensOut { get; init; }

    public IReadOnlyList<FieldMatch> Fields { get; init; } = [];

    /// <summary>Với <see cref="CardRole.AntiFabrication"/> và <see cref="CardRole.Negative"/>.</summary>
    public bool? SpecialPass { get; init; }

    /// <summary>
    /// Lý do thật của lời gọi hỏng — <c>error.status</c> và <c>error.message</c> của API, lấy từ
    /// <c>InnerException</c> (SPEC mục 4.1).
    ///
    /// <c>Exception.Message</c> của kiểu ngoài cùng là chuỗi trung tính viết cho người dùng cuối
    /// ("Đã hết hạn mức gọi mô hình hôm nay"), nên nó **không nói được lượt đo vấp vào trần nào
    /// hay model nào**. Đây là siêu dữ liệu của API chứ không phải nội dung danh thiếp, nên ghi
    /// vào <c>EVAL.md</c> là an toàn — và không có nó thì gỡ rối một lượt đo hỏng phải đoán.
    /// </summary>
    public string? ErrorDetail { get; init; }

    /// <summary>Đã chờ hết trần phút rồi gọi lại tấm này.</summary>
    public bool Retried { get; init; }

    public FieldMatch? Field(string name) =>
        Fields.FirstOrDefault(f => f.Field == name);
}

/// <summary>Điểm bốn trường bắt buộc của một nhóm.</summary>
public sealed record GroupScore(CardGroup Group, int Cards, IReadOnlyDictionary<string, (int Hit, int Total)> ByField)
{
    public int Hit => ByField.Values.Sum(v => v.Hit);

    public int Total => ByField.Values.Sum(v => v.Total);

    public double Percent => Total == 0 ? 0 : 100.0 * Hit / Total;
}

/// <summary>
/// Thống kê. <c>p50</c>/<c>p95</c> dùng **nearest-rank** — lối quen của số đo độ trễ, và luôn trả
/// về một giá trị đã thực sự đo được chứ không phải một số nội suy chưa từng xảy ra.
/// <see cref="Median"/> thì là trung vị đúng nghĩa (số chẵn phần tử thì lấy trung bình hai giữa),
/// nên hai tên gọi khác nhau trong báo cáo là cố ý.
/// </summary>
public static class Stats
{
    public static int Percentile(IReadOnlyList<int> values, double fraction)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        var rank = (int)Math.Ceiling(fraction * sorted.Count);

        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
