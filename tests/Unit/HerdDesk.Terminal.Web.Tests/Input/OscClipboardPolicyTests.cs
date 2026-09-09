using HerdDesk.Contracts;
using HerdDesk.Terminal.Web;

internal static class OscClipboardPolicyTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("osc 52 read is denied without snapshot reads", OscReadDenied),
        ("osc 52 write is denied without snapshot reads", OscWriteDenied),
        ("malicious ansi cannot read clipboard or create files", MaliciousAnsi)
    ];

    static void OscReadDenied()
    {
        var reader = new CountingOscReader();
        var bytes = "\u001b]52;c;?\u0007"u8.ToArray();
        var decision = OscClipboardPolicy.Evaluate(bytes, reader);
        WebTestHost.Check(!decision.Allowed);
        WebTestHost.Check(decision.Kind == OscClipboardKind.Read);
        WebTestHost.Check(decision.Code == ClipboardCodes.ClipboardReadDenied);
        WebTestHost.Check(reader.Invocations == 0);
    }

    static void OscWriteDenied()
    {
        var reader = new CountingOscReader();
        var bytes = "\u001b]52;c;SGVsbG8=\u001b\\"u8.ToArray();
        var decision = OscClipboardPolicy.Evaluate(bytes, reader);
        WebTestHost.Check(!decision.Allowed);
        WebTestHost.Check(decision.Kind == OscClipboardKind.Write);
        WebTestHost.Check(decision.Code == ClipboardCodes.ClipboardWriteDenied);
        WebTestHost.Check(reader.Invocations == 0);
    }

    static void MaliciousAnsi()
    {
        var reader = new CountingOscReader();
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd031-osc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var marker = Path.Combine(root, "owned.txt");
            File.WriteAllText(marker, "keep");
            var bytes = "\u001b[31m\u001b]52;c;?\u0007\u001b[0m"u8.ToArray();
            var decision = OscClipboardPolicy.Evaluate(bytes, reader);
            WebTestHost.Check(!decision.Allowed);
            WebTestHost.Check(decision.Code == ClipboardCodes.ClipboardReadDenied);
            WebTestHost.Check(reader.Invocations == 0);
            WebTestHost.Check(File.Exists(marker));
            WebTestHost.Check(Directory.GetFiles(root).Length == 1);
            var eightBit = new byte[] { 0x9D, (byte)'5', (byte)'2', (byte)';', (byte)'c', (byte)';', (byte)'A', 0x9C };
            var write = OscClipboardPolicy.Evaluate(eightBit, reader);
            WebTestHost.Check(write.Code == ClipboardCodes.ClipboardWriteDenied);
            WebTestHost.Check(reader.Invocations == 0);
            WebTestHost.Check(Directory.GetFiles(root).Length == 1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

internal sealed class CountingOscReader : IClipboardSnapshotReader
{
    public int Invocations { get; private set; }

    public ClipboardSnapshot Read()
    {
        Invocations++;
        throw new InvalidOperationException("clipboard_reader_must_not_run");
    }
}
