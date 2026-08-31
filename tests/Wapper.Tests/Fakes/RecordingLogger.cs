using Microsoft.Extensions.Logging;

namespace Wapper.Tests.Fakes;

/// <summary>Keeps every line logged, so a test can say what the client said about itself.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId Event, string Message)> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Lines.Add((logLevel, eventId, formatter(state, exception)));
}
