using System.Threading.Channels;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Terminal;

internal sealed class TerminalWriteItem
{
    public required ulong CommandId { get; init; }
    public required byte[] Payload { get; init; }
    public readonly TaskCompletionSource<TerminalWriteReceipt> Completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Written;
}

internal sealed class TerminalWriteQueue
{
    private readonly Channel<TerminalWriteItem> _channel;
    private readonly int _byteLimit;
    private int _queuedBytes;
    private int _closed;

    public TerminalWriteQueue(TerminalTransportOptions options)
    {
        _byteLimit = options.WriteQueueByteLimit;
        _channel = Channel.CreateBounded<TerminalWriteItem>(new BoundedChannelOptions(options.WriteQueueItemLimit)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public ChannelReader<TerminalWriteItem> Reader => _channel.Reader;

    public bool TryClose() => Interlocked.Exchange(ref _closed, 1) == 0;

    public async ValueTask<TerminalWriteReceipt> EnqueueAsync(
        ulong commandId,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _closed) != 0)
            return new TerminalWriteReceipt(commandId, TerminalWriteDisposition.NotSent,
                TerminalTransportCodes.Closing);
        if (payload.Length > _byteLimit)
            return new TerminalWriteReceipt(commandId, TerminalWriteDisposition.NotSent,
                TerminalTransportCodes.QueueBytesLimit);

        while (true)
        {
            var current = Volatile.Read(ref _queuedBytes);
            if (current + payload.Length > _byteLimit)
                return new TerminalWriteReceipt(commandId, TerminalWriteDisposition.NotSent,
                    TerminalTransportCodes.QueueBytesLimit);
            if (Interlocked.CompareExchange(ref _queuedBytes, current + payload.Length, current) == current)
                break;
        }

        var item = new TerminalWriteItem { CommandId = commandId, Payload = payload };
        try
        {
            await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Add(ref _queuedBytes, -payload.Length);
            return new TerminalWriteReceipt(commandId, TerminalWriteDisposition.NotSent,
                TerminalTransportCodes.NotSent);
        }
        catch (ChannelClosedException)
        {
            Interlocked.Add(ref _queuedBytes, -payload.Length);
            return new TerminalWriteReceipt(commandId, TerminalWriteDisposition.NotSent,
                TerminalTransportCodes.Closing);
        }

        try
        {
            return await item.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var written = Volatile.Read(ref item.Written) != 0;
            var receipt = written
                ? new TerminalWriteReceipt(commandId, TerminalWriteDisposition.UnknownAfterDisconnect,
                    TerminalTransportCodes.UnknownAfterDisconnect)
                : new TerminalWriteReceipt(commandId, TerminalWriteDisposition.NotSent,
                    TerminalTransportCodes.NotSent);
            item.Completion.TrySetResult(receipt);
            return await item.Completion.Task.ConfigureAwait(false);
        }
    }

    public void Complete(string notSentCode, string unknownCode)
    {
        TryClose();
        _channel.Writer.TryComplete();
        while (_channel.Reader.TryRead(out var item))
        {
            Interlocked.Add(ref _queuedBytes, -item.Payload.Length);
            var written = Volatile.Read(ref item.Written) != 0;
            item.Completion.TrySetResult(written
                ? new TerminalWriteReceipt(item.CommandId, TerminalWriteDisposition.UnknownAfterDisconnect,
                    unknownCode)
                : new TerminalWriteReceipt(item.CommandId, TerminalWriteDisposition.NotSent, notSentCode));
        }
    }

    public void AccountWritten(int payloadLength) =>
        Interlocked.Add(ref _queuedBytes, -payloadLength);
}
