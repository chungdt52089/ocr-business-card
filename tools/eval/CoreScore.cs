namespace PartnerCard.Eval;

/// <summary>
/// Điểm bốn trường bắt buộc, đọc theo **hai mẫu số** — và cả hai đều cần thiết vì mỗi con số một
/// mình dẫn tới một hiểu sai ngược nhau:
///
/// <list type="table">
/// <item>
///   <term><see cref="Total"/> (72)</term>
///   <description>Con số nghiệm thu. Thẻ không có kết quả tính 0 điểm cho cả bốn trường. Bỏ chúng
///   ra khỏi mẫu số thì tỷ lệ **đẹp lên đúng vì lượt đo hỏng nhiều hơn** — con số tự thưởng cho
///   chính thất bại của nó.</description>
/// </item>
/// <item>
///   <term><see cref="CalledTotal"/></term>
///   <description>Chặn cái sai ngược lại: đọc <c>60/72</c> rồi kết luận mô hình đọc kém, trong khi
///   thật ra hai tấm không có kết quả vì mạng rớt chứ không phải vì mô hình đọc sai.</description>
/// </item>
/// </list>
/// </summary>
public sealed record CoreScore(int Hit, int Cards, int CalledCards)
{
    private const int FieldsPerCard = 4;

    /// <summary>Mẫu số nghiệm thu: mọi thẻ tính điểm, kể cả thẻ không gọi được.</summary>
    public int Total => Cards * FieldsPerCard;

    /// <summary>Mẫu số chỉ tính những thẻ thực sự có kết quả.</summary>
    public int CalledTotal => CalledCards * FieldsPerCard;

    public int BrokenCards => Cards - CalledCards;

    public double Percent => Percent0(Hit, Total);

    public double CalledPercent => Percent0(Hit, CalledTotal);

    public static CoreScore From(IReadOnlyList<CardRun> runs)
    {
        var core = runs.Where(run => run.Role == CardRole.Core).ToList();

        return new CoreScore(
            Hit: core.Sum(run =>
                run.Fields.Count(field => CardScorer.CoreFields.Contains(field.Field) && field.Matched)),
            Cards: core.Count,
            CalledCards: core.Count(run => run.Called));
    }

    private static double Percent0(int hit, int total) => total == 0 ? 0 : 100.0 * hit / total;
}
