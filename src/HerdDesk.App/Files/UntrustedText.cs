using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.App;

public static class UntrustedText
{
    public static string Display(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        var chars = raw.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsControl(c) && c != '\t')
                chars[i] = '\uFFFD';
        }

        return new string(chars);
    }

    public static string Display(ReadOnlySpan<byte> utf8) =>
        Display(Encoding.UTF8.GetString(utf8));

    public static FileLocator? TryAsPath(string? display)
    {
        _ = display;
        return null;
    }

    public static bool TryAsCommand(string? display)
    {
        _ = display;
        return false;
    }

    public static bool TryAsUri(string? display)
    {
        _ = display;
        return false;
    }

    public static bool TryAsNavigation(string? display)
    {
        _ = display;
        return false;
    }
}
