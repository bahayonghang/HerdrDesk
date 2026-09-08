using System.Diagnostics;
using System.Threading.Channels;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Rpc;
using HerdDesk.Infrastructure.Ssh;

namespace HerdDesk.Infrastructure.SshTransports;

internal sealed class SshProcessChannel : IAsyncDisposable
{
    public const int StderrRetainBytes = 64 * 1024;
    private readonly OwnedChildProcess _child;
    private readonly BoundedNdjsonReader _stdout;
    private readonly Channel<byte[]> _outbound = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writer;
    private readonly Task _stderr;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _stdoutBytes;
    private long _stderrBytes;
    private int _stderrTruncated;
    private int _failed;
    private int _disposed;
    private SshTransportOutcome? _outcome;

    private SshProcessChannel(
        OwnedChildProcess child,
        SessionKey session,
        ConnectionEpoch epoch,
        SshChannelKind kind)
    {
        _child = child;
        Session = session;
        Epoch = epoch;
        Kind = kind;
        _stdout = new BoundedNdjsonReader(child.StandardOutput);
        _writer = Task.Run(() => WriteLoopAsync(_cts.Token));
        _stderr = Task.Run(() => DrainStderrAsync(_cts.Token));
    }

    public SessionKey Session { get; }
    public ConnectionEpoch Epoch { get; }
    public SshChannelKind Kind { get; }
    public int ChildProcessId => _child.Id;
    public SshTransportOutcome? Outcome => _outcome;
    public long StdoutBytes => Interlocked.Read(ref _stdoutBytes);
    public long StderrBytes => Interlocked.Read(ref _stderrBytes);
    public bool StderrTruncated => Volatile.Read(ref _stderrTruncated) != 0;

    public static SshProcessChannel Start(
        SshProcessSpec spec,
        SessionKey session,
        ConnectionEpoch epoch,
        SshChannelKind kind,
        ISshChildProcessStarter? starter = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (SshProcessSpecFactory.HasInteractiveTtyFlag(spec.Arguments) ||
            !spec.Arguments.Contains("-T"))
            throw new InvalidOperationException(SshCodes.ProfileInvalid);
        var child = (starter ?? OwnedSshChildProcessStarter.Instance).Start(spec);
        try
        {
            return new SshProcessChannel(child, session, epoch, kind);
        }
        catch
        {
            child.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _failed) != 0)
            return;
        var copy = payload.ToArray();
        try
        {
            await _outbound.Writer.WriteAsync(copy, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Fail(SshTransportCodes.TransportCancelled);
        }
        catch (ChannelClosedException)
        {
        }
    }

    public async ValueTask<byte[]?> ReadStdoutLineAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _failed) != 0)
            return null;
        try
        {
            var line = await _stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                Fail(SshTransportCodes.ChildExited);
                return null;
            }

            Interlocked.Add(ref _stdoutBytes, line.Length);
            if (!IsNdjsonObject(line))
            {
                Fail(SshTransportCodes.StdoutProtocolPollution);
                return null;
            }

            return line;
        }
        catch (RpcProtocolException error)
        {
            Fail(error.Message == RpcCodes.LineBytesLimit
                ? SshTransportCodes.RecordTooLarge
                : error.Message == RpcCodes.TruncatedRecord
                    ? SshTransportCodes.StreamTruncated
                    : SshTransportCodes.StdoutProtocolPollution);
            return null;
        }
        catch (OperationCanceledException)
        {
            Fail(SshTransportCodes.TransportCancelled);
            return null;
        }
        catch (IOException)
        {
            Fail(SshTransportCodes.ChildExited);
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Fail(SshTransportCodes.TransportCancelled);
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
            await Task.WhenAll(_writer, _stderr).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        _cts.Dispose();
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var payload in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await _child.StandardInput.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await _child.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            Fail(SshTransportCodes.ChildExited);
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
                var total = Interlocked.Add(ref _stderrBytes, read);
                if (total > StderrRetainBytes)
                    Volatile.Write(ref _stderrTruncated, 1);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private void Fail(string code)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0)
            return;
        _outbound.Writer.TryComplete();
        _outcome = SshTransportMapper.Outcome(
            Kind,
            SshTransportStage.Failed,
            Epoch,
            code,
            _clock.ElapsedMilliseconds,
            StdoutBytes,
            StderrBytes,
            _child.ExitCode,
            _child.Id);
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static bool IsNdjsonObject(byte[] line)
    {
        if (line.Length == 0)
            return false;
        return line[0] == (byte)'{';
    }
}
