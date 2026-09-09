using HerdDesk.Infrastructure.Files;

internal static class WindowsLocalNameMappingTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("windows reserved names require mapping", ReservedNames),
        ("windows trailing dot and space require mapping", Trailing),
        ("windows illegal characters require mapping", IllegalChars),
        ("ordinary unicode name is allowed", OrdinaryAllowed)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void ReservedNames()
    {
        foreach (var name in new[]
                 {
                     "CON", "con", "CON.txt", "Prn.log", "AUX", "NUL", "COM1", "COM9.dat", "LPT1",
                     "lpt9", "CONIN$", "CONOUT$"
                 })
            Check(WindowsLocalNameMapping.MappingRequired(name));
        Check(!WindowsLocalNameMapping.MappingRequired("COM10"));
        Check(!WindowsLocalNameMapping.MappingRequired("console.txt"));
    }

    static void Trailing()
    {
        Check(WindowsLocalNameMapping.MappingRequired("readme."));
        Check(WindowsLocalNameMapping.MappingRequired("readme "));
        Check(WindowsLocalNameMapping.MappingRequired(""));
    }

    static void IllegalChars()
    {
        Check(WindowsLocalNameMapping.MappingRequired("a<b"));
        Check(WindowsLocalNameMapping.MappingRequired("a:b"));
        Check(WindowsLocalNameMapping.MappingRequired("a|b"));
        Check(WindowsLocalNameMapping.MappingRequired("a*b"));
        Check(WindowsLocalNameMapping.MappingRequired("a?b"));
        Check(WindowsLocalNameMapping.MappingRequired("a\"b"));
        Check(WindowsLocalNameMapping.MappingRequired("a\\b"));
        Check(WindowsLocalNameMapping.MappingRequired("a/b"));
        Check(WindowsLocalNameMapping.MappingRequired("a\nb"));
    }

    static void OrdinaryAllowed()
    {
        Check(!WindowsLocalNameMapping.MappingRequired("hello world.txt"));
        Check(!WindowsLocalNameMapping.MappingRequired("你好.txt"));
        Check(!WindowsLocalNameMapping.MappingRequired("file (1).txt"));
    }
}
