using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HerdDesk.Contracts;
using OsProcess = System.Diagnostics.Process;

namespace HerdDesk.Infrastructure.Process;

internal static class OwnedChildProcessKillLedger
{
    internal static readonly ConcurrentBag<int> KilledProcessIds = [];
    internal static readonly ConcurrentBag<int> StartedProcessIds = [];
}

public sealed class OwnedChildProcess : IAsyncDisposable
{
    public const int CancelWaitMilliseconds = 3000;
    private readonly OsProcess _process;
    private int _disposed;

    private OwnedChildProcess(OsProcess process)
    {
        _process = process;
    }

    public int Id => _process.Id;
    public Stream StandardInput => _process.StandardInput.BaseStream;
    public Stream StandardOutput => _process.StandardOutput.BaseStream;
    public Stream StandardError => _process.StandardError.BaseStream;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            try
            {
                return HasExited ? _process.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
        _process.WaitForExitAsync(cancellationToken);

    public void CloseStandardInput()
    {
        try
        {
            _process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    public static OwnedChildProcess Start(string executable, IReadOnlyList<string> arguments) =>
        Start(executable, arguments, null);

    public static OwnedChildProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new ArgumentException(RpcCodes.ExecutableInvalid, nameof(executable));
        if (workingDirectory is not null &&
            (!Path.IsPathFullyQualified(workingDirectory) || !Directory.Exists(workingDirectory)))
            throw new ArgumentException(RpcCodes.ExecutableInvalid, nameof(workingDirectory));

        var utf8 = new UTF8Encoding(false, true);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8
        };
        if (workingDirectory is not null)
            start.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        var process = OsProcess.Start(start);
        if (process is null)
            throw new InvalidOperationException(RpcCodes.ChildExited);
        OwnedChildProcessKillLedger.StartedProcessIds.Add(process.Id);
        return new OwnedChildProcess(process);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            _process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }

        try
        {
            if (!HasExited)
            {
                var exited = _process.WaitForExitAsync();
                var finished = await Task.WhenAny(exited, Task.Delay(CancelWaitMilliseconds))
                    .ConfigureAwait(false);
                if (finished != exited && !HasExited)
                {
                    OwnedChildProcessKillLedger.KilledProcessIds.Add(_process.Id);
                    _process.Kill(entireProcessTree: false);
                }
                try
                {
                    await _process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }

        _process.Dispose();
    }
}
