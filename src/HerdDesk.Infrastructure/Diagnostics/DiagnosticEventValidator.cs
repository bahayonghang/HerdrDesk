using System.Text.RegularExpressions;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Diagnostics;

public static class DiagnosticEventValidator
{
    private static readonly Regex Token = new("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex ErrorCode = new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex Alias = new("^[a-z]+-[0-9a-f]{12}$", RegexOptions.CultureInvariant);

    public static bool IsValid(DiagnosticEvent evt)
    {
        if (evt is null)
            return false;
        if (evt.UtcTimestamp.Offset != TimeSpan.Zero)
            return false;
        if (!Token.IsMatch(evt.Component) || !Token.IsMatch(evt.Operation))
            return false;
        if (evt.ErrorCode is not null && !ErrorCode.IsMatch(evt.ErrorCode))
            return false;
        if (evt.DeviceAlias is not null && !Alias.IsMatch(evt.DeviceAlias))
            return false;
        if (evt.SessionAlias is not null && !Alias.IsMatch(evt.SessionAlias))
            return false;
        if (LooksSensitive(evt.Component) || LooksSensitive(evt.Operation) ||
            LooksSensitive(evt.ErrorCode) || LooksSensitive(evt.DeviceAlias) ||
            LooksSensitive(evt.SessionAlias))
            return false;
        if (evt.Epoch is <= 0)
            return false;
        if (evt.DurationMs is < 0)
            return false;
        if (evt.QueueBytes is < 0)
            return false;
        return true;
    }

    internal static bool LooksSensitive(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        if (value.IndexOfAny(['\\', '/', '\0', '\u001b']) >= 0)
            return true;
        var lower = value.ToLowerInvariant();
        return lower.Contains("password", StringComparison.Ordinal) ||
               lower.Contains("token", StringComparison.Ordinal) ||
               lower.Contains("secret", StringComparison.Ordinal) ||
               lower.Contains("private", StringComparison.Ordinal) ||
               lower.Contains("begin ", StringComparison.Ordinal) ||
               lower.Contains("terminal.input", StringComparison.Ordinal);
    }
}
