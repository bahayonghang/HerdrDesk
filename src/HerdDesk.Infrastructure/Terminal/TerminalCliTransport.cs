using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Process;

namespace HerdDesk.Infrastructure.Terminal;

public sealed class TerminalCliTransport : ITerminalTransport, IChildProcessIdentity
{
    private readonly OwnedChildProcess _child;
    private readonly TerminalOpenRequest _request;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly TerminalTransportOptions _options;
    private readonly TerminalFrameParser _parser = new();
    private readonly TerminalWriteQueue _writes;
    private readonly TerminalOwnershipClassifier _ownership;
    private readonly TerminalStderrDrainer _stderr;
    private readonly Channel<TerminalTransportEvent> _events;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _stdoutTask;
    private readonly Task _stderrTask;
    private readonly Task _writerTask;
    private readonly Task _exitTask;
    private long _eventId;
    private long _commandId;
    private int _queuedItems;
    private int _queuedBytes;
    private int _closing;
    private int _ended;
    private int _endReason;
    private int _releaseOnce;
    private int _firstFrame;
    private int _sawClosed;
    private int _disposed;
    private string? _protocolCode;
    private TerminalWriteReceipt? _releaseReceipt;

    internal TerminalCliTransport(
        OwnedChildProcess child,
        TerminalOpenRequest request,
        IDiagnosticSink? diagnostics,
        TerminalTransportOptions options,
        string executableSha256)
    {
        _child = child;
        _request = request;
        _diagnostics = diagnostics;
        _options = options;
        _writes = new TerminalWriteQueue(options);
        _ownership = new TerminalOwnershipClassifier(executableSha256, options.OwnershipFingerprints);
        _stderr = new TerminalStderrDrainer(options.StderrRetainBytes);
        _events = Channel.CreateUnbounded<TerminalTransportEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _stdoutTask = Task.Run(() => TerminalStdoutPump.RunAsync(
            _child.StandardOutput, OnStdoutRecordAsync, _cts.Token));
        _stderrTask = Task.Run(() => _stderr.RunAsync(
            _child.StandardError, OnStderrClassified, _cts.Token));
        _writerTask = Task.Run(() => WriteLoopAsync(_cts.Token));
        _exitTask = Task.Run(() => WatchExitAsync(_cts.Token));
        WriteDiagnostic("open", DiagnosticOutcome.Success, null);
    }

    public PaneKey Pane => _request.Pane;
    public ConnectionEpoch Epoch => _request.Epoch;
    public TerminalMode Mode => _request.Mode;
    public int ChildProcessId => _child.Id;

    public async IAsyncEnumerable<TerminalTransportEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public ValueTask<TerminalWriteReceipt> SendInputAsync(
        TerminalInputCommand input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Mode == TerminalMode.Observe)
            return Reject(TerminalTransportCodes.ObserveInputDenied);
        var identity = CheckIdentity(input.Pane, input.Epoch);
        if (identity is not null)
            return Reject(identity);
        var error = TerminalCommandSerializer.TrySerializeInput(input, out var payload);
        return error is null ? EnqueueAsync(payload, cancellationToken) : Reject(error);
    }

    public ValueTask<TerminalWriteReceipt> ResizeAsync(
        TerminalResizeCommand size,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(size);
        if (Mode == TerminalMode.Observe)
            return Reject(TerminalTransportCodes.ObserveInputDenied);
        var identity = CheckIdentity(size.Pane, size.Epoch);
        if (identity is not null)
            return Reject(identity);
        var error = TerminalCommandSerializer.TrySerializeResize(size, out var payload);
        return error is null ? EnqueueAsync(payload, cancellationToken) : Reject(error);
    }

    public ValueTask<TerminalWriteReceipt> ScrollAsync(
        TerminalScrollCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Mode == TerminalMode.Observe)
            return Reject(TerminalTransportCodes.ObserveInputDenied);
        var identity = CheckIdentity(request.Pane, request.Epoch);
        if (identity is not null)
            return Reject(identity);
        var error = TerminalCommandSerializer.TrySerializeScroll(request, out var payload);
        return error is null ? EnqueueAsync(payload, cancellationToken) : Reject(error);
    }

    public async ValueTask<TerminalWriteReceipt> ReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        if (Mode == TerminalMode.Observe)
        {
            var id = NextCommandId();
            BeginClose(TerminalEndReason.Released);
            return new TerminalWriteReceipt(id, TerminalWriteDisposition.NotSent,
                TerminalTransportCodes.ObserveClose);
        }

        if (Interlocked.Exchange(ref _releaseOnce, 1) != 0)
        {
            return _releaseReceipt ?? new TerminalWriteReceipt(NextCommandId(),
                TerminalWriteDisposition.NotSent, TerminalTransportCodes.ReleaseAlreadyQueued);
        }

        var receipt = await EnqueueAsync(TerminalCommandSerializer.SerializeRelease(), cancellationToken)
            .ConfigureAwait(false);
        _releaseReceipt = receipt;
        BeginClose(TerminalEndReason.Released);
        return receipt;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            await Task.WhenAll(_stdoutTask, _stderrTask, _writerTask, _exitTask).ConfigureAwait(false);
            return;
        }

        BeginClose(TerminalEndReason.AppStopping);
        await _child.DisposeAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_stdoutTask, _stderrTask, _writerTask, _exitTask).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        _cts.Dispose();
    }

    private ValueTask<TerminalWriteReceipt> EnqueueAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _closing) != 0)
            return Reject(TerminalTransportCodes.Closing);
        return _writes.EnqueueAsync(NextCommandId(), payload, cancellationToken);
    }

    private ValueTask<TerminalWriteReceipt> Reject(string code)
    {
        var id = NextCommandId();
        return ValueTask.FromResult(new TerminalWriteReceipt(
            id, TerminalWriteDisposition.NotSent, code));
    }

    private string? CheckIdentity(PaneKey pane, ConnectionEpoch epoch)
    {
        if (!TerminalCliProcessFactory.IsValidPane(pane))
            return TerminalTransportCodes.InvalidIdentity;
        if (pane != Pane)
            return TerminalTransportCodes.WrongPane;
        if (epoch.Value <= 0 || epoch != Epoch)
            return TerminalTransportCodes.StaleEpoch;
        return null;
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _writes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (item.Completion.Task.IsCompleted)
                {
                    _writes.AccountWritten(item.Payload.Length);
                    continue;
                }

                Interlocked.Exchange(ref item.Written, 1);
                if (item.Completion.Task.IsCompleted)
                {
                    _writes.AccountWritten(item.Payload.Length);
                    continue;
                }

                try
                {
                    await _child.StandardInput.WriteAsync(item.Payload, cancellationToken)
                        .ConfigureAwait(false);
                    await _child.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
                    _writes.AccountWritten(item.Payload.Length);
                    item.Completion.TrySetResult(new TerminalWriteReceipt(
                        item.CommandId,
                        TerminalWriteDisposition.WrittenUnacknowledged,
                        TerminalTransportCodes.WrittenUnacknowledged));
                }
                catch (Exception error) when (error is OperationCanceledException or IOException)
                {
                    _writes.AccountWritten(item.Payload.Length);
                    item.Completion.TrySetResult(new TerminalWriteReceipt(
                        item.CommandId,
                        TerminalWriteDisposition.UnknownAfterDisconnect,
                        TerminalTransportCodes.UnknownAfterDisconnect));
                    BeginClose(TerminalEndReason.Cancelled);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private ValueTask OnStdoutRecordAsync(byte[]? line, string? error, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (error == TerminalTransportCodes.LineBytesLimit)
        {
            Publish(new TerminalProtocolFailed(NextEventId(), Pane, Epoch,
                TerminalTransportCodes.LineBytesLimit));
            _protocolCode = TerminalTransportCodes.LineBytesLimit;
            BeginClose(TerminalEndReason.ProtocolFailed);
            return ValueTask.CompletedTask;
        }

        if (error == TerminalTransportCodes.TruncatedRecord)
        {
            Publish(new TerminalStdoutEnded(NextEventId(), Pane, Epoch, Truncated: true));
            BeginClose(TerminalEndReason.TruncatedEof);
            return ValueTask.CompletedTask;
        }

        if (error is not null)
        {
            Publish(new TerminalProtocolFailed(NextEventId(), Pane, Epoch, error));
            _protocolCode = error;
            BeginClose(TerminalEndReason.ProtocolFailed);
            return ValueTask.CompletedTask;
        }

        if (line is null)
        {
            Publish(new TerminalStdoutEnded(NextEventId(), Pane, Epoch, Truncated: false));
            BeginClose(Volatile.Read(ref _sawClosed) != 0
                ? TerminalEndReason.ClosedThenEof
                : TerminalEndReason.StdoutEof);
            return ValueTask.CompletedTask;
        }

        TerminalEnvelope envelope;
        try
        {
            envelope = _parser.Parse(line);
        }
        catch (TerminalProtocolException protocol)
        {
            Publish(new TerminalProtocolFailed(NextEventId(), Pane, Epoch, protocol.Message));
            _protocolCode = protocol.Message;
            BeginClose(TerminalEndReason.ProtocolFailed);
            return ValueTask.CompletedTask;
        }

        if (envelope is TerminalClosed closed)
        {
            Interlocked.Exchange(ref _sawClosed, 1);
            Publish(new TerminalClosedObserved(
                NextEventId(), Pane, Epoch, closed.ReasonPresent, TerminalTransportCodes.Closed));
            return ValueTask.CompletedTask;
        }

        if (envelope is not TerminalFrame frame)
            return ValueTask.CompletedTask;

        var copy = frame.Bytes.ToArray();
        if (!TryReserve(copy.Length))
        {
            Publish(new TerminalConsumerBackpressure(
                NextEventId(), Pane, Epoch, TerminalTransportCodes.ConsumerBackpressure));
            BeginClose(TerminalEndReason.ConsumerBackpressure);
            return ValueTask.CompletedTask;
        }

        var owned = new TerminalOwnedFrame(
            Pane, Epoch, frame.Sequence, frame.Columns, frame.Rows, frame.Full,
            copy, ReleaseBudget);
        var arrived = new TerminalFrameArrived(NextEventId(), Pane, Epoch, owned);
        if (!Publish(arrived))
        {
            arrived.Dispose();
            return ValueTask.CompletedTask;
        }

        if (Interlocked.Exchange(ref _firstFrame, 1) == 0)
        {
            var result = _ownership.ForFirstFrame(_request, processAlive: !_child.HasExited);
            Publish(new TerminalOwnershipObserved(
                NextEventId(), Pane, Epoch, _request.ControlAttemptId, result));
        }

        return ValueTask.CompletedTask;
    }

    private void OnStderrClassified(string code, long bytesRead)
    {
        var diagnosticCode = code is "stderr_marker" or TerminalTransportCodes.StderrFlood
            or TerminalTransportCodes.Busy or TerminalTransportCodes.Rejected
            or TerminalTransportCodes.InputIgnored
            ? code
            : TerminalTransportCodes.StderrFlood;
        WriteDiagnostic("stderr", DiagnosticOutcome.Failure, diagnosticCode, bytesRead);
        var observed = _ownership.ForStderr(_request, code, _stderr.WindowContains);
        if (observed is not null)
        {
            Publish(new TerminalOwnershipObserved(
                NextEventId(), Pane, Epoch, _request.ControlAttemptId, observed));
        }
    }

    private async Task WatchExitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _child.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            Publish(new TerminalProcessExited(NextEventId(), Pane, Epoch, _child.ExitCode));
            BeginClose(TerminalEndReason.ProcessExited);
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            BeginClose(TerminalEndReason.ProcessExited);
        }
    }

    private void BeginClose(TerminalEndReason reason)
    {
        Interlocked.CompareExchange(ref _endReason, (int)reason, 0);
        if (Interlocked.Exchange(ref _closing, 1) == 0)
        {
            _writes.Complete(
                TerminalTransportCodes.NotSent,
                TerminalTransportCodes.UnknownAfterDisconnect);
            _child.CloseStandardInput();
            WriteDiagnostic("exit", DiagnosticOutcome.Failure, CurrentEndCode());
        }

        PublishEnd();
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void PublishEnd()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
            return;
        var reason = (TerminalEndReason)Volatile.Read(ref _endReason);
        Publish(new TerminalTransportEnded(
            NextEventId(),
            Pane,
            Epoch,
            TerminalExitClassifier.Code(reason, _protocolCode),
            _child.ExitCode,
            Volatile.Read(ref _sawClosed) != 0));
        _events.Writer.TryComplete();
    }

    private bool Publish(TerminalTransportEvent evt)
    {
        if (Volatile.Read(ref _ended) != 0 && evt is not TerminalTransportEnded)
            return false;
        return _events.Writer.TryWrite(evt);
    }

    private bool TryReserve(int bytes)
    {
        while (true)
        {
            var items = Volatile.Read(ref _queuedItems);
            var queued = Volatile.Read(ref _queuedBytes);
            if (items >= _options.EventItemLimit || queued + bytes > _options.EventByteLimit)
                return false;
            if (Interlocked.CompareExchange(ref _queuedItems, items + 1, items) != items)
                continue;
            if (Interlocked.CompareExchange(ref _queuedBytes, queued + bytes, queued) == queued)
                return true;
            Interlocked.Decrement(ref _queuedItems);
        }
    }

    private void ReleaseBudget(int bytes)
    {
        Interlocked.Decrement(ref _queuedItems);
        Interlocked.Add(ref _queuedBytes, -bytes);
    }

    private long NextEventId() => Interlocked.Increment(ref _eventId);
    private ulong NextCommandId() => (ulong)Interlocked.Increment(ref _commandId);

    private string CurrentEndCode() =>
        TerminalExitClassifier.Code((TerminalEndReason)Volatile.Read(ref _endReason), _protocolCode);

    private void WriteDiagnostic(string operation, DiagnosticOutcome outcome, string? code, long? queueBytes = null)
    {
        _diagnostics?.TryWrite(new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            "terminal",
            operation,
            outcome,
            code,
            Epoch.Value,
            null,
            queueBytes,
            null,
            null));
    }
}
