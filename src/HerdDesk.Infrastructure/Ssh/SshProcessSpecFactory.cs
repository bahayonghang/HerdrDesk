using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Rpc;
using HerdDesk.Infrastructure.SshTransports;
using HerdDesk.Infrastructure.Terminal;

namespace HerdDesk.Infrastructure.Ssh;

internal static class SshProcessSpecFactory
{
    public const string RemoteProbeCommand = "true";
    public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ConfigTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan PlatformProbeTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan HelperBootstrapTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan HelperCleanupTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RemoteProbeTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RemoteSessionLifetime = Timeout.InfiniteTimeSpan;

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

    public static bool TryPlatformProbe(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!TrySshT(locator, settings, knownHostsFile, out var arguments, out code))
            return false;

        arguments.Add("sh");
        arguments.Add("-c");
        arguments.Add(HelperRemoteScripts.Probe);
        spec = new(
            locator.SshExecutable,
            WithPrefix(locator, arguments),
            PlatformProbeTimeout,
            SshProcessKind.PlatformProbe);
        code = SshCodes.Ok;
        return true;
    }

    public static bool TryHelperBootstrap(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        string version,
        string target,
        string sha256,
        long length,
        string stagingId,
        string hashCommand,
        ReadOnlyMemory<byte> payload,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!HelperRemoteScripts.IsSafeVersion(version) ||
            !HelperRemoteScripts.IsDeployableTriple(target) ||
            !HelperRemoteScripts.IsSha256(sha256) ||
            !HelperRemoteScripts.IsPositiveLength(length) ||
            !HelperRemoteScripts.IsStagingId(stagingId) ||
            !HelperRemoteScripts.IsHashCommand(hashCommand))
        {
            code = HelperCodes.ManifestInvalid;
            return false;
        }

        if (!TrySshT(locator, settings, knownHostsFile, out var arguments, out code))
            return false;

        arguments.Add("sh");
        arguments.Add("-c");
        arguments.Add(HelperRemoteScripts.Bootstrap);
        arguments.Add("herddesk-helper");
        arguments.Add(version);
        arguments.Add(target);
        arguments.Add(sha256);
        arguments.Add(length.ToString());
        arguments.Add(stagingId);
        arguments.Add(hashCommand);
        spec = new(
            locator.SshExecutable,
            WithPrefix(locator, arguments),
            HelperBootstrapTimeout,
            SshProcessKind.HelperBootstrap,
            payload);
        code = SshCodes.Ok;
        return true;
    }

    public static bool TryHelperCleanup(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        string version,
        string target,
        string stagingId,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!HelperRemoteScripts.IsSafeVersion(version) ||
            !HelperRemoteScripts.IsDeployableTriple(target) ||
            !HelperRemoteScripts.IsStagingId(stagingId))
        {
            code = HelperCodes.ManifestInvalid;
            return false;
        }

        if (!TrySshT(locator, settings, knownHostsFile, out var arguments, out code))
            return false;

        arguments.Add("sh");
        arguments.Add("-c");
        arguments.Add(HelperRemoteScripts.Cleanup);
        arguments.Add("herddesk-helper");
        arguments.Add(version);
        arguments.Add(target);
        arguments.Add(stagingId);
        spec = new(
            locator.SshExecutable,
            WithPrefix(locator, arguments),
            HelperCleanupTimeout,
            SshProcessKind.HelperCleanup);
        code = SshCodes.Ok;
        return true;
    }

    public static bool TryRemoteRpc(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        string helperPath,
        string socketPath,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!AtomicConfigurationStore.IsSafePosixAbsolute(helperPath))
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        var socketError = RpcSocketPath.RejectReason(socketPath);
        if (socketError is not null)
        {
            code = socketError;
            return false;
        }

        if (!AtomicConfigurationStore.IsSafePosixAbsolute(socketPath))
        {
            code = RpcCodes.EndpointInvalid;
            return false;
        }

        return TryRemoteCommand(
            locator,
            settings,
            knownHostsFile,
            SshProcessKind.RemoteRpc,
            RemoteSessionLifetime,
            [helperPath, "rpc", "--socket-path", socketPath],
            out spec,
            out code);
    }

    public static bool TryRemoteTerminal(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        string herdrPath,
        TerminalOpenRequest request,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!AtomicConfigurationStore.IsSafePosixAbsolute(herdrPath))
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        ArgumentNullException.ThrowIfNull(request);
        var tokens = new List<string> { herdrPath };
        tokens.AddRange(TerminalCliArgumentList.Build(request));
        if (tokens.Contains("machine") || tokens.Contains("-t") || tokens.Contains("-tt"))
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        return TryRemoteCommand(
            locator,
            settings,
            knownHostsFile,
            SshProcessKind.RemoteTerminal,
            RemoteSessionLifetime,
            tokens,
            out spec,
            out code);
    }

    public static bool TryRemoteHerdrVersion(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        string herdrPath,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!AtomicConfigurationStore.IsSafePosixAbsolute(herdrPath))
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        return TryRemoteCommand(
            locator,
            settings,
            knownHostsFile,
            SshProcessKind.RemoteHerdrVersion,
            RemoteProbeTimeout,
            [herdrPath, "--version"],
            out spec,
            out code);
    }

    public static bool TryRemoteApiSchema(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        string herdrPath,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!AtomicConfigurationStore.IsSafePosixAbsolute(herdrPath))
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        return TryRemoteCommand(
            locator,
            settings,
            knownHostsFile,
            SshProcessKind.RemoteApiSchema,
            RemoteProbeTimeout,
            [herdrPath, "api", "schema", "--json"],
            out spec,
            out code);
    }

    public static bool TryRemoteHelperVersion(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        string helperPath,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!AtomicConfigurationStore.IsSafePosixAbsolute(helperPath))
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        return TryRemoteCommand(
            locator,
            settings,
            knownHostsFile,
            SshProcessKind.RemoteHelperVersion,
            RemoteProbeTimeout,
            [helperPath, "--version"],
            out spec,
            out code);
    }

    public static bool HasInteractiveTtyFlag(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        foreach (var argument in arguments)
        {
            if (argument is "-t" or "-tt")
                return true;
        }

        return false;
    }

    private static bool TryRemoteCommand(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        SshProcessKind kind,
        TimeSpan timeout,
        IReadOnlyList<string> remoteTokens,
        out SshProcessSpec spec,
        out string code)
    {
        spec = null!;
        if (!PosixShellQuote.TryCommand(remoteTokens, out var remote, out code))
            return false;
        if (!TrySshT(locator, settings, knownHostsFile, out var arguments, out code))
            return false;
        arguments.Add(remote);
        if (HasInteractiveTtyFlag(arguments) || !arguments.Contains("-T"))
        {
            code = SshCodes.ProfileInvalid;
            return false;
        }

        spec = new(
            locator.SshExecutable,
            WithPrefix(locator, arguments),
            timeout,
            kind);
        code = SshCodes.Ok;
        return true;
    }

    private static bool TrySshT(
        OpenSshLocator locator,
        SshDeviceSettings settings,
        string knownHostsFile,
        out List<string> arguments,
        out string code)
    {
        arguments = [];
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
        arguments =
        [
            "-T",
            "-o", "BatchMode=yes",
            "-o", "StrictHostKeyChecking=yes",
            "-o", "UserKnownHostsFile=" + knownHostsFile,
            "-o", "GlobalKnownHostsFile=" + globalKnown,
            "-o", "PasswordAuthentication=no",
            "-o", "KbdInteractiveAuthentication=no",
            "-o", "PreferredAuthentications=publickey"
        ];
        if (!string.IsNullOrEmpty(settings.IdentityFilePath))
            arguments.AddRange(["-o", "IdentitiesOnly=yes"]);
        AddTypedFields(arguments, settings);
        arguments.Add(settings.HostAlias);
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
