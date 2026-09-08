using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed record TerminalQueueDecision(
    bool Accepted,
    int QueuedBytes,
    string Code,
    bool Stale,
    bool Overloaded,
    bool RequiresObserveReset);

public sealed class TerminalQueueBudget
{
    public const int MaxQueuedBytes = 24 * 1024 * 1024;
    private readonly Queue<QueuedFrame> _queued = new();
    private readonly object _gate = new();
    private ConnectionEpoch _epoch;
    private ulong _lastAcked;
    private int _queuedBytes;
    private bool _stale;
    private bool _overloaded;

    public TerminalQueueBudget(ConnectionEpoch epoch)
    {
        if (epoch.Value <= 0)
            throw new TerminalProtocolException(ResourceBudgetCodes.StaleEpoch);
        _epoch = epoch;
    }

    public PaneKey? Pane { get; init; }
    public int QueuedBytes
    {
        get { lock (_gate) return _queuedBytes; }
    }

    public bool Stale
    {
        get { lock (_gate) return _stale; }
    }

    public bool Overloaded
    {
        get { lock (_gate) return _overloaded; }
    }

    public TerminalQueueDecision TryEnqueue(ConnectionEpoch epoch, ulong sequence, int decodedBytes)
    {
        lock (_gate)
        {
            if (_stale || _overloaded)
                return Reject(ResourceBudgetCodes.TerminalQueueLimit, reset: true);
            if (epoch != _epoch)
            {
                _stale = true;
                return Reject(ResourceBudgetCodes.StaleEpoch, reset: true);
            }

            if (sequence == 0 || decodedBytes <= 0)
                return Reject(ResourceBudgetCodes.InvalidIdentity, reset: false);
            if (decodedBytes > TerminalFrameParser.MaxFrameBytes)
                return Reject("decoded_bytes_limit", reset: false);
            if ((long)_queuedBytes + decodedBytes > MaxQueuedBytes)
            {
                _overloaded = true;
                _stale = true;
                return Reject(ResourceBudgetCodes.TerminalQueueLimit, reset: true);
            }

            _queued.Enqueue(new QueuedFrame(sequence, decodedBytes));
            _queuedBytes += decodedBytes;
            return new TerminalQueueDecision(true, _queuedBytes, "queued", false, false, false);
        }
    }

    public TerminalQueueDecision Acknowledge(ConnectionEpoch epoch, ulong sequence)
    {
        lock (_gate)
        {
            if (epoch != _epoch)
                return Reject(ResourceBudgetCodes.StaleRenderAck, reset: false);
            if (_queued.Count == 0)
                return Reject(ResourceBudgetCodes.StaleRenderAck, reset: false);
            var oldest = _queued.Peek();
            if (sequence <= _lastAcked || sequence != oldest.Sequence)
                return Reject(ResourceBudgetCodes.StaleRenderAck, reset: false);
            _queued.Dequeue();
            _queuedBytes -= oldest.Bytes;
            _lastAcked = sequence;
            return new TerminalQueueDecision(true, _queuedBytes, "parse_consumed", _stale, _overloaded, false);
        }
    }

    public TerminalQueueDecision AcknowledgeParseConsumed(RenderToken token) =>
        Acknowledge(token.Epoch, token.Sequence);

    public TerminalQueueDecision ClassifyDeltaDrop()
    {
        lock (_gate)
        {
            _stale = true;
            _overloaded = true;
            return Reject(ResourceBudgetCodes.DeltaDropForbidden, reset: true);
        }
    }

    public void Reset(ConnectionEpoch next)
    {
        lock (_gate)
        {
            if (next.Value <= 0 || next.Value <= _epoch.Value)
                throw new TerminalProtocolException(ResourceBudgetCodes.StaleEpoch);
            _epoch = next;
            _queued.Clear();
            _queuedBytes = 0;
            _lastAcked = 0;
            _stale = false;
            _overloaded = false;
        }
    }

    private TerminalQueueDecision Reject(string code, bool reset) =>
        new(false, _queuedBytes, code, _stale, _overloaded, reset);

    private readonly record struct QueuedFrame(ulong Sequence, int Bytes);
}
