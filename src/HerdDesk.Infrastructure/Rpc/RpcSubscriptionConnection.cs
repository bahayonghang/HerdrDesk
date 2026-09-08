using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;

namespace HerdDesk.Infrastructure.Rpc;

public sealed class RpcSubscriptionConnection : IRpcSubscriptionConnection
{
    private const ulong SubscribeId = 1;
    private readonly OwnedChildProcess _child;
    private readonly Channel<JsonElement> _events = Channel.CreateBounded<JsonElement>(
        new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly TaskCompletionSource<bool> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _reader;
    private readonly Task _stderr;
    private RpcFailure? _failure;

    internal RpcSubscriptionConnection(
        OwnedChildProcess child,
        ConnectionEpoch epoch,
        JsonElement subscribeParameters)
    {
        _child = child;
        Epoch = epoch;
        var payload = RpcRequestConnectionEncode.Subscribe(SubscribeId, subscribeParameters);
        _child.StandardInput.Write(payload);
        _child.StandardInput.Flush();
        _reader = Task.Run(() => ReadLoopAsync(_cts.Token));
        _stderr = Task.Run(() => DrainStderrAsync(_cts.Token));
    }

    public ConnectionEpoch Epoch { get; }
    public int? ChildProcessId => _child.Id;
    public RpcFailure? Failure => _failure;

    public async IAsyncEnumerable<JsonElement> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_failure is not null)
            yield break;
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public async ValueTask DisposeAsync()
    {
        Fail(new RpcFailure(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost));
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
            await Task.WhenAll(_reader, _stderr).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        _cts.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var reader = new BoundedNdjsonReader(_child.StandardOutput);
        var acknowledged = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    Fail(new RpcFailure(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost));
                    return;
                }

                ParsedRpcEnvelope envelope;
                try
                {
                    envelope = RpcEnvelopeParser.Parse(line);
                }
                catch (RpcProtocolException error)
                {
                    Fail(new RpcFailure(error.Message, RpcFailureKind.Protocol));
                    return;
                }

                using (envelope)
                {
                    if (!acknowledged)
                    {
                        if (!envelope.IsResponse || envelope.Id != SubscribeId)
                        {
                            Fail(new RpcFailure(RpcCodes.ProtocolPollution, RpcFailureKind.Protocol));
                            return;
                        }
                        if (envelope.HasError)
                        {
                            Fail(new RpcFailure(RpcCodes.SubscribeAckFailed, RpcFailureKind.SubscribeAckFailed));
                            return;
                        }
                        acknowledged = true;
                        _ready.TrySetResult(true);
                        continue;
                    }

                    var clone = envelope.Document.RootElement.Clone();
                    if (!_events.Writer.TryWrite(clone))
                    {
                        Fail(new RpcFailure(RpcCodes.EventQueueOverflow, RpcFailureKind.EventQueueOverflow));
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (RpcProtocolException error)
        {
            Fail(new RpcFailure(error.Message, RpcFailureKind.Protocol));
        }
        catch (IOException)
        {
            Fail(new RpcFailure(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost));
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

    private void Fail(RpcFailure failure)
    {
        _failure ??= failure;
        _events.Writer.TryComplete();
        _ready.TrySetResult(false);
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

internal static class RpcRequestConnectionEncode
{
    internal static byte[] Subscribe(ulong id, JsonElement parameters)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("method", "events.subscribe");
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
