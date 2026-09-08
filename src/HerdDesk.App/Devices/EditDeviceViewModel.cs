using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.App;

public sealed record HostKeyReviewState(
    HostKeyStatus Status,
    string FingerprintSha256,
    string KeyType,
    int Port,
    string Prompt,
    bool CanConfirm,
    bool IsHardBlocked);

public sealed record SshDeviceDraft(
    DeviceId Device,
    string Label,
    string VerifiedHerdrPath,
    string HostAlias,
    string? User,
    string? PortText,
    string? IdentityFilePath,
    string? IdentityAgent,
    string? ProxyJumpAlias,
    string RemoteHerdrPath,
    string? RemoteHelperPath,
    string AuthModeRaw,
    IReadOnlyList<LocalSessionDraft> Sessions);

public sealed class EditDeviceViewModel
{
    private readonly IDeviceProfileStore _profiles;
    private readonly ISshConnectionTester _tester;
    private readonly ISshConfigPreview _preview;
    private CancellationTokenSource? _test;

    public EditDeviceViewModel(
        IDeviceProfileStore profiles,
        ISshConnectionTester tester,
        ISshConfigPreview? preview = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(tester);
        _profiles = profiles;
        _tester = tester;
        _preview = preview ?? tester as ISshConfigPreview ?? new UnavailableSshPreview();
        Draft = EmptyDraft(new DeviceId(Guid.Empty));
        Unsupported = SshRedaction.UnsupportedMatrix;
    }

    public SettingsLifecycle Lifecycle { get; private set; } = SettingsLifecycle.Loading;
    public string? ErrorCode { get; private set; }
    public SshDeviceDraft Draft { get; private set; }
    public DeviceProfile? CommittedDevice { get; private set; }
    public ConfigurationSnapshot CommittedSnapshot { get; private set; } = ConfigurationSnapshot.Empty;
    public SshResolvedConfiguration? Preview { get; private set; }
    public SshResolvedConfiguration? PreviewDisplay { get; private set; }
    public SshConnectionTestResult? LastTest { get; private set; }
    public HostKeyReviewState? HostKeyReview { get; private set; }
    public IReadOnlyList<SshUnsupportedCapability> Unsupported { get; }
    public int SaveCalls { get; private set; }
    public int TestCalls { get; private set; }

    public async ValueTask LoadAsync(CancellationToken cancellationToken = default)
    {
        Lifecycle = SettingsLifecycle.Loading;
        var loaded = await _profiles.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.Code is ConfigurationCodes.Missing)
        {
            CommittedSnapshot = ConfigurationSnapshot.Empty;
            CommittedDevice = null;
            Draft = EmptyDraft(new DeviceId(Guid.Empty));
            Lifecycle = SettingsLifecycle.Ready;
            ErrorCode = null;
            return;
        }

        if (!loaded.Succeeded)
        {
            CommittedSnapshot = ConfigurationSnapshot.Empty;
            ErrorCode = loaded.Code;
            Lifecycle = SettingsLifecycle.Ready;
            return;
        }

        CommittedSnapshot = loaded.Snapshot!;
        CommittedDevice = CommittedSnapshot.Devices.FirstOrDefault(item => item.IsSshConnection)
                          ?? CommittedSnapshot.Devices.FirstOrDefault();
        Draft = CommittedDevice is null ? EmptyDraft(new DeviceId(Guid.Empty)) : ToDraft(CommittedDevice);
        Lifecycle = SettingsLifecycle.Ready;
        ErrorCode = null;
    }

    public void BeginNewDevice(DeviceId? device = null)
    {
        Draft = EmptyDraft(device ?? new DeviceId(Guid.NewGuid()));
        Lifecycle = SettingsLifecycle.Dirty;
        ErrorCode = null;
        Preview = null;
        PreviewDisplay = null;
        LastTest = null;
        HostKeyReview = null;
    }

    public void SetLabel(string label) => Mutate(draft => draft with { Label = label });
    public void SetHerdrPath(string path) => Mutate(draft => draft with { VerifiedHerdrPath = path });
    public void SetHostAlias(string alias) => Mutate(draft => draft with { HostAlias = alias });
    public void SetUser(string? user) => Mutate(draft => draft with { User = user });
    public void SetPortText(string? port) => Mutate(draft => draft with { PortText = port });
    public void SetIdentityFilePath(string? path) => Mutate(draft => draft with { IdentityFilePath = path });
    public void SetIdentityAgent(string? agent) => Mutate(draft => draft with { IdentityAgent = agent });
    public void SetProxyJumpAlias(string? alias) => Mutate(draft => draft with { ProxyJumpAlias = alias });
    public void SetRemoteHerdrPath(string path) => Mutate(draft => draft with { RemoteHerdrPath = path });
    public void SetRemoteHelperPath(string? path) => Mutate(draft => draft with { RemoteHelperPath = path });
    public void SetAuthModeRaw(string raw) => Mutate(draft => draft with { AuthModeRaw = raw });

    public void AddNamedSession(string sessionName) =>
        Mutate(draft => draft with
        {
            Sessions = [.. draft.Sessions, new LocalSessionDraft(
                SessionProfileKind.NamedSession, sessionName, null, null)]
        });

    public void AddExplicitEndpoint(string location, EndpointKind kind, string? sessionName = null) =>
        Mutate(draft => draft with
        {
            Sessions = [.. draft.Sessions, new LocalSessionDraft(
                SessionProfileKind.ExplicitEndpoint, sessionName, kind, location)]
        });

    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        var profile = TryBuildProfile();
        if (profile is null)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode ??= ConfigurationCodes.InvalidSshProfile;
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
            RestoreDraft();
            return;
        }

        CommittedSnapshot = result.Snapshot!;
        CommittedDevice = CommittedSnapshot.Devices.First(item => item.Device == profile.Device);
        Draft = ToDraft(CommittedDevice);
        Lifecycle = SettingsLifecycle.Saved;
        ErrorCode = null;
    }

    public async ValueTask PreviewAsync(CancellationToken cancellationToken = default)
    {
        var settings = TryBuildSettings();
        if (settings is null)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode ??= SshCodes.ProfileInvalid;
            Preview = null;
            PreviewDisplay = null;
            return;
        }

        var result = await _preview.PreviewAsync(settings, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode = result.Code;
            Preview = null;
            PreviewDisplay = null;
            return;
        }

        Preview = result.Configuration;
        PreviewDisplay = Redact(result.Configuration!);
        Lifecycle = SettingsLifecycle.Ready;
        ErrorCode = null;
    }

    public async ValueTask TestUntilHostKeyAsync(CancellationToken cancellationToken = default)
    {
        var settings = TryBuildSettings();
        if (settings is null)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode ??= SshCodes.ProfileInvalid;
            return;
        }

        if (settings.AuthMode.Known is null)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode = SshCodes.AuthUnsupported;
            return;
        }

        var run = await ReplaceTestAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Lifecycle = SettingsLifecycle.Testing;
            TestCalls++;
            var result = await _tester.TestUntilHostKeyAsync(settings, run.Token).ConfigureAwait(false);
            AcceptTest(result);
        }
        finally
        {
            CompleteTest(run);
        }
    }

    public async ValueTask ConfirmUnknownHostAsync(CancellationToken cancellationToken = default)
    {
        if (HostKeyReview is not { CanConfirm: true } || LastTest?.HostKey?.Candidate is not { } candidate)
        {
            ErrorCode = SshCodes.HostKeyUnknown;
            return;
        }

        var result = await _tester.ConfirmUnknownHostAsync(candidate, cancellationToken).ConfigureAwait(false);
        AcceptTest(result);
    }

    public void CancelHostKeyReview()
    {
        HostKeyReview = null;
        if (LastTest?.HostKey?.Status == HostKeyStatus.UnknownCandidate)
        {
            LastTest = LastTest with
            {
                Phase = SshConnectionTestPhase.Cancelled,
                Code = SshCodes.TestCancelled
            };
            Lifecycle = SettingsLifecycle.Ready;
            ErrorCode = SshCodes.TestCancelled;
        }
    }

    public async ValueTask AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var settings = TryBuildSettings();
        if (settings is null)
        {
            Lifecycle = SettingsLifecycle.ValidationFailed;
            ErrorCode ??= SshCodes.ProfileInvalid;
            return;
        }

        if (HostKeyReview is { IsHardBlocked: true })
        {
            ErrorCode = SshCodes.HostKeyChanged;
            Lifecycle = SettingsLifecycle.ValidationFailed;
            return;
        }

        var run = await ReplaceTestAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Lifecycle = SettingsLifecycle.Testing;
            TestCalls++;
            var result = await _tester.AuthenticateAsync(settings, run.Token).ConfigureAwait(false);
            AcceptTest(result);
        }
        finally
        {
            CompleteTest(run);
        }
    }

    public async ValueTask CancelTestAsync()
    {
        var current = _test;
        if (current is not null)
        {
            try
            {
                await current.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        var result = await _tester.CancelAsync().ConfigureAwait(false);
        if (current is null)
            return;
        AcceptTest(result);
    }

    private void AcceptTest(SshConnectionTestResult result)
    {
        if (_test is { IsCancellationRequested: true } &&
            result.Phase != SshConnectionTestPhase.Cancelled)
            return;

        LastTest = result;
        Preview = result.Preview ?? Preview;
        PreviewDisplay = Preview is null ? PreviewDisplay : Redact(Preview);
        HostKeyReview = ToReview(result.HostKey);
        if (result.Phase == SshConnectionTestPhase.Cancelled)
        {
            Lifecycle = SettingsLifecycle.Ready;
            ErrorCode = SshCodes.TestCancelled;
            return;
        }

        if (result.Phase == SshConnectionTestPhase.AwaitingHostVerification)
        {
            Lifecycle = SettingsLifecycle.AwaitingHostKey;
            ErrorCode = result.Code;
            return;
        }

        if (result.Phase == SshConnectionTestPhase.Succeeded)
        {
            Lifecycle = SettingsLifecycle.Ready;
            ErrorCode = null;
            return;
        }

        Lifecycle = SettingsLifecycle.ValidationFailed;
        ErrorCode = result.Code;
    }

    private static HostKeyReviewState? ToReview(HostKeyAssessment? assessment)
    {
        if (assessment?.Candidate is not { } candidate)
            return null;
        return assessment.Status switch
        {
            HostKeyStatus.UnknownCandidate => new(
                HostKeyStatus.UnknownCandidate,
                candidate.FingerprintSha256,
                candidate.KeyType,
                candidate.Port,
                ShellStrings.HostKeySideChannel,
                true,
                false),
            HostKeyStatus.Changed => new(
                HostKeyStatus.Changed,
                candidate.FingerprintSha256,
                candidate.KeyType,
                candidate.Port,
                ShellStrings.HostKeyChangedBlocked,
                false,
                true),
            HostKeyStatus.Trusted => new(
                HostKeyStatus.Trusted,
                candidate.FingerprintSha256,
                candidate.KeyType,
                candidate.Port,
                "",
                false,
                false),
            _ => null
        };
    }

    private async ValueTask<CancellationTokenSource> ReplaceTestAsync(CancellationToken cancellationToken)
    {
        if (_test is { } previous)
        {
            try
            {
                await previous.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }

            await _tester.CancelAsync().ConfigureAwait(false);
        }

        var next = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _test = next;
        return next;
    }

    private void CompleteTest(CancellationTokenSource run)
    {
        if (ReferenceEquals(_test, run))
            _test = null;
        run.Dispose();
    }

    private void Mutate(Func<SshDeviceDraft, SshDeviceDraft> update)
    {
        Draft = update(Draft);
        Lifecycle = SettingsLifecycle.Dirty;
    }

    private void RestoreDraft()
    {
        Draft = CommittedDevice is null ? EmptyDraft(Draft.Device) : ToDraft(CommittedDevice);
    }

    private DeviceProfile? TryBuildProfile()
    {
        var settings = TryBuildSettings();
        if (settings is null || Draft.Device.Value == Guid.Empty)
            return null;
        if (AtomicConfigurationStore.LooksLikeSecret(Draft.Label) ||
            AtomicConfigurationStore.LooksLikeSecret(Draft.VerifiedHerdrPath))
        {
            ErrorCode = ConfigurationCodes.ForbiddenField;
            return null;
        }

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
            ConnectionKinds.Ssh,
            Draft.VerifiedHerdrPath,
            sessions,
            settings);
    }

    private SshDeviceSettings? TryBuildSettings()
    {
        if (Draft.Device.Value == Guid.Empty)
            return null;
        int? port = null;
        if (!string.IsNullOrWhiteSpace(Draft.PortText))
        {
            if (!int.TryParse(Draft.PortText, out var parsed) || parsed is < 1 or > 65535)
            {
                ErrorCode = SshCodes.ProfileInvalid;
                return null;
            }

            port = parsed;
        }

        if (Draft.HostAlias.StartsWith('-'))
        {
            ErrorCode = SshCodes.ProfileInvalid;
            return null;
        }

        if (!string.IsNullOrWhiteSpace(Draft.IdentityFilePath) &&
            !Path.IsPathRooted(Draft.IdentityFilePath))
        {
            ErrorCode = SshCodes.ProfileInvalid;
            return null;
        }

        if (!string.IsNullOrWhiteSpace(Draft.RemoteHerdrPath) &&
            !AtomicConfigurationStore.IsSafePosixAbsolute(Draft.RemoteHerdrPath))
        {
            ErrorCode = SshCodes.ProfileInvalid;
            return null;
        }

        if (!string.IsNullOrWhiteSpace(Draft.RemoteHelperPath) &&
            !AtomicConfigurationStore.IsSafePosixAbsolute(Draft.RemoteHelperPath))
        {
            ErrorCode = SshCodes.ProfileInvalid;
            return null;
        }

        var settings = new SshDeviceSettings(
            Draft.HostAlias,
            string.IsNullOrWhiteSpace(Draft.User) ? null : Draft.User,
            port,
            string.IsNullOrWhiteSpace(Draft.IdentityFilePath) ? null : Draft.IdentityFilePath,
            string.IsNullOrWhiteSpace(Draft.IdentityAgent) ? null : Draft.IdentityAgent,
            string.IsNullOrWhiteSpace(Draft.ProxyJumpAlias) ? null : Draft.ProxyJumpAlias,
            Draft.RemoteHerdrPath,
            string.IsNullOrWhiteSpace(Draft.RemoteHelperPath) ? null : Draft.RemoteHelperPath,
            SshDeviceSettings.ParseAuthMode(Draft.AuthModeRaw),
            CommittedDevice?.Ssh?.ProfileRevision ?? 0);
        var error = AtomicConfigurationStore.ValidateSsh(settings);
        if (error is not null)
        {
            ErrorCode = error;
            return null;
        }

        return settings;
    }

    private static SshDeviceDraft EmptyDraft(DeviceId device) =>
        new(
            device,
            "",
            "",
            "",
            null,
            null,
            null,
            null,
            null,
            "",
            null,
            SshDeviceSettings.AuthModeWire(SshAuthMode.OpenSshConfig),
            []);

    private static SshDeviceDraft ToDraft(DeviceProfile profile) =>
        new(
            profile.Device,
            profile.Label,
            profile.VerifiedHerdrPath,
            profile.Ssh?.HostAlias ?? "",
            profile.Ssh?.User,
            profile.Ssh?.Port?.ToString(),
            profile.Ssh?.IdentityFilePath,
            profile.Ssh?.IdentityAgent,
            profile.Ssh?.ProxyJumpAlias,
            profile.Ssh?.RemoteHerdrPath ?? "",
            profile.Ssh?.RemoteHelperPath,
            profile.Ssh?.AuthMode.Raw ?? SshDeviceSettings.AuthModeWire(SshAuthMode.OpenSshConfig),
            profile.Sessions.Select(item => new LocalSessionDraft(
                item.Kind, item.SessionName, item.Endpoint, item.CanonicalLocation)).ToArray());

    private static SessionProfile? ToSession(LocalSessionDraft draft) =>
        draft.Kind switch
        {
            SessionProfileKind.LocalDefault => SessionProfile.LocalDefault(),
            SessionProfileKind.NamedSession when !string.IsNullOrWhiteSpace(draft.SessionName) =>
                SessionProfile.Named(draft.SessionName),
            SessionProfileKind.ExplicitEndpoint when draft.Endpoint is { } kind &&
                                                     !string.IsNullOrWhiteSpace(draft.CanonicalLocation) =>
                SessionProfile.Explicit(draft.CanonicalLocation, kind, draft.SessionName),
            _ => null
        };

    private static SshResolvedConfiguration Redact(SshResolvedConfiguration value) =>
        value with
        {
            Hostname = SshRedaction.Display(value.Hostname),
            User = SshRedaction.Display(value.User),
            IdentityFiles = value.IdentityFiles
                .Select(item => item with { Path = SshRedaction.Display(item.Path) })
                .ToArray(),
            IdentityAgent = value.IdentityAgent is null ? null : SshRedaction.Display(value.IdentityAgent),
            ProxyJump = value.ProxyJump is null ? null : SshRedaction.Display(value.ProxyJump)
        };

    private sealed class UnavailableSshPreview : ISshConfigPreview
    {
        public ValueTask<SshPreviewResult> PreviewAsync(
            SshDeviceSettings settings, CancellationToken cancellationToken = default)
        {
            _ = (settings, cancellationToken);
            return new(new SshPreviewResult(null, SshCodes.ExecutableUnavailable));
        }
    }
}
