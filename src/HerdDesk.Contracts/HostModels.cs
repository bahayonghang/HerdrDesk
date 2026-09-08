namespace HerdDesk.Contracts;

public sealed record UnavailableCapability(string Name, string Code)
{
    public bool Available => false;
    public bool IsFakeSuccess => false;
}

public interface IAdapterCapability
{
    string Name { get; }
    bool Available { get; }
    bool IsFakeSuccess { get; }
}

public interface ITerminalRendererFactory : IAdapterCapability;
