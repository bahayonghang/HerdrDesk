using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class ConflictDialogViewModel
{
    public ConflictDialogViewModel()
    {
        ReplaceAutomationName = ShellStrings.FileConflictReplace;
        KeepBothAutomationName = ShellStrings.FileConflictKeepBoth;
        CancelAutomationName = ShellStrings.FileConflictCancel;
        ApplyAllAutomationName = ShellStrings.FileConflictApplyAll;
    }

    public ConflictPrompt? Current { get; private set; }
    public bool IsOpen { get; private set; }
    public bool ApplyAll { get; private set; }
    public Guid? DraftScope { get; private set; }
    public string? LastError { get; private set; }
    public string? RestoreFocusTarget { get; private set; }
    public string ReplaceAutomationName { get; }
    public string KeepBothAutomationName { get; }
    public string CancelAutomationName { get; }
    public string ApplyAllAutomationName { get; }

    public string KeepBothCandidateDisplay
    {
        get
        {
            if (Current is null)
                return "";
            return UntrustedText.Display(KeepBothNames.Candidate(Current.Name.Raw, 1));
        }
    }

    public string ExactPathDisplay => Current is null ? "" : UntrustedText.Display(Current.ExactPathDisplay);

    public string ScopeText => Current is null ? "" : Current.ScopeText;

    public void Open(ConflictPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        Current = prompt;
        IsOpen = true;
        ApplyAll = false;
        DraftScope = prompt.DraftId;
        LastError = null;
        RestoreFocusTarget = "conflict-replace";
    }

    public ConflictIntent Submit(ConflictIntent intent, FileObservation? token, bool applyAll = false)
    {
        if (!IsOpen || Current is null)
            return ConflictIntent.Cancel;
        if (intent is ConflictIntent.Replace or ConflictIntent.KeepBoth)
        {
            if (token is null || token.Hex != Current.Token.Hex)
            {
                LastError = FileOpCodes.StaleTarget;
                return ConflictIntent.TokenMismatch;
            }
        }

        ApplyAll = applyAll && DraftScope == Current.DraftId;
        IsOpen = false;
        RestoreFocusTarget = "copy-action";
        var closed = Current;
        Current = null;
        _ = closed;
        return intent == ConflictIntent.TokenMismatch ? ConflictIntent.Cancel : intent;
    }

    public ConflictIntent Dismiss() => Submit(ConflictIntent.Cancel, null);

    public ConflictIntent ReplaceFromKeyboard(FileObservation token, bool applyAll = false) =>
        Submit(ConflictIntent.Replace, token, applyAll);

    public ConflictIntent ReplaceFromScreenReader(FileObservation token, bool applyAll = false) =>
        Submit(ConflictIntent.Replace, token, applyAll);

    public ConflictIntent KeepBothFromKeyboard(FileObservation token, bool applyAll = false) =>
        Submit(ConflictIntent.KeepBoth, token, applyAll);

    public ConflictIntent CancelFromKeyboard() => Dismiss();

    public ConflictIntent CancelFromScreenReader() => Dismiss();

    public static string FormatExactPath(FileLocation location, FileComponent name)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder();
        if (location.Kind == FileLocationKind.Remote)
            builder.Append(location.Device.Value.ToString("D")).Append(' ');
        else
            builder.Append(ShellStrings.FileLocal).Append(' ');
        builder.Append(UntrustedText.Display(location.ProviderId));
        foreach (var component in location.Path.Components)
        {
            builder.Append(" / ");
            builder.Append(UntrustedText.Display(component.Raw));
        }

        builder.Append(" / ");
        builder.Append(UntrustedText.Display(name.Raw));
        return builder.ToString();
    }
}
