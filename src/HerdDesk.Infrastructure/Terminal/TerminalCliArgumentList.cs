using System.Globalization;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Terminal;

internal static class TerminalCliArgumentList
{
    public static IReadOnlyList<string> Build(TerminalOpenRequest request)
    {
        var arguments = new List<string>();
        if (!string.IsNullOrEmpty(request.SessionName))
        {
            arguments.Add("--session");
            arguments.Add(request.SessionName);
        }

        arguments.Add("terminal");
        arguments.Add("session");
        arguments.Add(request.Mode == TerminalMode.Observe ? "observe" : "control");
        arguments.Add(request.Target);
        arguments.Add("--cols");
        arguments.Add(request.Columns.ToString(CultureInfo.InvariantCulture));
        arguments.Add("--rows");
        arguments.Add(request.Rows.ToString(CultureInfo.InvariantCulture));
        return arguments;
    }
}
