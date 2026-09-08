using HerdDesk.Terminal.Web;

internal sealed class RecordingParseProbe : IWebParseProbe
{
    private readonly List<byte[]> chunks = [];

    public IReadOnlyList<byte[]> Chunks => chunks;
    public byte[] WrittenBytes => chunks.SelectMany(static chunk => chunk).ToArray();

    public ValueTask ParseAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        chunks.Add(bytes.ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

internal sealed class ManualParseProbe : IWebParseProbe
{
    private readonly object gate = new();
    private readonly Queue<TaskCompletionSource> waiting = new();

    public int Pending
    {
        get
        {
            lock (gate)
                return waiting.Count;
        }
    }

    public async ValueTask ParseAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        _ = bytes;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
            waiting.Enqueue(tcs);
        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                tcs)
            : default;
        await tcs.Task.ConfigureAwait(false);
    }

    public void CompleteNext()
    {
        TaskCompletionSource tcs;
        lock (gate)
        {
            if (waiting.Count == 0)
                throw new InvalidOperationException("parse_probe_empty");
            tcs = waiting.Dequeue();
        }

        tcs.TrySetResult();
    }
}
