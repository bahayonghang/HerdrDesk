namespace HerdDesk.Terminal.Web;

public interface IWebParseProbe
{
    ValueTask ParseAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
}

public sealed class ImmediateParseProbe : IWebParseProbe
{
    public ValueTask ParseAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        _ = bytes;
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
