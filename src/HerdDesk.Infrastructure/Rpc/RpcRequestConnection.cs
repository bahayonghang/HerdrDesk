using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;

namespace HerdDesk.Infrastructure.Rpc;

internal sealed class PendingRpcRequest
{
    public readonly TaskCompletionSource<RpcRequestOutcome> Completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Written;
}

internal readonly record struct RpcWriteItem(RpcRequestId Id, byte[] Payload);

public sealed class RpcRequestConnection : IRpcRequestConnection
{
    private readonly OwnedChildProcess _child;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly ConcurrentDictionary<RpcRequestId, PendingRpcRequest> _pending = new();
    private readonly Channel<RpcWriteItem> _outbound = Channel.CreateBounded<RpcWriteItem>(
        new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writer;
    private readonly Task _reader;
    private readonly Task _stderr;
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _nextId;
    private int _failed;
    private string _failCode = RpcCodes.ConnectionLost;
    private RpcFailureKind _failKind = RpcFailureKind.ConnectionLost;

    internal RpcRequestConnection(
        OwnedChildProcess child,
        ConnectionEpoch epoch,
        IDiagnosticSink? diagnostics)
    {
        _child = child;
        Epoch = epoch;
        _diagnostics = diagnostics;
        _writer = Task.Run(() => WriteLoopAsync(_cts.Token));
        _reader = Task.Run(() => ReadLoopAsync(_cts.Token));
        _stderr = Task.Run(() => DrainStderrAsync(_cts.Token));
    }

    public ConnectionEpoch Epoch { get; }
    public int? ChildProcessId => _child.Id;
    public int PendingCount => _pending.Count;
    public RpcFailure? Failure =>
        Volatile.Read(ref _failed) == 0 ? null : new RpcFailure(_failCode, _failKind);
    public Task WhenCompleted => _completed.Task;

    public async ValueTask<RpcRequestOutcome> RequestAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(method))
            return new RpcRequestOutcome(new RpcFailure(RpcCodes.EnvelopeInvalid, RpcFailureKind.Protocol));
        if (Volatile.Read(ref _failed) != 0)
            return new RpcRequestOutcome(new RpcFailure(_failCode, _failKind));

        ulong raw;
        try
        {
            raw = NextId();
        }
        catch (RpcProtocolException error)
        {
            Fail(error.Message, RpcFailureKind.Protocol);
            return new RpcRequestOutcome(new RpcFailure(error.Message, RpcFailureKind.Protocol));
        }

        var id = new RpcRequestId(Epoch, raw);
        var pending = new PendingRpcRequest();
        if (!_pending.TryAdd(id, pending))
        {
            Fail(RpcCodes.DuplicateResponse, RpcFailureKind.Protocol);
            return new RpcRequestOutcome(new RpcFailure(RpcCodes.DuplicateResponse, RpcFailureKind.Protocol));
        }

        byte[] payload;
        try
        {
            payload = EncodeRequest(raw, method, parameters);
        }
        catch (Exception)
        {
            RemoveOnce(id, new RpcFailure(RpcCodes.EnvelopeInvalid, RpcFailureKind.NotSent));
            return await pending.Completion.Task.ConfigureAwait(false);
        }

        try
        {
            await _outbound.Writer.WriteAsync(new RpcWriteItem(id, payload), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RemoveOnce(id, new RpcFailure(RpcCodes.NotSent, RpcFailureKind.NotSent));
            return await pending.Completion.Task.ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            RemoveOnce(id, new RpcFailure(_failCode, _failKind));
            return await pending.Completion.Task.ConfigureAwait(false);
        }

        try
        {
            return await pending.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var written = Volatile.Read(ref pending.Written) != 0;
            var failure = written
                ? new RpcFailure(RpcCodes.CancelledAfterWrite, RpcFailureKind.CancelledAfterWrite)
                : new RpcFailure(RpcCodes.NotSent, RpcFailureKind.NotSent);
            RemoveOnce(id, failure);
            return await pending.Completion.Task.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Fail(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost);
        _outbound.Writer.TryComplete();
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        await _child.DisposeAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_writer, _reader, _stderr).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        _cts.Dispose();
    }

    private ulong NextId()
    {
        var next = Interlocked.Increment(ref _nextId);
        if ((ulong)next == ulong.MaxValue || next <= 0)
            throw new RpcProtocolException(RpcCodes.IdOverflow);
        return (ulong)next;
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_pending.TryGetValue(item.Id, out var pending) || pending.Completion.Task.IsCompleted)
                    continue;
                Interlocked.Exchange(ref pending.Written, 1);
                if (pending.Completion.Task.IsCompleted)
                    continue;
                await _child.StandardInput.WriteAsync(item.Payload, cancellationToken).ConfigureAwait(false);
                await _child.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            Fail(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var reader = new BoundedNdjsonReader(_child.StandardOutput);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    Fail(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost);
                    return;
                }
                ParsedRpcEnvelope envelope;
                try
                {
                    envelope = RpcEnvelopeParser.Parse(line);
                }
                catch (RpcProtocolException error)
                {
                    Fail(error.Message, RpcFailureKind.Protocol);
                    return;
                }

                using (envelope)
                {
                    if (!envelope.IsResponse || envelope.Id is null)
                    {
                        Fail(RpcCodes.ProtocolPollution, RpcFailureKind.Protocol);
                        return;
                    }

                    var id = new RpcRequestId(Epoch, envelope.Id.Value);
                    if (!_pending.TryRemove(id, out var pending))
                    {
                        _diagnostics?.TryWrite(new DiagnosticEvent(
                            DateTimeOffset.UtcNow, "rpc", "unknown-response", DiagnosticOutcome.Failure,
                            RpcCodes.UnknownResponse, Epoch.Value, null, null, null, null));
                        continue;
                    }

                    var document = JsonDocument.Parse(line);
                    if (!pending.Completion.TrySetResult(new RpcRequestOutcome(document)))
                        document.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (RpcProtocolException error)
        {
            Fail(error.Message, RpcFailureKind.Protocol);
        }
        catch (IOException)
        {
            Fail(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost);
        }
    }

    private async Task DrainStderrAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _child.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private void Fail(string code, RpcFailureKind kind)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0)
            return;
        _failCode = code;
        _failKind = kind;
        _outbound.Writer.TryComplete();
        var failure = new RpcFailure(code, kind);
        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var pending))
                pending.Completion.TrySetResult(new RpcRequestOutcome(failure));
        }
        _completed.TrySetResult();
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RemoveOnce(RpcRequestId id, RpcFailure failure)
    {
        if (_pending.TryRemove(id, out var pending))
            pending.Completion.TrySetResult(new RpcRequestOutcome(failure));
    }

    private static byte[] EncodeRequest(ulong id, string method, JsonElement parameters)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            if (parameters.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else
            {
                parameters.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }
}
