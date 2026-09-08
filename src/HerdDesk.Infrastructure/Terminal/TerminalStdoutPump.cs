using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Rpc;

namespace HerdDesk.Infrastructure.Terminal;

internal static class TerminalStdoutPump
{
    public static async Task RunAsync(
        Stream stdout,
        Func<byte[]?, string?, CancellationToken, ValueTask> onRecord,
        CancellationToken cancellationToken)
    {
        var reader = new BoundedNdjsonReader(stdout);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[]? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (RpcProtocolException error)
                {
                    await onRecord(null, error.Message, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (line is null)
                {
                    await onRecord(null, null, cancellationToken).ConfigureAwait(false);
                    return;
                }

                line = StripTrailingCr(line);
                if (IsWhitespaceOnly(line))
                    continue;
                await onRecord(line, null, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            await onRecord(null, TerminalTransportCodes.ConnectionLost, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static byte[] StripTrailingCr(byte[] line)
    {
        if (line.Length == 0 || line[^1] != (byte)'\r')
            return line;
        var trimmed = new byte[line.Length - 1];
        Buffer.BlockCopy(line, 0, trimmed, 0, trimmed.Length);
        return trimmed;
    }

    private static bool IsWhitespaceOnly(byte[] line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            var b = line[i];
            if (b is not ((byte)' ' or (byte)'\t'))
                return false;
        }

        return true;
    }
}
