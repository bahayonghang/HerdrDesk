namespace HerdDesk.Contracts;

// Created by the trusted host. Never deserialized from a renderer message.
// DeviceId and SessionKey are the identity inputs. PaneKey, window title,
// and agent type are not endpoint identity.

public enum EndpointPreferenceKind { Explicit, Default, Named }

public enum EndpointKind { NamedPipe, UnixSocket, FilesystemPath }

public enum EndpointAccessScope { LocalUser, Unknown }

public enum EndpointObservationKind { Missing, PermissionDenied, CrossUser }

public sealed record EndpointPreference(
    EndpointPreferenceKind Kind,
    string? Location = null,
    string? SessionName = null)
{
    public static EndpointPreference Default { get; } =
        new(EndpointPreferenceKind.Default);

    public static EndpointPreference Explicit(string location) =>
        new(EndpointPreferenceKind.Explicit, location);

    public static EndpointPreference Named(string sessionName) =>
        new(EndpointPreferenceKind.Named, SessionName: sessionName);
}

public sealed record VerifiedEndpointMapping(
    DeviceId Device,
    string EndpointKey,
    string? SessionName,
    string CanonicalLocation,
    EndpointKind Kind,
    string EvidenceId,
    EndpointAccessScope AccessScope);

public sealed record EndpointObservation(
    string CanonicalLocation,
    EndpointObservationKind Kind,
    string DiagnosticId);

public sealed record EndpointResolutionConfig(
    IReadOnlyList<VerifiedEndpointMapping> VerifiedMappings,
    IReadOnlyList<EndpointObservation> Observations)
{
    public static EndpointResolutionConfig Empty { get; } = new(
        Array.Empty<VerifiedEndpointMapping>(),
        Array.Empty<EndpointObservation>());
}

public sealed record ResolvedEndpoint(
    EndpointKind Kind,
    string CanonicalLocation,
    string EvidenceId,
    EndpointAccessScope AccessScope);

public sealed record EndpointResolutionFailure(
    string Code,
    string DiagnosticId,
    bool RequiresExplicitConfiguration);

public sealed record EndpointResolutionResult(
    ResolvedEndpoint? Endpoint,
    EndpointResolutionFailure? Failure)
{
    public bool Resolved => Endpoint is not null;
}
