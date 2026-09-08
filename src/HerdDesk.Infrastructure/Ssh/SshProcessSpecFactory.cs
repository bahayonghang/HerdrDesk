using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.Infrastructure.Ssh;

internal static class SshProcessSpecFactory
{
    public const string RemoteProbeCommand = "true";
    public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ConfigTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public static bool TryVersion(OpenSshLocator locator, out SshProcessSpec spec, out string code)
    {
        spec = null!;
        if (!IsSafeExecutable(locator.SshExecutable))
        {
            code = SshCodes.ExecutableUnavailable;
            return false;
        }

        spec = new(
            locator.SshExecutable,
            WithPrefix(locator, ["-V"]),
            VersionTimeout,
            SshProcessKind.Version);
        code = SshCodes.Ok;
        return true;
    }

    public static bool TryConfigPreview(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        var previewError = Validate(locator.SshExecutable, settings);
        if (previewError is not null)
        {
            code = previewError;
            return false;
        }

        var arguments = new List<string> { "-G" };
        AddTypedFields(arguments, settings);
        arguments.Add(settings.HostAlias);
        spec = new(
            locator.SshExecutable,
            WithPrefix(locator, arguments),
            ConfigTimeout,
            SshProcessKind.ConfigPreview);
        code = SshCodes.Ok;
        return true;
    }

    public static bool TryHostKeyScan(
        OpenSshLocator locator,
        string host,
        int port,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!IsSafeExecutable(locator.KeyScanExecutable))
        {
            code = SshCodes.ExecutableUnavailable;
            return false;
        }

        if (!AtomicConfigurationStore.IsSafeHostAlias(host) || port is < 1 or > 65535)
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        spec = new(
            locator.KeyScanExecutable,
            WithPrefix(locator, ["-t", "ed25519,rsa,ecdsa", "-T", "5", "-p", port.ToString(), host]),
            ScanTimeout,
            SshProcessKind.HostKeyScan);
        code = SshCodes.Ok;
        return true;
    }

    public static bool TryAuthProbe(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        var probeError = Validate(locator.SshExecutable, settings);
        if (probeError is not null)
        {
            code = probeError;
            return false;
        }
        if (!Path.IsPathFullyQualified(knownHostsFile) ||
            knownHostsFile.Contains("..", StringComparison.Ordinal) ||
            AtomicConfigurationStore.LooksLikeSecret(knownHostsFile))
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        var globalKnown = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        var arguments = new List<string>
        {
            "-T",
            "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=yes",
            "-o", "UserKnownHostsFile=" + knownHostsFile,
            "-o", "GlobalKnownHostsFile=" + globalKnown,
            "-o", "PasswordAuthentication=no",
            "-o", "KbdInteractiveAuthentication=no",
            "-o", "PreferredAuthentications=publickey"
        };
        if (!string.IsNullOrEmpty(settings.IdentityFilePath))
            arguments.AddRange(["-o", "IdentitiesOnly=yes"]);
        AddTypedFields(arguments, settings);
        arguments.Add(settings.HostAlias);
        arguments.Add(RemoteProbeCommand);
        spec = new(
            locator.SshExecutable,
            WithPrefix(locator, arguments),
            ProbeTimeout,
            SshProcessKind.AuthProbe);
        code = SshCodes.Ok;
        return true;
    }

    private static void AddTypedFields(List<string> arguments, SshDeviceSettings settings)
    {
        if (settings.Port is { } port)
        {
            arguments.Add("-p");
            arguments.Add(port.ToString());
        }

        if (!string.IsNullOrEmpty(settings.User))
        {
            arguments.Add("-l");
            arguments.Add(settings.User);
        }

        if (!string.IsNullOrEmpty(settings.IdentityFilePath))
        {
            arguments.Add("-i");
            arguments.Add(settings.IdentityFilePath);
        }

        if (!string.IsNullOrEmpty(settings.IdentityAgent))
        {
            arguments.Add("-o");
            arguments.Add("IdentityAgent=" + settings.IdentityAgent);
        }

        if (!string.IsNullOrEmpty(settings.ProxyJumpAlias))
        {
            arguments.Add("-o");
            arguments.Add("ProxyJump=" + settings.ProxyJumpAlias);
        }
    }

    private static IReadOnlyList<string> WithPrefix(OpenSshLocator locator, IReadOnlyList<string> arguments)
    {
        if (locator.ArgumentPrefix.Count == 0)
            return arguments;
        var combined = new List<string>(locator.ArgumentPrefix.Count + arguments.Count);
        combined.AddRange(locator.ArgumentPrefix);
        combined.AddRange(arguments);
        return combined;
    }

    private static string? Validate(string executable, SshDeviceSettings settings)
    {
        if (!IsSafeExecutable(executable))
            return SshCodes.ExecutableUnavailable;
        var profile = AtomicConfigurationStore.ValidateSsh(settings);
        if (profile is not null)
            return profile == ConfigurationCodes.ForbiddenField
                ? ConfigurationCodes.ForbiddenField
                : SshCodes.ProfileInvalid;
        if (settings.AuthMode.Known is null)
            return SshCodes.AuthUnsupported;
        return null;
    }

    private static bool IsSafeExecutable(string path) =>
        Path.IsPathFullyQualified(path) &&
        !path.Contains("..", StringComparison.Ordinal) &&
        !AtomicConfigurationStore.LooksLikeSecret(path);
}
