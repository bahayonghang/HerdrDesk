using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public sealed class SelectionAndMousePolicy
{
    public SelectionSnapshot Selection { get; private set; } = new("", false);
    public bool ApplicationMouseMode { get; private set; }
    public int LocalScrollCount { get; private set; }
    public int UpstreamScrollCount { get; private set; }
    public string? ReadOnlyScrollNotice { get; private set; }

    public HostInputResult DragSelect(string visibleText, bool shift)
    {
        if (shift)
        {
            ApplicationMouseMode = true;
            return HostInputResult.Local(HostInputCodes.Allowed);
        }

        ApplicationMouseMode = false;
        var text = VisibleText.StripAnsi(visibleText ?? "");
        Selection = new SelectionSnapshot(text, text.Length > 0);
        return HostInputResult.Local(HostInputCodes.Allowed, selection: true);
    }

    public HostInputResult Copy()
    {
        if (!Selection.HasSelection)
            return HostInputResult.Deny(HostInputCodes.InputBytesLimit);
        return HostInputResult.Local(HostInputCodes.Allowed, copy: true);
    }

    public HostInputResult Scroll(int delta, InputContext context, bool mouseReporting, bool writeAllowed = true)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = delta;
        if (!writeAllowed ||
            context.Access != TerminalAccess.Controlling ||
            !context.ControlVerified)
        {
            LocalScrollCount++;
            ReadOnlyScrollNotice = "只读，滚动由当前控制者决定";
            return HostInputResult.Deny(HostInputCodes.ObserveScrollDenied);
        }

        if (!mouseReporting)
        {
            LocalScrollCount++;
            return HostInputResult.Local(HostInputCodes.Allowed, scroll: true);
        }

        UpstreamScrollCount++;
        ReadOnlyScrollNotice = null;
        return HostInputResult.Local(HostInputCodes.Allowed, scroll: true);
    }

    public void Clear()
    {
        Selection = new SelectionSnapshot("", false);
        ApplicationMouseMode = false;
    }
}

internal static class VisibleText
{
    public static string StripAnsi(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\u001b')
            {
                i++;
                if (i >= value.Length)
                    break;
                if (value[i] == '[')
                {
                    i++;
                    while (i < value.Length && !IsCsiFinal(value[i]))
                        i++;
                    continue;
                }

                if (value[i] == ']')
                {
                    i++;
                    while (i < value.Length)
                    {
                        if (value[i] == '\u0007')
                            break;
                        if (value[i] == '\u001b' && i + 1 < value.Length && value[i + 1] == '\\')
                        {
                            i++;
                            break;
                        }

                        i++;
                    }

                    continue;
                }

                continue;
            }

            if (ch < 32 && ch is not '\t' and not '\n' and not '\r')
                continue;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool IsCsiFinal(char ch) => ch is >= '@' and <= '~';
}
