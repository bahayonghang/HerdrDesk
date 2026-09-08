using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// Maps DeviceId, SessionKey, and a trusted-host configuration object to an
/// API endpoint candidate. Does not query OS credentials, environment, or
/// pipes. Does not create a terminal session and does not send JSON RPC.
/// </summary>
public static class EndpointResolver
{
    public static EndpointResolutionResult Resolve(
        DeviceId device,
        SessionKey session,
        EndpointPreference preference,
        EndpointResolutionConfig config)
    {
        ArgumentNullException.ThrowIfNull(preference);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(config.VerifiedMappings);
        ArgumentNullException.ThrowIfNull(config.Observations);

        if (!IsValidIdentity(device, session))
            return Fail("invalid_identity", "diag-invalid-identity", false);

        var identityEncoding = CheckUnicode(session.EndpointKey)
            ?? CheckUnicode(session.SessionName);
        if (identityEncoding is not null)
            return identityEncoding;

        return preference.Kind switch
        {
            EndpointPreferenceKind.Explicit => ResolveExplicit(device, session, preference, config),
            EndpointPreferenceKind.Default => ResolveDefault(device, session, preference, config),
            EndpointPreferenceKind.Named => ResolveNamed(device, session, preference, config),
            _ => Fail("invalid_preference", "diag-invalid-preference", true),
        };
    }

    private static EndpointResolutionResult ResolveExplicit(
        DeviceId device,
        SessionKey session,
        EndpointPreference preference,
        EndpointResolutionConfig config)
    {
        if (!string.IsNullOrEmpty(preference.SessionName))
            return Fail("invalid_preference", "diag-invalid-preference", true);
        var location = preference.Location;
        if (string.IsNullOrWhiteSpace(location))
            return Fail("invalid_preference", "diag-invalid-preference", true);
        var encoding = CheckUnicode(location);
        if (encoding is not null)
            return encoding;
        if (HasIllegalControl(location))
            return Fail("invalid_preference", "diag-invalid-preference", true);
        if (LooksLikeEnvironmentGuess(location))
            return Fail("explicit_configuration_required", "diag-unmapped-default", true);
        if (IsRemoteUnc(location))
            return Fail("remote_unc_rejected", "diag-remote-unc", false);
        if (!TryClassify(location, out var kind))
            return Fail("explicit_configuration_required", "diag-unmapped-default", true);

        var evidenceId = "explicit-preference";
        var scope = EndpointAccessScope.LocalUser;
        foreach (var mapping in config.VerifiedMappings)
        {
            if (mapping.Device != device)
                continue;
            if (!OrdinalEquals(mapping.EndpointKey, session.EndpointKey))
                continue;
            if (!OrdinalEquals(mapping.CanonicalLocation, location))
                continue;
            evidenceId = Redact(mapping.EvidenceId, evidenceId);
            kind = mapping.Kind;
            scope = mapping.AccessScope;
            break;
        }

        return ApplyObservations(
            location,
            new ResolvedEndpoint(kind, location, evidenceId, scope),
            config);
    }

    private static EndpointResolutionResult ResolveDefault(
        DeviceId device,
        SessionKey session,
        EndpointPreference preference,
        EndpointResolutionConfig config)
    {
        if (!string.IsNullOrEmpty(session.SessionName)
            || !string.IsNullOrEmpty(preference.SessionName)
            || !string.IsNullOrEmpty(preference.Location))
            return Fail("invalid_preference", "diag-invalid-preference", true);

        return ResolveMapped(device, session, null, config, "explicit_configuration_required", "diag-unmapped-default");
    }

    private static EndpointResolutionResult ResolveNamed(
        DeviceId device,
        SessionKey session,
        EndpointPreference preference,
        EndpointResolutionConfig config)
    {
        var name = preference.SessionName;
        if (string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(preference.Location))
            return Fail("invalid_preference", "diag-invalid-preference", true);
        var encoding = CheckUnicode(name);
        if (encoding is not null)
            return encoding;
        if (!IsDefaultName(session.SessionName) && !OrdinalEquals(session.SessionName, name))
            return Fail("invalid_preference", "diag-invalid-preference", true);

        return ResolveMapped(device, session, name, config, "named_session_unmapped", "diag-unmapped-named");
    }

    private static EndpointResolutionResult ResolveMapped(
        DeviceId device,
        SessionKey session,
        string? sessionName,
        EndpointResolutionConfig config,
        string unmappedCode,
        string unmappedDiagnostic)
    {
        VerifiedEndpointMapping? found = null;
        var count = 0;
        foreach (var mapping in config.VerifiedMappings)
        {
            if (mapping.Device != device)
                continue;
            if (!OrdinalEquals(mapping.EndpointKey, session.EndpointKey))
                continue;
            if (!SessionNameEquals(mapping.SessionName, sessionName))
                continue;
            found = mapping;
            count++;
        }

        if (count == 0)
            return Fail(unmappedCode, unmappedDiagnostic, true);
        if (count > 1 || found is null)
            return Fail("ambiguous_mapping", "diag-ambiguous-mapping", true);

        var location = found.CanonicalLocation;
        var encoding = CheckUnicode(location);
        if (encoding is not null)
            return encoding;
        if (HasIllegalControl(location) || LooksLikeEnvironmentGuess(location))
            return Fail("explicit_configuration_required", "diag-unmapped-default", true);
        if (IsRemoteUnc(location))
            return Fail("remote_unc_rejected", "diag-remote-unc", false);

        return ApplyObservations(
            location,
            new ResolvedEndpoint(
                found.Kind,
                location,
                Redact(found.EvidenceId, "verified-mapping"),
                found.AccessScope),
            config);
    }

    private static EndpointResolutionResult ApplyObservations(
        string location,
        ResolvedEndpoint candidate,
        EndpointResolutionConfig config)
    {
        foreach (var observation in config.Observations)
        {
            if (!OrdinalEquals(observation.CanonicalLocation, location))
                continue;
            switch (observation.Kind)
            {
                case EndpointObservationKind.PermissionDenied:
                    return Fail(
                        "permission_denied",
                        Redact(observation.DiagnosticId, "diag-permission-denied"),
                        false);
                case EndpointObservationKind.CrossUser:
                    return Fail(
                        "cross_user_denied",
                        Redact(observation.DiagnosticId, "diag-cross-user"),
                        false);
                case EndpointObservationKind.Missing:
                    return Fail(
                        "endpoint_not_found",
                        Redact(observation.DiagnosticId, "diag-explicit-missing"),
                        true);
                default:
                    return Fail("invalid_preference", "diag-invalid-preference", true);
            }
        }

        return new EndpointResolutionResult(candidate, null);
    }

    private static bool IsValidIdentity(DeviceId device, SessionKey session) =>
        device.Value != Guid.Empty &&
        session.Device.Value != Guid.Empty &&
        device == session.Device &&
        !string.IsNullOrWhiteSpace(session.EndpointKey);

    private static bool IsDefaultName(string? name) => string.IsNullOrEmpty(name);

    private static bool SessionNameEquals(string? left, string? right)
    {
        if (IsDefaultName(left) && IsDefaultName(right))
            return true;
        if (IsDefaultName(left) || IsDefaultName(right))
            return false;
        return OrdinalEquals(left, right);
    }

    private static bool OrdinalEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static bool HasIllegalControl(string value) =>
        value.IndexOf('\0') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0;

    private static bool LooksLikeEnvironmentGuess(string location) =>
        ContainsIgnoreCase(location, "%APPDATA%") ||
        ContainsIgnoreCase(location, "%LOCALAPPDATA%") ||
        ContainsIgnoreCase(location, "%USERPROFILE%") ||
        ContainsIgnoreCase(location, "%HOMEPATH%") ||
        ContainsIgnoreCase(location, "%HOMEDRIVE%");

    private static bool ContainsIgnoreCase(string value, string token) =>
        value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRemoteUnc(string location)
    {
        if (location.StartsWith("//", StringComparison.Ordinal) &&
            !location.StartsWith("//./", StringComparison.Ordinal) &&
            !location.StartsWith("//?/", StringComparison.Ordinal))
            return true;
        if (!location.StartsWith(@"\\", StringComparison.Ordinal))
            return false;
        if (location.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return true;
        if (location.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            location.StartsWith(@"\\?\", StringComparison.Ordinal))
            return false;
        return true;
    }

    private static bool TryClassify(string location, out EndpointKind kind)
    {
        if (IsLocalNamedPipe(location))
        {
            kind = EndpointKind.NamedPipe;
            return true;
        }

        if (location.StartsWith('/') && !location.StartsWith("//", StringComparison.Ordinal))
        {
            kind = EndpointKind.UnixSocket;
            return true;
        }

        if (IsWindowsDrivePath(location))
        {
            kind = EndpointKind.FilesystemPath;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsLocalNamedPipe(string location) =>
        StartsWithIgnoreCase(location, @"\\.\pipe\") ||
        StartsWithIgnoreCase(location, @"\\?\pipe\") ||
        StartsWithIgnoreCase(location, "//./pipe/") ||
        StartsWithIgnoreCase(location, "//?/pipe/");

    private static bool StartsWithIgnoreCase(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsDrivePath(string location) =>
        location.Length >= 3 &&
        char.IsAsciiLetter(location[0]) &&
        location[1] == ':' &&
        (location[2] == '\\' || location[2] == '/');

    private static EndpointResolutionResult? CheckUnicode(string? value)
    {
        if (value is null)
            return null;
        if (!HasUnpairedSurrogate(value))
            return null;
        return Fail("unicode_encoding_error", "diag-unicode-encoding", false);
    }

    private static bool HasUnpairedSurrogate(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                    return true;
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string Redact(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return fallback;
        if (HasUnpairedSurrogate(candidate))
            return fallback;
        if (candidate.IndexOfAny(['\\', '/', '%']) >= 0)
            return fallback;
        return candidate;
    }

    private static EndpointResolutionResult Fail(string code, string diagnosticId, bool requiresExplicit) =>
        new(null, new EndpointResolutionFailure(code, diagnosticId, requiresExplicit));
}
