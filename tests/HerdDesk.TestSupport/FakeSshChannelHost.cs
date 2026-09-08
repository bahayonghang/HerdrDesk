using System.Text;
using System.Text.Json;

internal static class FakeSshChannelHost
{
    public const string StderrJsonCanary = "canary-stderr-json";
    public const string BannerText = "WELCOME TO THE BRIDGE";
    public const string HandshakeText = "herddesk-bridge/1.0";

    public static int Run(string[] args)
    {
        if (args.Length == 0)
            return 2;
        var mode = args[0];
        var sshArgs = args.Skip(1).ToArray();
        if (!sshArgs.Contains("-T") || sshArgs.Contains("-t") || sshArgs.Contains("-tt"))
            return 3;

        return mode switch
        {
            "rpc" => UnifiedRpc(),
            "subscribe" => FakeBridgeHost.Run(["subscribe"]),
            "banner" => BannerThen(UnifiedRpc),
            "banner-between" => BannerBetween(),
            "stderr-json" => StderrJsonThen(UnifiedRpc),
            "oversize" => Oversize(),
            "truncated" => Truncated(),
            "hang" => Hang(),
            "stderr-flood" => StderrFloodThen(UnifiedRpc),
            "handshake" => BannerThen(UnifiedRpc, HandshakeText),
            "chunked" => ChunkedRpc(),
            "terminal" => FakeTerminalHost.Run(["frames"]),
            "terminal-banner" => BannerThen(() => FakeTerminalHost.Run(["frames"])),
            "terminal-stderr-json" => StderrJsonThen(() => FakeTerminalHost.Run(["frames"])),
            "terminal-closed" => FakeTerminalHost.Run(["closed-eof"]),
            "terminal-truncated" => FakeTerminalHost.Run(["truncated"]),
            "terminal-oversize" => FakeTerminalHost.Run(["oversize"]),
            "terminal-malformed" => FakeTerminalHost.Run(["malformed"]),
            "terminal-exit-1" => FakeTerminalHost.Run(["exit-1"]),
            "version" => Version(),
            "schema" => Schema(22, 1),
            "schema-mismatch" => Schema(20, 1),
            "schema-banner" => BannerThen(() => Schema(22, 1)),
            "helper-version" => HelperVersion(),
            "auth-fail" => AuthFail(),
            "exit-1" => 1,
            _ => UnifiedRpc()
        };
    }

    private static int UnifiedRpc()
    {
        foreach (var line in ReadLines())
        {
            if (!TryGetRequest(line, out var id, out var method))
                continue;
            if (method == "events.subscribe")
            {
                WriteLine("{\"id\":" + id + ",\"result\":{\"ok\":true}}");
                for (var i = 0; i < 3; i++)
                    WriteLine("{\"type\":\"workspace.created\",\"seq\":" + (i + 1) + "}");
                continue;
            }

            WriteLine("{\"id\":" + id + ",\"result\":{\"ok\":true}}");
        }

        return 0;
    }

    private static int BannerBetween()
    {
        var first = true;
        foreach (var line in ReadLines())
        {
            if (!TryGetRequest(line, out var id, out _))
                continue;
            if (first)
            {
                WriteLine("{\"id\":" + id + ",\"result\":{\"ok\":true}}");
                WriteLine(BannerText);
                first = false;
                continue;
            }

            WriteLine("{\"id\":" + id + ",\"result\":{\"ok\":true}}");
        }

        return 0;
    }

    private static int ChunkedRpc()
    {
        foreach (var line in ReadLines())
        {
            if (!TryGetRequest(line, out var id, out _))
                continue;
            var payload = Encoding.UTF8.GetBytes("{\"id\":" + id + ",\"result\":{\"ok\":true}}\n");
            var stdout = Console.OpenStandardOutput();
            foreach (var b in payload)
            {
                stdout.WriteByte(b);
                stdout.Flush();
            }
        }

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

    private static int Truncated()
    {
        var stdout = Console.OpenStandardOutput();
        stdout.Write("{\"id\":1,\"result\":"u8);
        stdout.Flush();
        return 0;
    }

    private static int Hang()
    {
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    private static int Version()
    {
        WriteLine("herdr 0.9.0");
        return 0;
    }

    private static int Schema(int protocol, int schemaVersion)
    {
        WriteLine("{\"protocol\":" + protocol + ",\"schema_version\":" + schemaVersion + "}");
        return 0;
    }

    private static int HelperVersion()
    {
        WriteLine("herddesk-bridge 0.1.0");
        return 0;
    }

    private static int AuthFail()
    {
        WriteStderr("Permission denied (publickey).\n");
        return 255;
    }

    private static int BannerThen(Func<int> next, string? banner = null)
    {
        WriteLine(banner ?? BannerText);
        return next();
    }

    private static int StderrJsonThen(Func<int> next)
    {
        WriteStderr("{\"id\":1,\"result\":{\"secret\":\"" + StderrJsonCanary + "\"}}\n");
        return next();
    }

    private static int StderrFloodThen(Func<int> next)
    {
        var junk = Encoding.UTF8.GetBytes(new string('x', 4096) + "\n");
        var stderr = Console.OpenStandardError();
        for (var i = 0; i < 256; i++)
            stderr.Write(junk, 0, junk.Length);
        stderr.Flush();
        return next();
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

    private static bool TryGetRequest(byte[] line, out ulong id, out string method)
    {
        id = 0;
        method = "";
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("id", out var idElement))
                return false;
            if (idElement.ValueKind == JsonValueKind.Number)
            {
                if (!idElement.TryGetUInt64(out id))
                    return false;
            }
            else if (!ulong.TryParse(idElement.GetString(), out id))
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("method", out var methodElement))
                method = methodElement.GetString() ?? "";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void WriteLine(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\n");
        var stdout = Console.OpenStandardOutput();
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
    }

    private static void WriteStderr(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var stderr = Console.OpenStandardError();
        stderr.Write(bytes, 0, bytes.Length);
        stderr.Flush();
    }
}
