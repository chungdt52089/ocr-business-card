using Microsoft.Extensions.Logging;

namespace PartnerCard.Tests.Fakes;

/// <summary>Một dòng <see cref="ILogger"/> đã ghi: mức và thông điệp đã định dạng.</summary>
public sealed record LogLine(LogLevel Level, string Message);

/// <summary>
/// Giữ các dòng <see cref="ILogger"/> trong bộ nhớ để ca M-11 soi được cảnh báo thiếu header.
///
/// Đây là kênh **chẩn đoán**, khác hẳn <see cref="RecordingAuditLogger"/> — cái đó giữ
/// <c>AuditEntry</c>. Hai kênh không trộn: cảnh báo không bao giờ thành một dòng audit.
///
/// Vừa là logger vừa là provider, để cắm được vào <c>AddLogging</c> khi ca kiểm thử dựng DI thật.
/// </summary>
public sealed class RecordingLogger : ILogger, ILoggerProvider
{
    private readonly List<LogLine> _entries = [];

    public IReadOnlyList<LogLine> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Add(new LogLine(logLevel, formatter(state, exception)));

    public ILogger CreateLogger(string categoryName) => this;

    public void Dispose()
    {
        // Không giữ tài nguyên nào.
    }
}
