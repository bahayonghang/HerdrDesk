namespace HerdDesk.Contracts;

public enum DiagnosticOutcome { Success, Failure, Dropped, Unavailable }

public sealed record DiagnosticEvent(
    DateTimeOffset UtcTimestamp,
    string Component,
    string Operation,
    DiagnosticOutcome Outcome,
    string? ErrorCode,
    long? Epoch,
    long? DurationMs,
    long? QueueBytes,
    string? DeviceAlias,
    string? SessionAlias)
{
    public const int MaxTokenLength = 64;
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IDiagnosticSink : IAsyncDisposable
{
    bool TryWrite(DiagnosticEvent evt);
    long DroppedCount { get; }
}
