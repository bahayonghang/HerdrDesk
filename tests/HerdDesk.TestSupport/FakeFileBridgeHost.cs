using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

internal static class FakeFileBridgeHost
{
    public static int Run(string[] args)
    {
        var mode = args.Length == 0 ? "eof" : args[0];
        return mode switch
        {
            "pollution" => Pollution(),
            "eof" => EofWithoutComplete(),
            _ => EofWithoutComplete()
        };
    }

    static int Pollution()
    {
        var stdout = Console.OpenStandardOutput();
        stdout.Write("NOT-HDFB-POLLUTION"u8);
        stdout.Flush();
        return 0;
    }

    static int EofWithoutComplete()
    {
        var stdin = Console.OpenStandardInput();
        var header = new byte[16];
        var got = ReadExact(stdin, header);
        if (got < 16)
            return 1;
        var len = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
        var payload = new byte[len];
        if (ReadExact(stdin, payload) < payload.Length)
            return 1;
        using var doc = JsonDocument.Parse(payload);
        var job = doc.RootElement.GetProperty("job").GetString() ?? "";
        var op = doc.RootElement.GetProperty("op").GetString() ?? "list";
        var body = "{\"job\":\"" + job + "\",\"op\":\"" + op + "\",\"identity\":\"YQ==\",\"size\":\"0\"}";
        var bytes = Encoding.UTF8.GetBytes(body);
        var frame = new byte[16 + bytes.Length];
        "HDFB"u8.CopyTo(frame);
        frame[4] = 1;
        frame[5] = 0;
        frame[6] = 0x11;
        frame[7] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(12, 4), 0);
        bytes.CopyTo(frame.AsSpan(16));
        var stdout = Console.OpenStandardOutput();
        stdout.Write(frame);
        stdout.Flush();
        return 0;
    }

    static int ReadExact(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = stream.Read(buffer, offset, buffer.Length - offset);
            if (n == 0)
                return offset;
            offset += n;
        }
        return offset;
    }
}
