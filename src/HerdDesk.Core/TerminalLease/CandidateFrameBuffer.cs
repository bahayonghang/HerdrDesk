using HerdDesk.Contracts;

namespace HerdDesk.Core;

internal sealed class CandidateFrameBuffer : IDisposable
{
    private readonly int _maxBytes;
    private readonly List<TerminalFrame> _frames = [];
    private int _bytes;
    private bool _faulted;

    public CandidateFrameBuffer(int maxBytes = 24 * 1024 * 1024)
    {
        if (maxBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        _maxBytes = maxBytes;
    }

    public bool HasFullBaseline { get; private set; }
    public ulong? LastSequence { get; private set; }
    public int ByteCount => _bytes;
    public string? FaultCode { get; private set; }

    public bool TryAdd(TerminalOwnedFrame frame, out string? code)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_faulted)
        {
            code = FaultCode ?? ControlLeaseCodes.CandidateBackpressure;
            return false;
        }

        if (_frames.Count == 0 && !frame.Full)
        {
            Fault(ControlLeaseCodes.InitialFullRequired);
            code = FaultCode;
            return false;
        }

        var count = frame.Bytes.Length;
        if (count is <= 0 or > TerminalFrameParser.MaxFrameBytes)
        {
            Fault(ControlLeaseCodes.CandidateBackpressure);
            code = FaultCode;
            return false;
        }

        if (_bytes + count > _maxBytes)
        {
            Fault(ControlLeaseCodes.CandidateBackpressure);
            code = FaultCode;
            return false;
        }

        var copy = frame.Bytes.ToArray();
        _frames.Add(new TerminalFrame(frame.Sequence, frame.Columns, frame.Rows, frame.Full, copy));
        _bytes += count;
        LastSequence = frame.Sequence;
        if (frame.Full)
            HasFullBaseline = true;
        code = null;
        return true;
    }

    public IReadOnlyList<TerminalFrame> Snapshot() => _frames.ToArray();

    public void Dispose()
    {
        _frames.Clear();
        _bytes = 0;
        HasFullBaseline = false;
        LastSequence = null;
    }

    private void Fault(string code)
    {
        _faulted = true;
        FaultCode = code;
        _frames.Clear();
        _bytes = 0;
        HasFullBaseline = false;
        LastSequence = null;
    }
}
