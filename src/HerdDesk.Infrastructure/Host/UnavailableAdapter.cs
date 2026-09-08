using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Host;

public sealed class UnavailableAdapter :
    IRpcConnectionFactory,
    ITerminalTransportFactory,
    ITerminalRendererFactory,
    IAsyncDisposable
{
    public UnavailableAdapter(string name, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Name = name;
        Code = code;
    }

    public string Name { get; }
    public string Code { get; }
    public bool Available => false;
    public bool IsFakeSuccess => false;

    public UnavailableCapability Capability => new(Name, Code);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
