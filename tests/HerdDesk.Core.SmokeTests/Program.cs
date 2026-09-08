using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;

// BCL-only G0 smoke runner; dotnet run, not dotnet test. The Windows UI and
// upstream daemon are not exercised. No external NuGet packages are needed.
static byte[] Frame(ulong sequence = 1, bool full = true, byte[]? bytes = null) =>
    JsonSerializer.SerializeToUtf8Bytes(new { type = "terminal.frame", seq = sequence,
        encoding = "ansi", width = 120, height = 40, full,
        bytes = Convert.ToBase64String(bytes ?? Encoding.UTF8.GetBytes("hello")) });
static void Check(bool condition) { if (!condition) throw new Exception("assertion_failed"); }
static void Reject(Action action)
{
    try { action(); }
    catch (TerminalProtocolException) { return; }
    throw new Exception("expected_protocol_rejection");
}
static void RejectMalformedThenLatch(byte[] raw)
{
    var parser = new TerminalFrameParser();
    try
    {
        parser.Parse(raw);
        throw new Exception("expected_malformed_terminal_record");
    }
    catch (TerminalProtocolException error) when (error.Message == "malformed_terminal_record")
    {
        Check(error.InnerException is null);
        Check(!error.Message.Contains('\\'));
        Check(!error.Message.Contains('/'));
        Check(!error.Message.Contains('\ud800'));
    }
    try
    {
        parser.Parse(Frame());
        throw new Exception("expected_terminal_stream_not_active");
    }
    catch (TerminalProtocolException error) when (error.Message == "terminal_stream_not_active")
    {
        Check(error.InnerException is null);
        Check(!error.Message.Contains('\\'));
        Check(!error.Message.Contains('/'));
        Check(!error.Message.Contains('\ud800'));
    }
}
static Dictionary<string, byte[]> LoadEdgeCases()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var path = Path.Combine(dir.FullName, "tests", "fixtures", "protocol-edge-cases.json");
        if (!File.Exists(path)) continue;
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
            map[item.GetProperty("name").GetString()!] =
                Encoding.UTF8.GetBytes(item.GetProperty("raw").GetString()!);
        return map;
    }
    throw new Exception("protocol_edge_corpus_missing");
}

static JsonDocument LoadJsonFixture(string name)
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var path = Path.Combine(dir.FullName, "tests", "fixtures", name);
        if (File.Exists(path))
            return JsonDocument.Parse(File.ReadAllBytes(path));
    }
    throw new Exception("fixture_missing");
}
static string? OptionalString(JsonElement obj, string name)
{
    if (!obj.TryGetProperty(name, out var element) ||
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        return null;
    return element.GetString();
}
static EndpointKind ParseEndpointKind(string value) => value switch
{
    "named_pipe" => EndpointKind.NamedPipe,
    "unix_socket" => EndpointKind.UnixSocket,
    "filesystem_path" => EndpointKind.FilesystemPath,
    _ => throw new Exception("unknown_endpoint_kind"),
};
static EndpointAccessScope ParseAccessScope(string value) => value switch
{
    "local_user" => EndpointAccessScope.LocalUser,
    "unknown" => EndpointAccessScope.Unknown,
    _ => throw new Exception("unknown_access_scope"),
};
static EndpointObservationKind ParseObservationKind(string value) => value switch
{
    "missing" => EndpointObservationKind.Missing,
    "permission_denied" => EndpointObservationKind.PermissionDenied,
    "cross_user" => EndpointObservationKind.CrossUser,
    _ => throw new Exception("unknown_observation_kind"),
};
static EndpointPreference ReadPreference(JsonElement preference)
{
    var kind = preference.GetProperty("kind").GetString();
    return kind switch
    {
        "explicit" => EndpointPreference.Explicit(preference.GetProperty("location").GetString()!),
        "default" => EndpointPreference.Default,
        "named" => EndpointPreference.Named(preference.GetProperty("session_name").GetString()!),
        _ => throw new Exception("unknown_preference_kind"),
    };
}
static EndpointResolutionConfig ReadEndpointConfig(Guid device, JsonElement caseElement)
{
    var config = caseElement.GetProperty("config");
    var mappings = new List<VerifiedEndpointMapping>();
    foreach (var item in config.GetProperty("verified_mappings").EnumerateArray())
    {
        var mappedDeviceText = OptionalString(item, "device");
        var mappedDevice = mappedDeviceText is null ? device : Guid.Parse(mappedDeviceText);
        mappings.Add(new VerifiedEndpointMapping(
            new DeviceId(mappedDevice),
            item.GetProperty("endpoint_key").GetString()!,
            OptionalString(item, "session_name"),
            item.GetProperty("canonical_location").GetString()!,
            ParseEndpointKind(item.GetProperty("kind").GetString()!),
            item.GetProperty("evidence_id").GetString()!,
            ParseAccessScope(item.GetProperty("access_scope").GetString()!)));
    }
    var observations = new List<EndpointObservation>();
    foreach (var item in config.GetProperty("observations").EnumerateArray())
        observations.Add(new EndpointObservation(
            item.GetProperty("canonical_location").GetString()!,
            ParseObservationKind(item.GetProperty("kind").GetString()!),
            item.GetProperty("diagnostic_id").GetString()!));
    return new EndpointResolutionConfig(mappings, observations);
}
static void AssertRedacted(string value)
{
    Check(!value.Contains('\\'));
    Check(!value.Contains('/'));
    Check(!value.Contains("%APPDATA%", StringComparison.OrdinalIgnoreCase));
}
static void AssertEndpointCase(JsonElement item)
{
    Check(item.GetProperty("simulation").GetBoolean());
    Check(item.GetProperty("live_result").GetString() == "blocked");
    Check(item.GetProperty("evidence_level").GetString() == "synthetic");
    var device = Guid.Parse(item.GetProperty("device").GetString()!);
    var sessionElement = item.GetProperty("session");
    var session = new SessionKey(
        new DeviceId(device),
        sessionElement.GetProperty("endpoint_key").GetString()!,
        OptionalString(sessionElement, "session_name"));
    var result = EndpointResolver.Resolve(
        new DeviceId(device),
        session,
        ReadPreference(item.GetProperty("preference")),
        ReadEndpointConfig(device, item));
    var expect = item.GetProperty("expect");
    var resolved = expect.GetProperty("resolved").GetBoolean();
    Check(result.Resolved == resolved);
    if (resolved)
    {
        Check(result.Endpoint is not null && result.Failure is null);
        Check(result.Endpoint!.Kind == ParseEndpointKind(expect.GetProperty("kind").GetString()!));
        Check(result.Endpoint.CanonicalLocation == expect.GetProperty("canonical_location").GetString());
        Check(result.Endpoint.AccessScope == ParseAccessScope(expect.GetProperty("access_scope").GetString()!));
        if (item.GetProperty("preference").GetProperty("kind").GetString() == "explicit")
            Check(result.Endpoint.CanonicalLocation == item.GetProperty("preference").GetProperty("location").GetString());
        AssertRedacted(result.Endpoint.EvidenceId);
        Check(!result.Endpoint.CanonicalLocation.Contains("%APPDATA%", StringComparison.OrdinalIgnoreCase));
        return;
    }
    Check(result.Endpoint is null && result.Failure is not null);
    Check(result.Failure!.Code == expect.GetProperty("code").GetString());
    Check(result.Failure.RequiresExplicitConfiguration ==
        expect.GetProperty("requires_explicit_configuration").GetBoolean());
    AssertRedacted(result.Failure.Code);
    AssertRedacted(result.Failure.DiagnosticId);
}
static bool Flag(JsonElement obj, string name) =>
    obj.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.True;
static bool? OptionalBool(JsonElement obj, string name)
{
    if (!obj.TryGetProperty(name, out var element) ||
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        return null;
    if (element.ValueKind == JsonValueKind.True) return true;
    if (element.ValueKind == JsonValueKind.False) return false;
    throw new Exception("invalid_optional_bool");
}
static TerminalAccess ParseTerminalAccess(string value) => value switch
{
    "disconnected" => TerminalAccess.Disconnected,
    "observing" => TerminalAccess.Observing,
    "acquiring" => TerminalAccess.Acquiring,
    "controlling" => TerminalAccess.Controlling,
    "unknown" => TerminalAccess.Unknown,
    _ => throw new Exception("unknown_terminal_access"),
};
static TerminalLeaseOperation ParseLeaseOperation(string value) => value switch
{
    "observe" => TerminalLeaseOperation.Observe,
    "request_control" => TerminalLeaseOperation.RequestControl,
    "request_takeover" => TerminalLeaseOperation.RequestTakeover,
    "resize_while_verified" => TerminalLeaseOperation.ResizeWhileVerified,
    "release" => TerminalLeaseOperation.Release,
    _ => throw new Exception("unknown_lease_operation"),
};
static TerminalStreamEndKind ParseStreamEnd(string value) => value switch
{
    "none" => TerminalStreamEndKind.None,
    "stdout_eof" => TerminalStreamEndKind.StdoutEof,
    "terminal_closed" => TerminalStreamEndKind.TerminalClosed,
    "bridge_process_exit" => TerminalStreamEndKind.BridgeProcessExit,
    "unknown" => TerminalStreamEndKind.Unknown,
    _ => throw new Exception("unknown_stream_end"),
};
static TerminalControlSignal ParseControlSignal(string? value) => value switch
{
    null or "none" => TerminalControlSignal.None,
    "busy" => TerminalControlSignal.Busy,
    "rejected" => TerminalControlSignal.Rejected,
    "takeover_required" => TerminalControlSignal.TakeoverRequired,
    "takeover_confirmed" => TerminalControlSignal.TakeoverConfirmed,
    "released" => TerminalControlSignal.Released,
    "unknown" => TerminalControlSignal.Unknown,
    _ => throw new Exception("unknown_control_signal"),
};
static TerminalLeaseObservation ReadLeaseObservation(JsonElement observation) =>
    new(
        ParseLeaseOperation(observation.GetProperty("operation").GetString()!),
        ParseTerminalAccess(observation.GetProperty("access_before").GetString()!),
        Flag(observation, "control_verified_before"),
        Flag(observation, "stdout_eof_seen"),
        Flag(observation, "terminal_closed_seen"),
        Flag(observation, "bridge_process_exited"),
        OptionalBool(observation, "pane_alive_observed"),
        OptionalBool(observation, "daemon_alive_observed"),
        Flag(observation, "first_frame_seen"),
        Flag(observation, "process_alive"),
        Flag(observation, "window_focused"),
        Flag(observation, "input_sent"),
        Flag(observation, "input_acknowledged"),
        Flag(observation, "adapter_proved_write_ownership"),
        Flag(observation, "takeover_confirmed"),
        Flag(observation, "resize_attempted"),
        Flag(observation, "resize_acknowledged"),
        Flag(observation, "release_acknowledged"),
        ParseControlSignal(OptionalString(observation, "control_signal")),
        OptionalString(observation, "observed_wire_type"));
static void AssertLeaseCase(JsonElement item)
{
    Check(item.GetProperty("simulation").GetBoolean());
    Check(item.GetProperty("live_result").GetString() == "blocked");
    Check(item.GetProperty("evidence_level").GetString() == "synthetic");
    var result = TerminalLeaseProbe.Map(ReadLeaseObservation(item.GetProperty("observation")));
    var expect = item.GetProperty("expect");
    Check(result.Access == ParseTerminalAccess(expect.GetProperty("access").GetString()!));
    Check(result.ControlVerified == expect.GetProperty("control_verified").GetBoolean());
    Check(result.StreamEnd == ParseStreamEnd(expect.GetProperty("stream_end").GetString()!));
    Check(result.PaneExitVerified == expect.GetProperty("pane_exit_verified").GetBoolean());
    Check(result.Code == expect.GetProperty("code").GetString());
    AssertRedacted(result.Code);
    AssertRedacted(result.DiagnosticId);
    if (result.ControlVerified)
        Check(result.Access == TerminalAccess.Controlling);
}

var pane = new PaneKey(new SessionKey(new DeviceId(Guid.NewGuid()), "test-only-api", "test"), "w1", "p1");
var epoch = new ConnectionEpoch(1);
var context = new InputContext(pane, epoch, TerminalAccess.Controlling, ControlVerified: true);
var request = new RendererInput(pane, epoch, InputOrigin.CommittedText, Encoding.UTF8.GetBytes("你好"));
var edge = LoadEdgeCases();
var cases = new (string Name, Action Run)[]
{
    ("parse full frame", () => Check(new TerminalFrameParser().Parse(Frame()) is TerminalFrame { Sequence: 1, Full: true })),
    ("ordered delta", () => { var p = new TerminalFrameParser(); p.Parse(Frame()); Check(p.Parse(Frame(2, false)) is TerminalFrame { Sequence: 2 }); }),
    ("initial delta rejected", () => Reject(() => new TerminalFrameParser().Parse(Frame(full: false)))),
    ("gap rejected", () => { var p = new TerminalFrameParser(); p.Parse(Frame()); Reject(() => p.Parse(Frame(3))); }),
    ("failure latched", () => { var p = new TerminalFrameParser(); Reject(() => p.Parse("{}"u8.ToArray())); Reject(() => p.Parse(Frame())); }),
    ("replay rejected", () => { var p = new TerminalFrameParser(); p.Parse(Frame()); Reject(() => p.Parse(Frame())); }),
    ("duplicate key rejected", () => Reject(() => new TerminalFrameParser().Parse("{\"type\":\"terminal.closed\",\"type\":\"terminal.closed\"}"u8.ToArray()))),
    ("invalid utf8 rejected", () => Reject(() => new TerminalFrameParser().Parse(new byte[] { 255 }))),
    ("utf8 frame split preserved", () => { var p = new TerminalFrameParser(); var f = (TerminalFrame)p.Parse(Frame(bytes: new byte[] { 0xe4 })); Check(f.Bytes.Span[0] == 0xe4); }),
    ("closed envelope", () => Check(new TerminalFrameParser().Parse("{\"type\":\"terminal.closed\",\"reason\":\"detached\"}"u8.ToArray()) is TerminalClosed { ReasonPresent: true })),
    ("frame after close rejected", () => { var p = new TerminalFrameParser(); p.Parse("{\"type\":\"terminal.closed\"}"u8.ToArray()); Reject(() => p.Parse(Frame())); }),
    ("max uint64 sequence", () => Check(new TerminalFrameParser().Parse(Frame(ulong.MaxValue)) is TerminalFrame { Sequence: ulong.MaxValue })),
    ("verified exact input allowed", () => Check(InputPolicy.Evaluate(context, request).Allowed)),
    ("unverified control denied", () => Check(!InputPolicy.Evaluate(context with { ControlVerified = false }, request).Allowed)),
    ("observe denied", () => Check(!InputPolicy.Evaluate(context with { Access = TerminalAccess.Observing }, request).Allowed)),
    ("old epoch denied", () => Check(!InputPolicy.Evaluate(context, request with { Epoch = new ConnectionEpoch(2) }).Allowed)),
    ("other pane denied", () => Check(!InputPolicy.Evaluate(context, request with { Pane = pane with { PaneId = "p2" } }).Allowed)),
    ("emulator reply denied", () => Check(!InputPolicy.Evaluate(context, request with { Origin = InputOrigin.EmulatorReply }).Allowed)),
    ("empty input denied", () => Check(!InputPolicy.Evaluate(context, request with { Bytes = ReadOnlyMemory<byte>.Empty }).Allowed)),
    ("oversize input denied", () => Check(!InputPolicy.Evaluate(context, request with { Bytes = new byte[InputPolicy.MaxInputBytes + 1] }).Allowed)),
    ("unknown origin denied", () => Check(!InputPolicy.Evaluate(context, request with { Origin = (InputOrigin)999 }).Allowed)),
    ("default identity denied", () => Check(!InputPolicy.Evaluate(context with { ActivePane = default }, request with { Pane = default }).Allowed)),
    ("unpaired surrogate type value", () => RejectMalformedThenLatch(edge["unpaired_surrogate_type_value"])),
    ("unpaired surrogate property name", () => RejectMalformedThenLatch(edge["unpaired_surrogate_property_name"])),
    ("unpaired surrogate closed reason", () => RejectMalformedThenLatch(edge["unpaired_surrogate_closed_reason"])),
    ("valid surrogate pair accepted", () => Check(new TerminalFrameParser().Parse(edge["valid_surrogate_pair_closed_reason"]) is TerminalClosed { ReasonPresent: true })),
    ("endpoint fixture is not runtime proof", () =>
    {
        using var document = LoadJsonFixture("endpoint-cases.json");
        var root = document.RootElement;
        Check(root.GetProperty("simulation").GetBoolean());
        Check(root.GetProperty("runtime_pass").GetBoolean() is false);
        Check(root.GetProperty("windows_verified").GetBoolean() is false);
        Check(root.GetProperty("ac03_passed").GetBoolean() is false);
        Check(root.GetProperty("bridge_implemented").GetBoolean() is false);
        Check(root.GetProperty("all_live_checks").GetString() == "blocked");
        Check(root.GetProperty("blocked_category").GetString() ==
            "no_authorized_isolated_windows_endpoint_or_live_herdr_grant");
        var count = 0;
        foreach (var item in root.GetProperty("cases").EnumerateArray())
        {
            count++;
            AssertEndpointCase(item);
        }
        Check(count == 7);
    }),
    ("endpoint default does not guess APPDATA", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var session = new SessionKey(device, "local-api", null);
        var result = EndpointResolver.Resolve(device, session, EndpointPreference.Default, EndpointResolutionConfig.Empty);
        Check(!result.Resolved && result.Failure is not null);
        Check(result.Failure!.Code == "explicit_configuration_required");
        Check(result.Failure.RequiresExplicitConfiguration);
        AssertRedacted(result.Failure.Code);
        AssertRedacted(result.Failure.DiagnosticId);
        Check(result.Endpoint is null);
    }),
    ("endpoint named does not fall back to default", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var session = new SessionKey(device, "local-api", "work");
        var config = new EndpointResolutionConfig(
            [new VerifiedEndpointMapping(device, "local-api", null, @"\\.\pipe\herdr-api",
                EndpointKind.NamedPipe, "synthetic-default-mapping", EndpointAccessScope.LocalUser)],
            Array.Empty<EndpointObservation>());
        var result = EndpointResolver.Resolve(device, session, EndpointPreference.Named("work"), config);
        Check(!result.Resolved && result.Failure is not null);
        Check(result.Failure!.Code == "named_session_unmapped");
        Check(result.Failure.RequiresExplicitConfiguration);
        Check(result.Endpoint is null);
        AssertRedacted(result.Failure.Code);
        AssertRedacted(result.Failure.DiagnosticId);
    }),
    ("endpoint rejects UNC and does not convert to local", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var session = new SessionKey(device, "local-api", null);
        var config = new EndpointResolutionConfig(
            [new VerifiedEndpointMapping(device, "local-api", null, @"\\.\pipe\herdr-api",
                EndpointKind.NamedPipe, "synthetic-default-mapping", EndpointAccessScope.LocalUser)],
            Array.Empty<EndpointObservation>());
        foreach (var location in new[] { @"\\fileserver\share\herdr", @"\\?\UNC\fileserver\share", @"\\fileserver\pipe\herdr-api", "//fileserver/share/herdr" })
        {
            var result = EndpointResolver.Resolve(device, session, EndpointPreference.Explicit(location), config);
            Check(!result.Resolved && result.Failure is not null);
            Check(result.Failure!.Code == "remote_unc_rejected");
            Check(result.Endpoint is null);
            AssertRedacted(result.Failure.Code);
            AssertRedacted(result.Failure.DiagnosticId);
        }
    }),
    ("endpoint pane key is not identity", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var session = new SessionKey(device, "local-api", null);
        var windowPane = new PaneKey(session, "w1", "p1");
        var config = new EndpointResolutionConfig(
            [new VerifiedEndpointMapping(device, "local-api", "work", @"\\.\pipe\herdr-work",
                EndpointKind.NamedPipe, "synthetic-named-mapping", EndpointAccessScope.LocalUser)],
            Array.Empty<EndpointObservation>());
        var byPane = EndpointResolver.Resolve(device, session, EndpointPreference.Named(windowPane.PaneId), config);
        Check(!byPane.Resolved && byPane.Failure is not null);
        Check(byPane.Failure!.Code == "named_session_unmapped");
        var byTitle = EndpointResolver.Resolve(device, session, EndpointPreference.Explicit("Claude Code"), EndpointResolutionConfig.Empty);
        Check(!byTitle.Resolved && byTitle.Failure is not null);
        Check(byTitle.Failure!.Code == "explicit_configuration_required");
        var byAgent = EndpointResolver.Resolve(device, session, EndpointPreference.Explicit("claude-code"), EndpointResolutionConfig.Empty);
        Check(!byAgent.Resolved && byAgent.Failure is not null);
        Check(byAgent.Failure!.Code == "explicit_configuration_required");
    }),
    ("endpoint explicit does not rewrite to another path", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var session = new SessionKey(device, "local-api", null);
        var config = new EndpointResolutionConfig(
            [new VerifiedEndpointMapping(device, "local-api", null, @"\\.\pipe\herdr-api",
                EndpointKind.NamedPipe, "synthetic-default-mapping", EndpointAccessScope.LocalUser)],
            Array.Empty<EndpointObservation>());
        var result = EndpointResolver.Resolve(
            device, session, EndpointPreference.Explicit(@"\\.\pipe\herdr-other"), config);
        Check(result.Resolved && result.Endpoint is not null);
        Check(result.Endpoint!.CanonicalLocation == @"\\.\pipe\herdr-other");
    }),
    ("endpoint APPDATA token is not expanded", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var session = new SessionKey(device, "local-api", null);
        var result = EndpointResolver.Resolve(
            device, session, EndpointPreference.Explicit(@"%APPDATA%\herdr\api.sock"), EndpointResolutionConfig.Empty);
        Check(!result.Resolved && result.Failure is not null);
        Check(result.Failure!.Code == "explicit_configuration_required");
        AssertRedacted(result.Failure.Code);
        AssertRedacted(result.Failure.DiagnosticId);
    }),
    ("endpoint unpaired surrogate rejected", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var session = new SessionKey(device, "local-api", null);
        var result = EndpointResolver.Resolve(
            device, session, EndpointPreference.Explicit("\\\\.\\pipe\\\ud800"), EndpointResolutionConfig.Empty);
        Check(!result.Resolved && result.Failure is not null);
        Check(result.Failure!.Code == "unicode_encoding_error");
        AssertRedacted(result.Failure.Code);
        AssertRedacted(result.Failure.DiagnosticId);
    }),
    ("endpoint empty identity rejected", () =>
    {
        var result = EndpointResolver.Resolve(default, default, EndpointPreference.Default, EndpointResolutionConfig.Empty);
        Check(!result.Resolved && result.Failure is not null);
        Check(result.Failure!.Code == "invalid_identity");
    }),
    ("endpoint other device mapping is ignored", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var other = new DeviceId(Guid.Parse("22222222-2222-4222-8222-222222222222"));
        var session = new SessionKey(device, "local-api", null);
        var config = new EndpointResolutionConfig(
            [new VerifiedEndpointMapping(other, "local-api", null, @"\\.\pipe\herdr-api",
                EndpointKind.NamedPipe, "synthetic-default-mapping", EndpointAccessScope.LocalUser)],
            Array.Empty<EndpointObservation>());
        var result = EndpointResolver.Resolve(device, session, EndpointPreference.Default, config);
        Check(!result.Resolved && result.Failure is not null);
        Check(result.Failure!.Code == "explicit_configuration_required");
        Check(result.Endpoint is null);
    }),
    ("endpoint conventional pipe name is not guessed", () =>
    {
        var device = new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var session = new SessionKey(device, "local-api", null);
        var bare = EndpointResolver.Resolve(
            device, session, EndpointPreference.Explicit("herdr-api"), EndpointResolutionConfig.Empty);
        Check(!bare.Resolved && bare.Failure is not null);
        Check(bare.Failure!.Code == "explicit_configuration_required");
        Check(bare.Endpoint is null);
        var deviceNs = EndpointResolver.Resolve(
            device, session, EndpointPreference.Explicit("//./herdr-api"), EndpointResolutionConfig.Empty);
        Check(!deviceNs.Resolved && deviceNs.Failure is not null);
        Check(deviceNs.Failure!.Code == "explicit_configuration_required");
        Check(deviceNs.Endpoint is null);
    }),
    ("lease fixture is not runtime proof", () =>
    {
        using var document = LoadJsonFixture("lease-cases.json");
        var root = document.RootElement;
        Check(root.GetProperty("simulation").GetBoolean());
        Check(root.GetProperty("runtime_pass").GetBoolean() is false);
        Check(root.GetProperty("windows_verified").GetBoolean() is false);
        Check(root.GetProperty("ac05_passed").GetBoolean() is false);
        Check(root.GetProperty("herdr_executed").GetBoolean() is false);
        Check(root.GetProperty("real_captures").GetBoolean() is false);
        Check(root.GetProperty("all_live_checks").GetString() == "blocked");
        Check(root.GetProperty("blocked_category").GetString() ==
            "no_authorized_isolated_pane_or_live_herdr_grant");
        var count = 0;
        foreach (var item in root.GetProperty("cases").EnumerateArray())
        {
            count++;
            AssertLeaseCase(item);
        }
        Check(count == 14);
    }),
    ("lease does not set ControlVerified from frame process or focus", () =>
    {
        var result = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Observing,
            false,
            FirstFrameSeen: true,
            ProcessAlive: true,
            WindowFocused: true));
        Check(result.Access == TerminalAccess.Acquiring);
        Check(!result.ControlVerified);
        Check(result.Code == "control_unconfirmed");
        var denied = InputPolicy.Evaluate(
            context with { Access = result.Access, ControlVerified = result.ControlVerified },
            request);
        Check(!denied.Allowed);
        Check(denied.Code == "control_not_verified");
    }),
    ("lease observe cannot send input", () =>
    {
        var result = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.Observe,
            TerminalAccess.Observing,
            false,
            FirstFrameSeen: true,
            InputSent: true));
        Check(result.Access == TerminalAccess.Observing);
        Check(!result.ControlVerified);
        Check(result.Code == "observe_input_denied");
    }),
    ("lease EOF is not pane exit", () =>
    {
        Check(TerminalLeaseProbe.ClassifyStreamEnd(true, false, false) ==
            TerminalStreamEndKind.StdoutEof);
        var eof = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.Observe,
            TerminalAccess.Observing,
            false,
            StdoutEofSeen: true,
            FirstFrameSeen: true));
        Check(eof.StreamEnd == TerminalStreamEndKind.StdoutEof);
        Check(!eof.PaneExitVerified);
        Check(!eof.ControlVerified);
        var closed = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.Observe,
            TerminalAccess.Observing,
            false,
            TerminalClosedSeen: true));
        Check(closed.StreamEnd == TerminalStreamEndKind.TerminalClosed);
        Check(!closed.PaneExitVerified);
    }),
    ("lease fictional granted is not a grant", () =>
    {
        var result = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Observing,
            false,
            AdapterProvedWriteOwnership: true,
            ObservedWireType: "terminal.granted"));
        Check(result.Access == TerminalAccess.Unknown);
        Check(!result.ControlVerified);
        Check(result.Code == "fictional_granted_rejected");
    }),
    ("lease observe frame does not verify control for input", () =>
    {
        var mapped = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.Observe,
            TerminalAccess.Disconnected,
            false,
            FirstFrameSeen: true,
            ProcessAlive: true,
            WindowFocused: true));
        Check(mapped.Access == TerminalAccess.Observing);
        Check(!mapped.ControlVerified);
        Check(!InputPolicy.Evaluate(
            context with { Access = mapped.Access, ControlVerified = mapped.ControlVerified },
            request).Allowed);
    }),
    ("lease stdin write is not ControlVerified", () =>
    {
        var unknown = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Acquiring,
            false,
            InputSent: true));
        Check(unknown.Code == "input_result_unknown");
        Check(!unknown.ControlVerified);
        var acked = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Acquiring,
            false,
            InputSent: true,
            InputAcknowledged: true));
        Check(acked.Access == TerminalAccess.Acquiring);
        Check(acked.Code == "control_unconfirmed");
        Check(!acked.ControlVerified);
        Check(!InputPolicy.Evaluate(
            context with { Access = acked.Access, ControlVerified = acked.ControlVerified },
            request).Allowed);
    }),
    ("lease rejected is not a grant", () =>
    {
        var result = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Observing,
            false,
            ControlSignal: TerminalControlSignal.Rejected));
        Check(result.Access == TerminalAccess.Observing);
        Check(result.Code == "rejected");
        Check(!result.ControlVerified);
    }),
    ("lease observe adapter proof does not verify", () =>
    {
        var result = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.Observe,
            TerminalAccess.Disconnected,
            false,
            FirstFrameSeen: true,
            AdapterProvedWriteOwnership: true));
        Check(result.Access == TerminalAccess.Observing);
        Check(result.Code == "observing");
        Check(!result.ControlVerified);
    }),
    ("lease resize EOF is not verified control", () =>
    {
        var eof = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.ResizeWhileVerified,
            TerminalAccess.Controlling,
            true,
            StdoutEofSeen: true,
            AdapterProvedWriteOwnership: true,
            ResizeAttempted: true,
            ResizeAcknowledged: true));
        Check(eof.Access == TerminalAccess.Disconnected);
        Check(eof.StreamEnd == TerminalStreamEndKind.StdoutEof);
        Check(!eof.PaneExitVerified);
        Check(!eof.ControlVerified);
        Check(eof.Code == "disconnected");
        var dead = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.ResizeWhileVerified,
            TerminalAccess.Controlling,
            true,
            PaneAliveObserved: false,
            AdapterProvedWriteOwnership: true,
            ResizeAttempted: true,
            ResizeAcknowledged: true));
        Check(dead.Access == TerminalAccess.Disconnected);
        Check(dead.PaneExitVerified);
        Check(!dead.ControlVerified);
        var stale = TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.ResizeWhileVerified,
            TerminalAccess.Controlling,
            true,
            ResizeAttempted: true));
        Check(stale.Code == "control_not_verified");
        Check(stale.Access != TerminalAccess.Controlling);
        Check(!stale.ControlVerified);
    }),
};
int failed = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name}"); }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} smoke tests passed; no live Windows/daemon validation.");
return failed == 0 ? 0 : 1;
