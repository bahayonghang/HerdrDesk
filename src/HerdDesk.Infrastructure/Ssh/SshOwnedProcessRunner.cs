using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;

namespace HerdDesk.Infrastructure.Ssh;

internal sealed class SshOwnedProcessRunner : ISshProcessRunner
{
    public const int MaxOutputBytes = OpenSshGParser.MaxOutputBytes;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async ValueTask<SshProcessRunResult> RunAsync(
        SshProcessSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        OwnedChildProcess child;
        try
        {
            child = OwnedChildProcess.Start(spec.Executable, spec.Arguments);
        }
        catch (Exception)
        {
            return new(255, "", "", false, cancellationToken.IsCancellationRequested, null);
        }

        var id = child.Id;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(spec.Timeout);
            var stdoutTask = ReadBoundedAsync(child.StandardOutput, linked.Token);
            var stderrTask = ReadBoundedAsync(child.StandardError, linked.Token);
            if (!spec.StandardInput.IsEmpty)
            {
                try
                {
                    await child.StandardInput.WriteAsync(spec.StandardInput, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    child.CloseStandardInput();
                    await child.DisposeAsync().ConfigureAwait(false);
                    return new(
                        255,
                        "",
                        "",
                        !cancellationToken.IsCancellationRequested,
                        cancellationToken.IsCancellationRequested,
                        id);
                }
                catch (IOException)
                {
                    child.CloseStandardInput();
                    await child.DisposeAsync().ConfigureAwait(false);
                    return new(255, "", "", false, false, id);
                }
            }

            child.CloseStandardInput();
            try
            {
                await child.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                child.CloseStandardInput();
                await child.DisposeAsync().ConfigureAwait(false);
                return new(
                    255,
                    "",
                    "",
                    !cancellationToken.IsCancellationRequested,
                    cancellationToken.IsCancellationRequested,
                    id);
            }

            string stdout;
            string stderr;
            try
            {
                stdout = await stdoutTask.ConfigureAwait(false);
                stderr = await stderrTask.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return new(255, "", "", false, false, id);
            }

            return new(child.ExitCode ?? 255, stdout, stderr, false, false, id);
        }
        finally
        {
            await child.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (read == 0)
                break;
            if (buffer.Length + read > MaxOutputBytes)
                throw new InvalidOperationException(SshCodes.ConfigInvalid);
            buffer.Write(chunk, 0, read);
        }

        try
        {
            return StrictUtf8.GetString(buffer.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return "";
        }
    }
}
