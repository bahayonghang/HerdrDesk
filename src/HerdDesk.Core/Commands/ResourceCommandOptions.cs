namespace HerdDesk.Core;

public sealed record ResourceCommandOptions
{
    public int MailboxCapacity { get; init; } = 32;
    public TimeSpan RpcTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ObserveTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public static ResourceCommandOptions Default { get; } = new();
}
