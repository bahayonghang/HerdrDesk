using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public static class KeySequenceTranslator
{
    public static HostInputResult Translate(PhysicalKeyEvent key, AgentInputProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrEmpty(key.Key))
            return HostInputResult.Deny(HostInputCodes.UnsupportedKeyProfile);

        if (IsCopyShortcut(key))
            return HostInputResult.Local(HostInputCodes.Allowed, copy: true);

        if (key.Key == "Enter" && key.Shift)
            return HostInputResult.Deny(HostInputCodes.UnsupportedKeyProfile);

        if (key.AnyCtrl && !key.Shift && !key.AltGr && !key.AnyAlt)
        {
            var ctrl = ControlByte(key.Key);
            if (ctrl is { } value)
                return HostInputResult.Accept(HostInputCodes.Allowed, [value], InputOrigin.UserKey);
        }

        if (key.AnyCtrl && !key.AltGr)
            return HostInputResult.Deny(HostInputCodes.UnsupportedKeyProfile);

        var sequence = NavigationBytes(key.Key);
        if (sequence is not null)
            return HostInputResult.Accept(HostInputCodes.Allowed, sequence, InputOrigin.UserKey);

        if (key.Key.Length == 1 && !key.AnyCtrl)
        {
            var text = key.Key;
            if (key.CapsLock && char.IsLetter(text[0]) && !key.Shift)
                text = text.ToUpperInvariant();
            var bytes = Encoding.UTF8.GetBytes(text);
            return HostInputResult.Accept(HostInputCodes.Allowed, bytes, InputOrigin.UserKey);
        }

        return HostInputResult.Deny(HostInputCodes.UnsupportedKeyProfile);
    }

    public static bool IsSearchShortcut(PhysicalKeyEvent key) =>
        key.AnyCtrl && !key.Shift && !key.AnyAlt &&
        string.Equals(key.Key, "k", StringComparison.OrdinalIgnoreCase);

    public static bool IsImeShortcut(PhysicalKeyEvent key) =>
        key.Key is "Enter" or "Escape" or " " or "Space" || IsSearchShortcut(key);

    public static bool IsCopyShortcut(PhysicalKeyEvent key) =>
        key.AnyCtrl && key.Shift && !key.AnyAlt &&
        string.Equals(key.Key, "c", StringComparison.OrdinalIgnoreCase);

    public static bool IsPasteShortcut(PhysicalKeyEvent key) =>
        key.AnyCtrl && key.Shift && !key.AnyAlt &&
        string.Equals(key.Key, "v", StringComparison.OrdinalIgnoreCase);

    public static string RedactedName(PhysicalKeyEvent key)
    {
        var parts = new List<string>(4);
        if (key.AnyCtrl)
            parts.Add("Ctrl");
        if (key.Shift)
            parts.Add("Shift");
        if (key.AnyAlt || key.AltGr)
            parts.Add(key.AltGr ? "AltGr" : "Alt");
        parts.Add(string.IsNullOrEmpty(key.Key) ? "Key" : key.Key);
        return string.Join("+", parts);
    }

    private static byte? ControlByte(string key)
    {
        if (key.Length != 1)
            return null;
        var ch = char.ToLowerInvariant(key[0]);
        if (ch is < 'a' or > 'z')
            return null;
        return (byte)(ch - 'a' + 1);
    }

    private static byte[]? NavigationBytes(string key) => key switch
    {
        "Enter" => [0x0d],
        "Escape" => [0x1b],
        "Tab" => [0x09],
        "Backspace" => [0x7f],
        "ArrowUp" => [0x1b, (byte)'[', (byte)'A'],
        "ArrowDown" => [0x1b, (byte)'[', (byte)'B'],
        "ArrowRight" => [0x1b, (byte)'[', (byte)'C'],
        "ArrowLeft" => [0x1b, (byte)'[', (byte)'D'],
        "PageUp" => [0x1b, (byte)'[', (byte)'5', (byte)'~'],
        "PageDown" => [0x1b, (byte)'[', (byte)'6', (byte)'~'],
        "Home" => [0x1b, (byte)'[', (byte)'H'],
        "End" => [0x1b, (byte)'[', (byte)'F'],
        _ => null
    };
}
