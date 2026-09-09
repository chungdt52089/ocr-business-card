using System.Text.Json;

namespace PartnerCard.Web.Storage;

/// <summary>
/// Cấp mã hồ sơ dạng <c>PTN0001</c> tăng dần. Số cuối đã cấp nằm trong <c>data/counter.json</c>
/// và **chỉ tiến, không lùi** — nhờ vậy mã của hồ sơ đã xoá không bao giờ được cấp lại (S-06).
/// Đếm lại từ danh sách hiện có sẽ hỏng đúng ở chỗ đó, nên phải có file riêng.
/// </summary>
internal static class PartnerIdGenerator
{
    public const string Prefix = "PTN";

    private const int PadWidth = 4;

    public static string Format(int sequence) => Prefix + sequence.ToString($"D{PadWidth}");

    public static int ReadCounter(string counterPath)
    {
        if (!File.Exists(counterPath))
        {
            return 0;
        }

        try
        {
            var state = JsonSerializer.Deserialize<CounterState>(
                File.ReadAllText(counterPath), JsonStore.Options);

            return state?.Last ?? 0;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Không đọc được {counterPath}: file không phải JSON hợp lệ. " +
                "Sửa hoặc xoá file rồi chạy lại — xoá thì bộ đếm về 0 và mã cũ có thể bị cấp lại.",
                ex);
        }
    }

    public static Task WriteCounterAsync(string counterPath, int last, CancellationToken ct) =>
        JsonStore.WriteAtomicAsync(counterPath, new CounterState(last), ct);

    private sealed record CounterState(int Last);
}
