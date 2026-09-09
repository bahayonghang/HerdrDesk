namespace HerdDesk.Infrastructure.Files;

public static class WindowsLocalNameMapping
{
    static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "CONIN$", "CONOUT$"
    ];

    public static bool MappingRequired(string localFileName)
    {
        if (string.IsNullOrEmpty(localFileName))
            return true;
        if (localFileName.EndsWith(' ') || localFileName.EndsWith('.'))
            return true;
        foreach (var ch in localFileName)
        {
            if (ch < 32 || ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
                return true;
        }
        var stem = localFileName;
        var dot = localFileName.IndexOf('.');
        if (dot >= 0)
            stem = localFileName[..dot];
        foreach (var name in Reserved)
        {
            if (stem.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                localFileName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
