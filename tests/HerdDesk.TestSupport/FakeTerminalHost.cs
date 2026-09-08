using System.Text;

internal static class FakeTerminalHost
{
    public const string AdapterProvedMarker = "hd004-tl10-adapter-proved";

    public static int Run(string[] args)
    {
        var mode = args.Length == 0 ? "frames" : args[0];
        string? stdinFile = null;
        string? argvFile = null;
        var chunkSize = 0;
        var i = 1;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--stdin-file":
                    stdinFile = args[++i];
                    i++;
                    continue;
                case "--argv-file":
                    argvFile = args[++i];
                    i++;
                    continue;
                case "--chunk-size":
                    chunkSize = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    i++;
                    continue;
                default:
                    break;
            }
            break;
        }

        if (argvFile is not null)
            File.WriteAllLines(argvFile, args.Skip(i));

        return mode switch
        {
            "chunked" => Frames(2, chunkSize > 0 ? chunkSize : 1, crlf: false, stdinFile),
            "crlf" => Frames(2, chunkSize > 0 ? chunkSize : 7, crlf: true, stdinFile),
            "closed-eof" => ClosedEof(),
            "truncated" => Truncated(),
            "malformed" => Malformed(),
            "oversize" => Oversize(),
            "gap" => Gap(),
            "stderr-flood" => StderrFlood(stdinFile),
            "capture" => Capture(stdinFile, emitFrame: true),
            "hang" => Hang(),
            "exit-1" => ExitOne(),
            "busy-stderr" => Busy(stdinFile),
            "ignored-stderr" => Ignored(stdinFile),
            "flood-frames" => FloodFrames(),
            "graphics-absent" => Frames(2, 0, crlf: false, stdinFile),
            "proved" => Proved(stdinFile),
            _ => Frames(2, 0, crlf: false, stdinFile)
        };
    }

    private static int Frames(int count, int chunkSize, bool crlf, string? stdinFile)
    {
        var payload = new List<byte>();
        for (var seq = 1; seq <= count; seq++)
            payload.AddRange(FrameLine((ulong)seq, seq == 1, crlf));
        WriteBytes(payload.ToArray(), chunkSize);
        Capture(stdinFile, emitFrame: false);
        return 0;
    }

    private static int ClosedEof()
    {
        WriteBytes(FrameLine(1, true, false), 0);
        WriteBytes(Encoding.UTF8.GetBytes("""{"type":"terminal.closed","reason":"detached"}""" + "\n"), 0);
        return 0;
    }

    private static int Truncated()
    {
        WriteBytes("{\"type\":\"terminal.frame\""u8.ToArray(), 0);
        return 0;
    }

    private static int Malformed()
    {
        WriteBytes("not-json\n"u8.ToArray(), 0);
        return 0;
    }

    private static int Oversize()
    {
        var stdout = Console.OpenStandardOutput();
        var chunk = Encoding.UTF8.GetBytes(new string('x', 64 * 1024));
        var remaining = 16 * 1024 * 1024 + 1;
        while (remaining > 0)
        {
            var n = Math.Min(remaining, chunk.Length);
            stdout.Write(chunk, 0, n);
            remaining -= n;
        }
        stdout.Flush();
        return 0;
    }

    private static int Gap()
    {
        WriteBytes(FrameLine(1, true, false), 0);
        WriteBytes(FrameLine(3, false, false), 0);
        Capture(null, emitFrame: false);
        return 0;
    }

    private static int StderrFlood(string? stdinFile)
    {
        var junk = Encoding.UTF8.GetBytes(new string('x', 4096) + "\n");
        var stderr = Console.OpenStandardError();
        for (var i = 0; i < 256; i++)
            stderr.Write(junk, 0, junk.Length);
        stderr.Flush();
        WriteBytes(FrameLine(1, true, false), 0);
        Capture(stdinFile, emitFrame: false);
        return 0;
    }

    private static int Capture(string? stdinFile, bool emitFrame)
    {
        if (emitFrame)
            WriteBytes(FrameLine(1, true, false), 0);
        var lines = new List<string>();
        foreach (var line in ReadLines())
            lines.Add(Encoding.UTF8.GetString(line));
        if (stdinFile is not null)
            File.WriteAllLines(stdinFile, lines);
        return 0;
    }

    private static int Hang()
    {
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    private static int ExitOne()
    {
        WriteBytes(FrameLine(1, true, false), 0);
        return 1;
    }

    private static int Busy(string? stdinFile)
    {
        WriteStderr("busy\n");
        WriteBytes(FrameLine(1, true, false), 0);
        Capture(stdinFile, emitFrame: false);
        return 0;
    }

    private static int Ignored(string? stdinFile)
    {
        WriteStderr("input ignored\n");
        WriteBytes(FrameLine(1, true, false), 0);
        Capture(stdinFile, emitFrame: false);
        return 0;
    }

    private static int FloodFrames()
    {
        for (var seq = 1; seq <= 8; seq++)
            WriteBytes(FrameLine((ulong)seq, seq == 1, false), 0);
        Capture(null, emitFrame: false);
        return 0;
    }

    private static int Proved(string? stdinFile)
    {
        WriteStderr(AdapterProvedMarker + "\n");
        WriteBytes(FrameLine(1, true, false), 0);
        Capture(stdinFile, emitFrame: false);
        return 0;
    }

    private static byte[] FrameLine(ulong seq, bool full, bool crlf)
    {
        var bytes = seq == 1
            ? "G1syShtbSFN5bnRoZXRpYyBIZXJkRGVzayBmaXh0dXJlDQo="
            : seq == 2 ? "5Lg=" : "reaWhw==";
        var json = "{\"type\":\"terminal.frame\",\"seq\":" + seq +
                   ",\"encoding\":\"ansi\",\"width\":120,\"height\":40,\"full\":" +
                   (full ? "true" : "false") + ",\"bytes\":\"" + bytes + "\"}";
        return Encoding.UTF8.GetBytes(json + (crlf ? "\r\n" : "\n"));
    }

    private static void WriteBytes(byte[] data, int chunkSize)
    {
        var stdout = Console.OpenStandardOutput();
        if (chunkSize <= 0)
        {
            stdout.Write(data, 0, data.Length);
            stdout.Flush();
            return;
        }

        for (var i = 0; i < data.Length; i += chunkSize)
        {
            var n = Math.Min(chunkSize, data.Length - i);
            stdout.Write(data, i, n);
            stdout.Flush();
        }
    }

    private static void WriteStderr(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var stderr = Console.OpenStandardError();
        stderr.Write(bytes, 0, bytes.Length);
        stderr.Flush();
    }

    private static IEnumerable<byte[]> ReadLines()
    {
        using var stdin = Console.OpenStandardInput();
        var pending = new List<byte>();
        var buffer = new byte[4096];
        while (true)
        {
            var read = stdin.Read(buffer, 0, buffer.Length);
            if (read == 0)
                yield break;
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    yield return pending.ToArray();
                    pending.Clear();
                }
                else
                {
                    pending.Add(buffer[i]);
                }
            }
        }
    }
}
