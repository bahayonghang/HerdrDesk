using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class SettingsViewModel
{
    private readonly IDeviceProfileStore _profiles;
    private readonly UiPreferenceStore? _ui;
    private readonly IConfigurationOwnership _ownership;
    private readonly TerminalDisplayCoordinator _display;

    public SettingsViewModel(
        IDeviceProfileStore profiles,
        TerminalDisplayCoordinator display,
        IConfigurationOwnership? ownership = null,
        UiPreferenceStore? ui = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(display);
        _profiles = profiles;
        _display = display;
        _ownership = ownership ?? ConfigurationOwnership.Owner;
        _ui = ui;
        SshAvailability = new RouteAvailability(
            RouteAvailabilityKind.Disabled, ShellCodes.SshProviderPending, ShellStrings.SshPending);
        Draft = new LocalDeviceDraft(new DeviceId(Guid.Empty), "", "", []);
        UiDraft = UiPreferences.Default;
    }

    public SettingsLifecycle Lifecycle { get; private set; } = SettingsLifecycle.Loading;
    public string? ErrorCode { get; private set; }
    public LocalDeviceDraft Draft { get; private set; }
    public DeviceProfile? CommittedDevice { get; private set; }
    public ConfigurationSnapshot CommittedSnapshot { get; private set; } = ConfigurationSnapshot.Empty;
    public UiPreferences UiDraft { get; private set; }
    public UiPreferences CommittedUi { get; private set; } = UiPreferences.Default;
    public RouteAvailability SshAvailability { get; }
    public IReadOnlyList<SessionKey> PendingConnects { get; private set; } = [];
    public int SaveCalls { get; private set; }
    public TerminalDisplayCoordinator Display => _display;

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        Lifecycle = SettingsLifecycle.Loading;
        var loaded = await _profiles.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Code is ConfigurationCodes.Missing)
        {
            CommittedSnapshot = ConfigurationSnapshot.Empty;
            CommittedDevice = null;
            Draft = new LocalDeviceDraft(new DeviceId(Guid.Empty), "", "", []);
            Lifecycle = SettingsLifecycle.Ready;
            ErrorCode = null;
        }
        else if (!loaded.Succeeded)
        {
            CommittedSnapshot = ConfigurationSnapshot.Empty;
            ErrorCode = loaded.Code;
            Lifecycle = SettingsLifecycle.Ready;
        }
        else
        {
            CommittedSnapshot = loaded.Snapshot!;
            CommittedDevice = CommittedSnapshot.Devices.FirstOrDefault();
            Draft = CommittedDevice is null
                ? new LocalDeviceDraft(new DeviceId(Guid.Empty), "", "", [])
                : ToDraft(CommittedDevice);
            Lifecycle = SettingsLifecycle.Ready;
            ErrorCode = null;
        }

        if (_ui is not null)
        {
            var ui = await _ui.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (ui.Succeeded)
            {
                CommittedUi = ui.Preferences!;
                UiDraft = CommittedUi;
                _display.AcceptCommitted(CommittedUi.Display);
            }
            else if (ui.Code is ConfigurationCodes.Missing)
            {
                CommittedUi = UiPreferences.Default;
                UiDraft = CommittedUi;
            }
            else
            {
                ErrorCode ??= ui.Code;
            }
        }
    }

    public void BeginNewDevice(DeviceId? device = null)
    {
        Draft = new LocalDeviceDraft(device ?? new DeviceId(Guid.NewGuid()), "", "", []);
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void SetDeviceLabel(string label)
    {
        Draft = Draft with { Label = label };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void SetHerdrPath(string path)
    {
        Draft = Draft with { VerifiedHerdrPath = path };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void AddNamedSession(string sessionName)
    {
        Draft = Draft with
        {
            Sessions = [.. Draft.Sessions, new LocalSessionDraft(
                SessionProfileKind.NamedSession, sessionName, null, null)]
        };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void AddExplicitEndpoint(string location, EndpointKind kind, string? sessionName = null)
    {
        Draft = Draft with
        {
            Sessions = [.. Draft.Sessions, new LocalSessionDraft(
                SessionProfileKind.ExplicitEndpoint, sessionName, kind, location)]
        };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void ReplaceSession(int index, LocalSessionDraft session)
    {
        if (index < 0 || index >= Draft.Sessions.Count)
            return;
        var sessions = Draft.Sessions.ToArray();
        sessions[index] = session;
        Draft = Draft with { Sessions = sessions };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public async ValueTask SaveLocalDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (!_ownership.CanWrite)
        {
            Lifecycle = SettingsLifecycle.PermissionDenied;
            ErrorCode = ShellCodes.PermissionDenied;
            RestoreDraftFromCommitted();
            return;
        }

        var profile = TryBuildProfile();
        if (profile is null)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode = ConfigurationCodes.InvalidProfile;
            return;
        }

        Lifecycle = SettingsLifecycle.Saving;
        SaveCalls++;
        var result = await _profiles.SaveDeviceAsync(profile, CommittedSnapshot.Revision, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            Lifecycle = SettingsLifecycle.SaveFailed;
            ErrorCode = result.Code;
            RestoreDraftFromCommitted();
            return;
        }

        CommittedSnapshot = result.Snapshot!;
        CommittedDevice = CommittedSnapshot.Devices.First(item => item.Device == profile.Device);
        Draft = ToDraft(CommittedDevice);
        Lifecycle = SettingsLifecycle.Saved;
        ErrorCode = null;
    }

    public async ValueTask SaveUiPreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (_ui is null)
            return;
        if (!_ownership.CanWrite)
        {
            Lifecycle = SettingsLifecycle.PermissionDenied;
            ErrorCode = ShellCodes.PermissionDenied;
            UiDraft = CommittedUi;
            _display.RestoreCommitted();
            return;
        }

        var error = UiPreferenceStore.Validate(UiDraft);
        if (error is not null)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode = error;
            UiDraft = CommittedUi;
            _display.RestoreCommitted();
            return;
        }

        Lifecycle = SettingsLifecycle.Saving;
        var result = await _ui.SaveAsync(UiDraft, CommittedUi.Revision, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            Lifecycle = SettingsLifecycle.SaveFailed;
            ErrorCode = result.Code;
            UiDraft = CommittedUi;
            _display.RestoreCommitted();
            return;
        }

        CommittedUi = result.Preferences!;
        UiDraft = CommittedUi;
        _display.AcceptCommitted(CommittedUi.Display);
        Lifecycle = SettingsLifecycle.Saved;
        ErrorCode = null;
    }

    public void PreviewDisplay(TerminalDisplayPreferences preferences)
    {
        var code = _display.ApplyPreview(preferences);
        if (code is not null)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode = code;
            return;
        }

        UiDraft = UiDraft with
        {
            FontFamily = preferences.FontFamily,
            FontSize = preferences.FontSize,
            ZoomPercent = preferences.ZoomPercent
        };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void SetTheme(UiThemeKind theme)
    {
        UiDraft = UiDraft with { Theme = theme };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void SetNotifications(bool enabled)
    {
        UiDraft = UiDraft with { NotificationsEnabled = enabled };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void SetDiagnosticPrivacy(bool redact)
    {
        UiDraft = UiDraft with { DiagnosticPrivacyRedact = redact };
        Lifecycle = SettingsLifecycle.Dirty;
    }

    public void Discard()
    {
        RestoreDraftFromCommitted();
        UiDraft = CommittedUi;
        _display.RestoreCommitted();
        Lifecycle = SettingsLifecycle.Ready;
        ErrorCode = null;
    }

    public void RequestConnect(int sessionIndex)
    {
        if (CommittedDevice is null || sessionIndex < 0 || sessionIndex >= CommittedDevice.Sessions.Count)
            return;
        var key = CommittedDevice.Sessions[sessionIndex].ToSessionKey(CommittedDevice.Device);
        PendingConnects = [.. PendingConnects, key];
    }

    private void RestoreDraftFromCommitted()
    {
        Draft = CommittedDevice is null
            ? new LocalDeviceDraft(new DeviceId(Guid.Empty), "", "", [])
            : ToDraft(CommittedDevice);
    }

    private DeviceProfile? TryBuildProfile()
    {
        if (Draft.Device.Value == Guid.Empty)
            return null;
        var sessions = new List<SessionProfile>(Draft.Sessions.Count);
        foreach (var draft in Draft.Sessions)
        {
            var session = ToSession(draft);
            if (session is null)
                return null;
            sessions.Add(session);
        }

        return new DeviceProfile(
            Draft.Device,
            Draft.Label,
            ConnectionKinds.Local,
            Draft.VerifiedHerdrPath,
            sessions);
    }

    private static LocalDeviceDraft ToDraft(DeviceProfile profile) =>
        new(
            profile.Device,
            profile.Label,
            profile.VerifiedHerdrPath,
            profile.Sessions.Select(item => new LocalSessionDraft(
                item.Kind, item.SessionName, item.Endpoint, item.CanonicalLocation)).ToArray());

    private static SessionProfile? ToSession(LocalSessionDraft draft)
    {
        return draft.Kind switch
        {
            SessionProfileKind.LocalDefault => SessionProfile.LocalDefault(),
            SessionProfileKind.NamedSession when !string.IsNullOrWhiteSpace(draft.SessionName) =>
                SessionProfile.Named(draft.SessionName),
            SessionProfileKind.ExplicitEndpoint when draft.Endpoint is { } kind &&
                                                     !string.IsNullOrWhiteSpace(draft.CanonicalLocation) =>
                SessionProfile.Explicit(draft.CanonicalLocation, kind, draft.SessionName),
            _ => null
        };
    }
}
