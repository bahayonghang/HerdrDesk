using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Terminal;

public sealed class TerminalTransportOptions
{
    public int EventItemLimit { get; init; } = 32;
    public int EventByteLimit { get; init; } = 24 * 1024 * 1024;
    public int WriteQueueItemLimit { get; init; } = 32;
    public int WriteQueueByteLimit { get; init; } = 256 * 1024;
    public int StderrRetainBytes { get; init; } = 64 * 1024;
    public int DirectChildWaitMilliseconds { get; init; } = 3000;
    public IReadOnlyList<TerminalOwnershipFingerprint> OwnershipFingerprints { get; init; } =
        Array.Empty<TerminalOwnershipFingerprint>();
}
