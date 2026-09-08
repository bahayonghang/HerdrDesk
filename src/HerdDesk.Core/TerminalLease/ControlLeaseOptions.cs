namespace HerdDesk.Core;

public sealed record ControlLeaseOptions
{
    public int MailboxCapacity { get; init; } = 64;
    public int CandidateByteLimit { get; init; } = 24 * 1024 * 1024;

    public static ControlLeaseOptions Default { get; } = new();
}
