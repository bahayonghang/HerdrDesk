using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.SshTransports;

internal static class PosixShellQuote
{
    public static bool TryCommand(IReadOnlyList<string> tokens, out string command, out string code)
    {
        command = "";
        code = SshCodes.ProfileInvalid;
        if (tokens is null || tokens.Count == 0)
            return false;

        var parts = new string[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrEmpty(token) ||
                token.IndexOf('\0') >= 0 ||
                token.IndexOf('\r') >= 0 ||
                token.IndexOf('\n') >= 0)
                return false;
            parts[i] = Quote(token);
        }

        command = string.Join(' ', parts);
        code = SshCodes.Ok;
        return true;
    }

    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            return "''";
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }
}
