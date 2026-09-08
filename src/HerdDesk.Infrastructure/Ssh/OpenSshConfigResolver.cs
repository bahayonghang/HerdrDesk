using System.Globalization;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Ssh;

internal static class OpenSshGParser
{
    public const int MaxOutputBytes = 256 * 1024;

    public static bool TryParse(
        string stdout,
        SshDeviceSettings settings,
        out SshResolvedConfiguration configuration,
        out string code)
    {
        configuration = null!;
        code = SshCodes.ConfigInvalid;
        if (stdout.Length > MaxOutputBytes)
            return false;
        if (AtomicConfigurationStoreLooksSecret(stdout))
            return false;

        string? hostname = null;
        string? user = null;
        int? port = null;
        string? agent = null;
        string? jump = null;
        var identities = new List<SshResolvedIdentity>();
        using var reader = new StringReader(stdout);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;
            var split = FirstAsciiWhitespace(line);
            var key = split < 0 ? line : line[..split];
            var value = split < 0 ? "" : line[(split + 1)..].TrimStart();
            key = key.Trim().ToLowerInvariant();
            switch (key)
            {
                case "hostname":
                    hostname = value;
                    break;
                case "user":
                    user = value;
                    break;
                case "port":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                        parsed is < 1 or > 65535)
                        return false;
                    port = parsed;
                    break;
                case "identityfile":
                    if (value.Length > 0 &&
                        !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                        identities.Add(new SshResolvedIdentity(value, IdentitySource(settings, value)));
                    break;
                case "identityagent":
                    if (value.Length > 0 &&
                        !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                        agent = value;
                    break;
                case "proxyjump":
                    if (value.Length > 0 &&
                        !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
                        jump = value;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(hostname) || hostname[0] == '-')
            return false;
        if (AtomicConfigurationStoreLooksSecret(hostname) ||
            AtomicConfigurationStoreLooksSecret(user) ||
            AtomicConfigurationStoreLooksSecret(agent) ||
            AtomicConfigurationStoreLooksSecret(jump))
            return false;

        configuration = new SshResolvedConfiguration(
            hostname,
            HostnameSource(settings, hostname),
            user ?? settings.User ?? "",
            settings.User is not null ? SshValueSource.ExplicitField : SshValueSource.OpenSshConfig,
            port ?? settings.Port ?? 22,
            settings.Port is not null ? SshValueSource.ExplicitField : SshValueSource.OpenSshConfig,
            identities,
            agent,
            agent is null
                ? null
                : settings.IdentityAgent is not null
                    ? SshValueSource.ExplicitField
                    : SshValueSource.OpenSshConfig,
            jump,
            jump is null
                ? null
                : settings.ProxyJumpAlias is not null
                    ? SshValueSource.ExplicitField
                    : SshValueSource.OpenSshConfig);
        code = SshCodes.Ok;
        return true;
    }

    public static bool LooksLikeOpenSshVersion(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 4096)
            return false;
        if (AtomicConfigurationStoreLooksSecret(text))
            return false;
        return text.Contains("OpenSSH", StringComparison.Ordinal);
    }

    public static bool TryParseKeyScan(
        string stdout,
        string host,
        int port,
        SshHopKind hop,
        DateTimeOffset utc,
        out HostKeyCandidate candidate,
        out string code)
    {
        candidate = null!;
        code = SshCodes.HostKeyUnknown;
        if (stdout.Length > MaxOutputBytes || AtomicConfigurationStoreLooksSecret(stdout))
            return false;
        using var reader = new StringReader(stdout);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = SplitAscii(line);
            if (parts.Count < 3)
                continue;
            var keyType = parts[1];
            var blob = parts[2];
            if (keyType.StartsWith('-') || blob.StartsWith('-'))
                continue;
            try
            {
                var raw = Convert.FromBase64String(blob);
                var hash = System.Security.Cryptography.SHA256.HashData(raw);
                var hex = Convert.ToHexString(hash).ToLowerInvariant();
                var fingerprint = "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
                candidate = new HostKeyCandidate(host, port, hop, keyType, hex, fingerprint, utc);
                code = SshCodes.Ok;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
    }

    public static string ClassifyAuthOutput(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return SshCodes.TestFailed;
        var lower = stderr.ToLowerInvariant();
        if (lower.Contains("password", StringComparison.Ordinal) ||
            lower.Contains("passphrase", StringComparison.Ordinal) ||
            lower.Contains("keyboard-interactive", StringComparison.Ordinal) ||
            lower.Contains("verification code", StringComparison.Ordinal) ||
            lower.Contains("pkcs11", StringComparison.Ordinal) ||
            lower.Contains("tailscale", StringComparison.Ordinal) ||
            lower.Contains("security key", StringComparison.Ordinal))
            return SshCodes.AuthUnsupported;
        if (lower.Contains("remote host identification has changed", StringComparison.Ordinal) ||
            lower.Contains("host key verification failed", StringComparison.Ordinal))
            return SshCodes.HostKeyChanged;
        if (lower.Contains("permission denied", StringComparison.Ordinal) ||
            lower.Contains("authentication failed", StringComparison.Ordinal))
            return SshCodes.AuthFailed;
        return SshCodes.TestFailed;
    }

    private static bool AtomicConfigurationStoreLooksSecret(string? value) =>
        Configuration.AtomicConfigurationStore.LooksLikeSecret(value);

    private static SshValueSource HostnameSource(SshDeviceSettings settings, string hostname) =>
        string.Equals(settings.HostAlias, hostname, StringComparison.Ordinal)
            ? SshValueSource.ExplicitField
            : SshValueSource.OpenSshConfig;

    private static SshValueSource IdentitySource(SshDeviceSettings settings, string path) =>
        string.Equals(settings.IdentityFilePath, path, StringComparison.Ordinal)
            ? SshValueSource.ExplicitField
            : SshValueSource.OpenSshConfig;

    private static int FirstAsciiWhitespace(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c is ' ' or '\t')
                return i;
        }

        return -1;
    }

    private static List<string> SplitAscii(string line)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i <= line.Length; i++)
        {
            if (i == line.Length || line[i] is ' ' or '\t')
            {
                if (i > start)
                    parts.Add(line[start..i]);
                start = i + 1;
            }
        }

        return parts;
    }
}

internal sealed class OpenSshConfigResolver : ISshConfigPreview
{
    private readonly OpenSshLocator _locator;
    private readonly ISshProcessRunner _runner;

    public OpenSshConfigResolver(OpenSshLocator locator, ISshProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        _locator = locator;
        _runner = runner;
    }

    public async ValueTask<SshPreviewResult> PreviewAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!SshProcessSpecFactory.TryConfigPreview(_locator, settings, out var spec, out var code))
            return new(null, code);
        var ran = await _runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        if (ran.Cancelled)
            return new(null, SshCodes.TestCancelled);
        if (ran.TimedOut)
            return new(null, SshCodes.TestTimeout);
        if (ran.ExitCode != 0)
            return new(null, SshCodes.ConfigInvalid);
        if (!OpenSshGParser.TryParse(ran.Stdout, settings, out var parsed, out var parseCode))
            return new(null, parseCode == SshCodes.Ok ? SshCodes.ConfigInvalid : parseCode);
        return new(parsed, null);
    }
}
