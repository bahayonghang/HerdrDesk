using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;

namespace HerdDesk.Infrastructure.SshTransports;

internal readonly record struct CompatibilityCacheKey(
    Guid Device,
    long ProfileRevision,
    long HostKeyRevision,
    string HelperHash);

internal sealed record RemoteHostProbeResult(
    bool Succeeded,
    bool CliCompatible,
    string Code,
    SshRetryDisposition Disposition,
    string HerdrVersionSha256,
    string SchemaSha256,
    int? SchemaProtocol,
    int? SchemaVersion,
    string HelperVersionSha256);

internal sealed record SessionPingResult(
    bool Succeeded,
    string Code,
    SshRetryDisposition Disposition,
    ConnectionEpoch Epoch);

internal sealed class RemoteCompatibilityProbe
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly OpenSshLocator _locator;
    private readonly ISshProcessRunner _runner;
    private readonly ConcurrentDictionary<CompatibilityCacheKey, RemoteHostProbeResult> _hostCache = new();
    private readonly ConcurrentDictionary<SessionKey, SessionPingResult> _pings = new();

    public RemoteCompatibilityProbe(OpenSshLocator locator, ISshProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        _locator = locator;
        _runner = runner;
    }

    public async ValueTask<RemoteHostProbeResult> ProbeHostAsync(
        RemoteSessionLaunch launch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var key = new CompatibilityCacheKey(
            launch.Session.Device.Value,
            launch.Settings.ProfileRevision,
            launch.HostKeyRevision,
            launch.Receipt.Sha256);
        if (_hostCache.TryGetValue(key, out var cached))
            return cached;

        var probed = await RunHostProbesAsync(launch, cancellationToken).ConfigureAwait(false);
        if (probed.Succeeded)
            _hostCache[key] = probed;
        return probed;
    }

    public async ValueTask<SessionPingResult> PingAsync(
        IRpcRequestConnection request,
        SessionKey session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var empty = JsonDocument.Parse("{}");
        using var outcome = await request.RequestAsync("ping", empty.RootElement.Clone(), cancellationToken)
            .ConfigureAwait(false);
        SessionPingResult result;
        if (outcome.Succeeded)
        {
            result = new(true, SshCodes.Ok, SshRetryDisposition.Stop, request.Epoch);
        }
        else
        {
            var code = SshTransportMapper.FromRpc(outcome.Failure?.Code);
            result = new(false, code, SshTransportMapper.Disposition(code), request.Epoch);
        }

        _pings[session] = result;
        return result;
    }

    public bool TryGetPing(SessionKey session, out SessionPingResult result) =>
        _pings.TryGetValue(session, out result!);

    private async ValueTask<RemoteHostProbeResult> RunHostProbesAsync(
        RemoteSessionLaunch launch,
        CancellationToken cancellationToken)
    {
        var helper = launch.Settings.RemoteHelperPath;
        if (helper is null)
            return Fail(SshTransportCodes.TrustRequired, "", "", null, null, "");

        if (!SshProcessSpecFactory.TryRemoteHerdrVersion(
                _locator, launch.Settings, launch.KnownHostsFile, launch.Settings.RemoteHerdrPath,
                out var versionSpec, out var versionCode))
            return Fail(versionCode, "", "", null, null, "");
        var versionRun = await _runner.RunAsync(versionSpec, cancellationToken).ConfigureAwait(false);
        var versionMapped = MapRun(versionRun, jsonRequired: false, out var versionHash, out _, out _);
        if (versionMapped is not null)
            return Fail(versionMapped, versionHash, "", null, null, "");

        if (!SshProcessSpecFactory.TryRemoteApiSchema(
                _locator, launch.Settings, launch.KnownHostsFile, launch.Settings.RemoteHerdrPath,
                out var schemaSpec, out var schemaCode))
            return Fail(schemaCode, versionHash, "", null, null, "");
        var schemaRun = await _runner.RunAsync(schemaSpec, cancellationToken).ConfigureAwait(false);
        var schemaMapped = MapRun(schemaRun, jsonRequired: true, out var schemaHash, out var protocol, out var schemaVersion);
        if (schemaMapped is not null)
            return Fail(schemaMapped, versionHash, schemaHash, protocol, schemaVersion, "");

        if (!SshProcessSpecFactory.TryRemoteHelperVersion(
                _locator, launch.Settings, launch.KnownHostsFile, helper,
                out var helperSpec, out var helperCode))
            return Fail(helperCode, versionHash, schemaHash, protocol, schemaVersion, "");
        var helperRun = await _runner.RunAsync(helperSpec, cancellationToken).ConfigureAwait(false);
        var helperMapped = MapRun(helperRun, jsonRequired: false, out var helperHash, out _, out _);
        if (helperMapped is not null)
            return Fail(helperMapped, versionHash, schemaHash, protocol, schemaVersion, helperHash);

        var compatible = protocol == SchemaCompatibilityBinding.PinnedProtocol &&
                         schemaVersion == SchemaCompatibilityBinding.PinnedSchemaVersion;
        var code = compatible ? SshCodes.Ok : SshTransportCodes.SchemaIncompatible;
        return new(
            true,
            compatible,
            code,
            compatible ? SshRetryDisposition.Stop : SshTransportMapper.Disposition(code),
            versionHash,
            schemaHash,
            protocol,
            schemaVersion,
            helperHash);
    }

    private static string? MapRun(
        SshProcessRunResult ran,
        bool jsonRequired,
        out string sha256,
        out int? protocol,
        out int? schemaVersion)
    {
        sha256 = "";
        protocol = null;
        schemaVersion = null;
        if (ran.Cancelled)
            return SshTransportCodes.TransportCancelled;
        if (ran.TimedOut)
            return SshTransportCodes.DaemonUnavailable;
        if (ran.Stdout.Length > SshOwnedProcessRunner.MaxOutputBytes)
            return SshTransportCodes.RecordTooLarge;
        var bytes = Encoding.UTF8.GetBytes(ran.Stdout);
        sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (jsonRequired)
        {
            if (!TryReadSchema(ran.Stdout, out protocol, out schemaVersion))
                return SshTransportCodes.StdoutProtocolPollution;
        }

        if (ran.ExitCode != 0)
            return ClassifyExit(ran);
        return null;
    }

    private static bool TryReadSchema(string stdout, out int? protocol, out int? schemaVersion)
    {
        protocol = null;
        schemaVersion = null;
        if (string.IsNullOrWhiteSpace(stdout))
            return false;
        var trimmed = stdout.Trim();
        if (trimmed[0] != '{')
            return false;
        try
        {
            _ = StrictUtf8.GetCharCount(Encoding.UTF8.GetBytes(trimmed));
            using var document = JsonDocument.Parse(trimmed, new JsonDocumentOptions
            {
                MaxDepth = 64,
                AllowDuplicateProperties = false
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (root.TryGetProperty("protocol", out var protocolEl) && protocolEl.TryGetInt32(out var protocolValue))
                protocol = protocolValue;
            if (root.TryGetProperty("schema_version", out var versionEl) && versionEl.TryGetInt32(out var versionValue))
                schemaVersion = versionValue;
            return protocol is not null && schemaVersion is not null;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException
                                          or FormatException)
        {
            return false;
        }
    }

    private static string ClassifyExit(SshProcessRunResult ran)
    {
        _ = ran;
        return SshTransportCodes.UnclassifiedExit;
    }

    private static RemoteHostProbeResult Fail(
        string code,
        string versionHash,
        string schemaHash,
        int? protocol,
        int? schemaVersion,
        string helperHash) =>
        new(
            false,
            false,
            code,
            SshTransportMapper.Disposition(code),
            versionHash,
            schemaHash,
            protocol,
            schemaVersion,
            helperHash);
}
