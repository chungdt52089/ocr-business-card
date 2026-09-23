using System.Text.Json;
using PartnerCard.Web.Models;
using PartnerCard.Web.Processing;

namespace PartnerCard.Web.Storage;

/// <summary>
/// Kho hồ sơ trên một file JSON — SPEC mục 3.1 và 3.3.
///
/// Nạp một lần lúc khởi động vào bộ nhớ. Đọc **không khoá**: danh sách được thay bằng một
/// tham chiếu mới mỗi lần đổi (copy-on-write), không sửa tại chỗ, nên người đọc luôn thấy
/// một bản nhất quán. Mọi thao tác ghi đi qua **một** <see cref="SemaphoreSlim"/> duy nhất.
/// </summary>
public sealed class JsonPartnerStore : IPartnerStore, IDisposable
{
    private const string PartnersFileName = "partners.json";
    private const string CounterFileName = "counter.json";

    private readonly string _partnersPath;
    private readonly string _counterPath;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private volatile IReadOnlyList<Partner> _partners;
    private int _lastSequence;

    private JsonPartnerStore(
        string partnersPath, string counterPath, TimeProvider clock,
        IReadOnlyList<Partner> partners, int lastSequence)
    {
        _partnersPath = partnersPath;
        _counterPath = counterPath;
        _clock = clock;
        _partners = partners;
        _lastSequence = lastSequence;
    }

    /// <summary>
    /// Nạp kho từ thư mục dữ liệu. File chưa tồn tại thì coi như mảng rỗng và chạy tiếp (S-13);
    /// file hỏng cú pháp thì **ném ngay** để tiến trình chết kèm thông báo rõ (S-12) — chạy tiếp
    /// với kho rỗng còn tệ hơn, vì trông như mất sạch dữ liệu.
    /// </summary>
    public static JsonPartnerStore LoadFrom(string dataDirectory, TimeProvider clock)
    {
        var partnersPath = Path.Combine(dataDirectory, PartnersFileName);
        var counterPath = Path.Combine(dataDirectory, CounterFileName);

        var partners = ReadPartners(partnersPath);
        var lastSequence = Math.Max(
            PartnerIdGenerator.ReadCounter(counterPath),
            HighestSequence(partners));

        return new JsonPartnerStore(partnersPath, counterPath, clock, partners, lastSequence);
    }

    public Task<Partner?> GetAsync(string partnerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(Find(_partners, partnerId));
    }

    public Task<IReadOnlyList<Partner>> SearchAsync(PartnerQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Hai phía cùng qua NameKey (SPEC 5.3): bỏ dấu, chữ thường, gộp khoảng trắng — gõ "nguyen van an"
        // ra "Nguyễn Văn An" (S-10). Rỗng sau khi qua khoá thì không lọc.
        var keyword = TextKeys.NameKey(query.Keyword);
        var company = TextKeys.NameKey(query.Company);

        // Where chứ không SelectMany: hồ sơ khớp ở hai trường vẫn chỉ ra một lần (S-20).
        var matches = _partners
            .Where(p => keyword.Length == 0 || SearchFields(p).Any(value => Matches(value, keyword)))
            .Where(p => company.Length == 0 || Matches(p.Company, company))
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.PartnerId, StringComparer.Ordinal)
            .Take(QueryLimits.Normalize(query.Take))
            .ToList();

        return Task.FromResult<IReadOnlyList<Partner>>(matches);
    }

    public async Task<Partner> UpsertAsync(Partner partner, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow();
            var current = _partners;

            var existing = string.IsNullOrWhiteSpace(partner.PartnerId)
                ? null
                : Find(current, partner.PartnerId);

            Partner saved;
            List<Partner> next;

            if (existing is null)
            {
                _lastSequence++;
                saved = partner with
                {
                    PartnerId = PartnerIdGenerator.Format(_lastSequence),
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                next = [.. current, saved];
                await PartnerIdGenerator.WriteCounterAsync(_counterPath, _lastSequence, ct);
            }
            else
            {
                // Ghi đè: CreatedAt là của lần tạo đầu, không phải của lần ghi này (S-05).
                saved = partner with
                {
                    PartnerId = existing.PartnerId,
                    CreatedAt = existing.CreatedAt,
                    UpdatedAt = now,
                };

                next = [.. current.Select(p => ReferenceEquals(p, existing) ? saved : p)];
            }

            await PersistAsync(next, ct);
            _partners = next;

            return saved;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string partnerId, string reason, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var current = _partners;
            var existing = Find(current, partnerId);
            if (existing is null)
            {
                return false;
            }

            var next = current.Where(p => !ReferenceEquals(p, existing)).ToList();

            await PersistAsync(next, ct);
            _partners = next;

            // Bộ đếm giữ nguyên — mã vừa xoá không được cấp lại (S-06).
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task<IReadOnlyList<Partner>> FindDuplicatesAsync(Partner candidate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var candidateEmails = new HashSet<string>(
            candidate.Emails.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (candidateEmails.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Partner>>([]);
        }

        // Luật duy nhất là giao nhau ở email (SPEC mục 8). Trùng tên khác email không tính — D-03.
        var matches = _partners
            .Where(p => !string.Equals(p.PartnerId, candidate.PartnerId, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.Emails.Any(candidateEmails.Contains))
            .ToList();

        return Task.FromResult<IReadOnlyList<Partner>>(matches);
    }

    public void Dispose() => _writeLock.Dispose();

    private Task PersistAsync(IReadOnlyList<Partner> snapshot, CancellationToken ct) =>
        JsonStore.WriteAtomicAsync(_partnersPath, snapshot, ct);

    private static Partner? Find(IEnumerable<Partner> partners, string partnerId) =>
        partners.FirstOrDefault(p =>
            string.Equals(p.PartnerId, partnerId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sáu trường của PRD US-06, một ô từ khoá cho cả hai nhóm: định danh (<c>fullName</c>, <c>company</c>,
    /// <c>emails</c>, <c>searchAlias</c>) và phân loại (<c>jobTitle</c>, <c>address</c>). Tách nhóm phân loại
    /// thành bộ lọc riêng là F-16, khi kho vượt khoảng 200 hồ sơ.
    ///
    /// <c>searchAlias</c> là thứ làm thẻ Nhật tìm lại được bằng chữ Latin — <c>aoba</c> ra <c>株式会社青葉精工</c> (S-19).
    /// </summary>
    private static IEnumerable<string?> SearchFields(Partner partner)
    {
        yield return partner.FullName;
        yield return partner.Company;

        foreach (var email in partner.Emails)
        {
            yield return email;
        }

        yield return partner.SearchAlias;
        yield return partner.JobTitle;
        yield return partner.Address;
    }

    /// <summary>Khớp chuỗi con trên khoá. <paramref name="key"/> đã qua <see cref="TextKeys.NameKey"/>.</summary>
    private static bool Matches(string? value, string key) =>
        TextKeys.NameKey(value).Contains(key, StringComparison.Ordinal);

    private static IReadOnlyList<Partner> ReadPartners(string partnersPath)
    {
        if (!File.Exists(partnersPath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Partner>>(
                File.ReadAllText(partnersPath), JsonStore.Options) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Không đọc được {partnersPath}: file không phải JSON hợp lệ. " +
                "Sửa file rồi chạy lại. Server dừng ở đây thay vì khởi động với kho rỗng, " +
                "vì kho rỗng trông giống hệt mất sạch dữ liệu.",
                ex);
        }
    }

    private static int HighestSequence(IEnumerable<Partner> partners)
    {
        var highest = 0;

        foreach (var partner in partners)
        {
            var id = partner.PartnerId;
            if (id.StartsWith(PartnerIdGenerator.Prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(id.AsSpan(PartnerIdGenerator.Prefix.Length), out var sequence))
            {
                highest = Math.Max(highest, sequence);
            }
        }

        return highest;
    }
}
