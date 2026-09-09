namespace HerdDesk.Core;

public static class KeepBothNames
{
    public const int MaxAttempts = 1000;

    public static byte[] Candidate(ReadOnlySpan<byte> name, int attempt)
    {
        if (attempt <= 0)
            return name.ToArray();
        var suffix = System.Text.Encoding.ASCII.GetBytes(" (" + attempt.ToString() + ")");
        var dot = -1;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            if (name[i] == (byte)'.')
            {
                dot = i;
                break;
            }
        }

        if (dot <= 0)
        {
            var plain = new byte[name.Length + suffix.Length];
            name.CopyTo(plain);
            suffix.CopyTo(plain.AsSpan(name.Length));
            return plain;
        }

        var sized = new byte[name.Length + suffix.Length];
        name[..dot].CopyTo(sized);
        suffix.CopyTo(sized.AsSpan(dot));
        name[dot..].CopyTo(sized.AsSpan(dot + suffix.Length));
        return sized;
    }
}
