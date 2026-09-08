using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// One parser per connection epoch. Seq is compared only inside the current
/// epoch. A stale epoch faults the gate. Reconnect constructs a new parser.
/// </summary>
public sealed class RendererEpochGate
{
    private ConnectionEpoch epoch;
    private TerminalFrameParser parser;
    private bool faulted;

    public RendererEpochGate(ConnectionEpoch epoch)
    {
        if (epoch.Value <= 0)
            throw new TerminalProtocolException("stale_epoch");
        this.epoch = epoch;
        parser = new TerminalFrameParser();
    }

    public ConnectionEpoch Epoch => epoch;

    public TerminalEnvelope Accept(ConnectionEpoch incoming, ReadOnlyMemory<byte> json)
    {
        if (faulted)
            throw new TerminalProtocolException("terminal_stream_not_active");
        if (incoming != epoch)
        {
            faulted = true;
            throw new TerminalProtocolException("stale_epoch");
        }
        try
        {
            return parser.Parse(json);
        }
        catch
        {
            faulted = true;
            throw;
        }
    }

    public void Reconnect(ConnectionEpoch next)
    {
        if (next.Value <= 0 || next.Value <= epoch.Value)
            throw new TerminalProtocolException("stale_epoch");
        epoch = next;
        parser = new TerminalFrameParser();
        faulted = false;
    }
}
