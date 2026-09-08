using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Terminal;

internal sealed class TerminalStderrDrainer
{
    private readonly byte[] _window;
    private int _windowLength;
    public long BytesRead;
    public int LineCount;
    public string? LastCode;

    public TerminalStderrDrainer(int retainBytes)
    {
        _window = new byte[Math.Max(512, retainBytes)];
    }

    public async Task RunAsync(
        Stream stderr,
        Action<string, long> onClassified,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var pending = new List<byte>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stderr.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return;
                BytesRead += read;
                AppendWindow(buffer.AsSpan(0, read));
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        LineCount++;
                        var code = ClassifyLine(pending);
                        pending.Clear();
                        if (code is not null)
                        {
                            LastCode = code;
                            onClassified(code, BytesRead);
                        }
                    }
                    else if (pending.Count < 512)
                    {
                        pending.Add(buffer[i]);
                    }
                    else
                    {
                        pending.Clear();
                    }
                }

                if (BytesRead > _window.Length && LastCode != TerminalTransportCodes.StderrFlood)
                {
                    LastCode = TerminalTransportCodes.StderrFlood;
                    onClassified(TerminalTransportCodes.StderrFlood, BytesRead);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    public bool WindowContains(string ascii)
    {
        var needle = Encoding.ASCII.GetBytes(ascii);
        return _window.AsSpan(0, _windowLength).IndexOf(needle) >= 0;
    }

    private void AppendWindow(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length >= _window.Length)
        {
            chunk[^_window.Length..].CopyTo(_window);
            _windowLength = _window.Length;
            return;
        }

        if (_windowLength + chunk.Length <= _window.Length)
        {
            chunk.CopyTo(_window.AsSpan(_windowLength));
            _windowLength += chunk.Length;
            return;
        }

        var keep = _window.Length - chunk.Length;
        _window.AsSpan(_windowLength - keep, keep).CopyTo(_window);
        chunk.CopyTo(_window.AsSpan(keep));
        _windowLength = _window.Length;
    }

    private static string? ClassifyLine(List<byte> pending)
    {
        if (pending.Count == 0)
            return null;
        var text = Encoding.ASCII.GetString(pending.ToArray()).Trim();
        if (text.Length == 0)
            return null;
        if (text.Contains("input ignored", StringComparison.OrdinalIgnoreCase))
            return TerminalTransportCodes.InputIgnored;
        if (string.Equals(text, "busy", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(" busy", StringComparison.OrdinalIgnoreCase))
            return TerminalTransportCodes.Busy;
        if (string.Equals(text, "rejected", StringComparison.OrdinalIgnoreCase))
            return TerminalTransportCodes.Rejected;
        if (text.Length <= 64)
            return "stderr_marker";
        return null;
    }
}
