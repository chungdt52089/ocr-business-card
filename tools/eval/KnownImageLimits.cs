namespace PartnerCard.Eval;

/// <summary>
/// Những chỗ sai đã truy được về **chất lượng tấm ảnh**, không về prompt.
///
/// Đây là danh sách "đừng chỉnh prompt cho chỗ này". Không có nó thì một dòng sai trong `EVAL.md`
/// trông y hệt mọi dòng sai khác, và người đọc tiếp theo — có thể là chính ta ba tuần sau — sẽ
/// ngồi viết lại luật đọc địa chỉ cho một tấm mà **không luật nào cứu được**: chữ ở đó tương phản
/// thấp tới mức con số bị đọc nhầm, và prompt không làm chữ sáng lên.
///
/// **Điều kiện để một mục được vào đây: đã lặp lại ở nhiều lượt đo độc lập.** Sai một lần là
/// nhiễu; sai đúng một kiểu ở hai lượt riêng biệt mới là tính chất của tấm ảnh. Ghi chú chỉ hiện
/// khi trường đó **thật sự sai ở lượt này** — tấm nào được chụp lại sáng hơn thì ghi chú tự biến
/// mất cùng lỗi, không thành một lời bào chữa nằm lại vĩnh viễn.
/// </summary>
public static class KnownImageLimits
{
    private static readonly Dictionary<(string Card, string Field), string> Notes = new()
    {
        [("ja-05", "address")] =
            "Thẻ nền tối, dòng địa chỉ ở đáy tương phản thấp (TEST-SPEC mục 12). Mô hình đọc nhầm "
            + "chữ số 8 thành 6 ở **cả hai lượt đo độc lập**, và đây cũng là tấm chậm nhất của cả "
            + "hai lượt. Chụp lại sáng hơn thì sửa được; sửa prompt thì không.",
    };

    public static string? For(string cardCode, string field) =>
        Notes.TryGetValue((cardCode, field), out var note) ? note : null;

    /// <summary>Các mục thật sự sai ở lượt này — mục đã hết sai thì không nhắc tới.</summary>
    public static IReadOnlyList<(string Card, string Field, string Note)> Hit(IReadOnlyList<CardRun> runs) =>
    [
        .. runs.SelectMany(run => run.Fields
            .Where(field => !field.Matched)
            .Select(field => (run.CardCode, field.Field, Note: For(run.CardCode, field.Field)))
            .Where(row => row.Note is not null)
            .Select(row => (row.CardCode, row.Field, row.Note!))),
    ];
}
