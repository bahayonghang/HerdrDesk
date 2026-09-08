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

    public string Start()
    {
        if (activeToken is { } previous)
            consumed.Add(previous);
        IsComposing = true;
        HasPreedit = false;
        serial++;
        activeToken = "c" + serial.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return activeToken;
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

    private void ClearActive()
    {
        IsComposing = false;
        HasPreedit = false;
        activeToken = null;
    }
}
