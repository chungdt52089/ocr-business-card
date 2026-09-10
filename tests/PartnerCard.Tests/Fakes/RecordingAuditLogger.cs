using PartnerCard.Web.Audit;

namespace PartnerCard.Tests.Fakes;

/// <summary>Giữ các dòng audit trong bộ nhớ để ca G-11 và G-12 soi được.</summary>
public sealed class RecordingAuditLogger : IAuditLogger
{
    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public void Log(AuditEntry entry) => _entries.Add(entry);
}

/// <summary>Đếm số lần <c>IExtractor</c> được gọi — ca G-13 (không có lần thử lại nào).</summary>
public sealed class CountingExtractor(PartnerCard.Web.Extraction.IExtractor inner)
    : PartnerCard.Web.Extraction.IExtractor
{
    public int Calls { get; private set; }

    // Chuyển tiếp nguyên số đo của bản cài bên trong: nó mới là bên thực sự chạy.
    public Task<PartnerCard.Web.Extraction.RawExtraction> ExtractRawAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct)
    {
        Calls++;
        return inner.ExtractRawAsync(imageBytes, mimeType, languageHint, sourceName, ct);
    }

    public Task<PartnerCard.Web.Models.CardExtractionResult> ExtractAsync(
        ReadOnlyMemory<byte> imageBytes, string mimeType, string? languageHint,
        string? sourceName, CancellationToken ct)
    {
        Calls++;
        return inner.ExtractAsync(imageBytes, mimeType, languageHint, sourceName, ct);
    }
}
