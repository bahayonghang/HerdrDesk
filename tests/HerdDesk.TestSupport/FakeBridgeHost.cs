using System.Text;
using System.Text.Json;

internal static class FakeBridgeHost
{
    public static int Run(string[] args)
    {
        var mode = args.Length == 0 ? "rpc" : args[0];
        return mode switch
        {
            "echo" => Echo(),
            "rpc" => Rpc(),
            "subscribe" => Subscribe(3),
            "subscribe-overflow" => Subscribe(64),
            "stderr-flood" => StderrFlood(),
            "banner" => Banner(),
            "hang" => Hang(),
            "late-id" => LateId(),
            "unknown-then-ok" => UnknownThenOk(),
            "error-and-result" => ErrorAndResult(),
            _ => Rpc()
        };
    }

    private static int Echo()
    {
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = stdin.Read(buffer, 0, buffer.Length);
            if (read == 0)
                return 0;
            stdout.Write(buffer, 0, read);
            stdout.Flush();
        }
    }

    private static int Rpc()
    {
        foreach (var line in ReadLines())
        {
            if (!TryGetId(line, out var id))
                continue;
            WriteLine("{\"id\":" + id + ",\"result\":{\"ok\":true}}");
        }
        return 0;
    }

    private static int Subscribe(int events)
    {
        var first = true;
        foreach (var line in ReadLines())
        {
            if (!TryGetId(line, out var id))
                continue;
            if (first)
            {
                WriteLine("{\"id\":" + id + ",\"result\":{\"ok\":true}}");
                first = false;
                for (var i = 0; i < events; i++)
                    WriteLine("{\"type\":\"workspace.created\",\"seq\":" + (i + 1) + "}");
            }
        }
        return 0;
    }

    private static int StderrFlood()
    {
        var junk = Encoding.UTF8.GetBytes(new string('x', 4096) + "\n");
        for (var i = 0; i < 256; i++)
            Console.OpenStandardError().Write(junk, 0, junk.Length);
        return Rpc();
    }

    private static int Banner()
    {
        WriteLine("WELCOME TO THE BRIDGE");
        return Rpc();
    }

    private static int Hang()
    {
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    private static int LateId()
    {
        foreach (var _ in ReadLines())
            WriteLine("""{"id":999999,"result":{"ok":true}}""");
        return 0;
    }

    private static int UnknownThenOk()
    {
        foreach (var line in ReadLines())
        {
            if (!TryGetId(line, out var id))
                continue;
            WriteLine("""{"id":999999,"result":{"ok":true}}""");
            WriteLine("{\"id\":" + id + ",\"result\":{\"ok\":true}}");
        }
        return 0;
    }

    private static int ErrorAndResult()
    {
        foreach (var line in ReadLines())
        {
            if (!TryGetId(line, out var id))
                continue;
            WriteLine("{\"id\":" + id + ",\"result\":{\"ok\":true},\"error\":{\"code\":\"nope\"}}");
        }
        return 0;
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

    private static bool TryGetId(byte[] line, out ulong id)
    {
        id = 0;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("id", out var element))
                return false;
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetUInt64(out id);
            return ulong.TryParse(element.GetString(), out id);
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
}
