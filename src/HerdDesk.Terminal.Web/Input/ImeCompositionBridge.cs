namespace HerdDesk.Terminal.Web;

/// <summary>
/// Composition lifecycle. Preedit stays local. A commit token is accepted once.
/// </summary>
public sealed class ImeCompositionBridge
{
    private readonly HashSet<string> consumed = new(StringComparer.Ordinal);
    private int serial;
    private string? activeToken;

    public bool IsComposing { get; private set; }
    public bool HasPreedit { get; private set; }
    public string? ActiveToken => activeToken;

    public string Start(string? token = null)
    {
        if (activeToken is { } previous)
            consumed.Add(previous);
        IsComposing = true;
        HasPreedit = false;
        serial++;
        if (IsUsable(token))
            activeToken = token;
        else
            activeToken = "c" + serial.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return activeToken!;
    }

    public void Update()
    {
        if (!IsComposing)
            return;
        HasPreedit = true;
    }

    public bool TryConsume(string token, out bool duplicate)
    {
        duplicate = false;
        if (string.IsNullOrEmpty(token))
            return false;
        if (consumed.Contains(token))
        {
            duplicate = true;
            if (activeToken == token)
                ClearActive();
            return false;
        }

        if (activeToken != token)
            return false;

        consumed.Add(token);
        ClearActive();
        return true;
    }

    public void Cancel()
    {
        if (activeToken is { } token)
            consumed.Add(token);
        ClearActive();
    }

    public void Suspend() => Cancel();

    private bool IsUsable(string? token) =>
        !string.IsNullOrEmpty(token) &&
        token.Length <= 64 &&
        !consumed.Contains(token) &&
        IsStable(token);

    private static bool IsStable(string token)
    {
        if (token[0] is < 'a' or > 'z')
            return false;
        foreach (var ch in token)
        {
            if (ch is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_')
                return false;
        }

        return true;
    }

    private void ClearActive()
    {
        IsComposing = false;
        HasPreedit = false;
        activeToken = null;
    }
}
