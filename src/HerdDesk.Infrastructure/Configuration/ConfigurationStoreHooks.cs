using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Configuration;

internal sealed class ConfigurationStoreHooks
{
    public Func<ConfigurationSnapshot, byte[]>? Serialize { get; init; }
    public Action<string>? AfterTempFlushed { get; init; }
    public Action<string, string, string?>? Commit { get; init; }
}
