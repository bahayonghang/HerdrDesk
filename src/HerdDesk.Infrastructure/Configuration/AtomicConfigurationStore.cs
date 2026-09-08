using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Configuration;

public sealed class AtomicConfigurationStore : IDeviceProfileStore
{
    public const int MaxDocumentBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "secret", "token", "private_key", "privatekey",
        "authorization", "credential", "terminal_text", "ansi", "input_text",
        "private_key_pem", "passphrase", "mfa", "keyboard_interactive", "pkcs11"
    };
    private static readonly HashSet<string> RootNames = new(StringComparer.Ordinal)
    {
        "schema_version", "revision", "devices"
    };
    private static readonly HashSet<string> DeviceNames = new(StringComparer.Ordinal)
    {
        "device_id", "label", "connection_kind", "verified_herdr_path", "sessions", "ssh"
    };
    private static readonly HashSet<string> SshNames = new(StringComparer.Ordinal)
    {
        "host_alias", "user", "port", "identity_file_path", "identity_agent",
        "proxy_jump_alias", "remote_herdr_path", "remote_helper_path", "auth_mode",
        "profile_revision"
    };
    private static readonly HashSet<string> SessionNames = new(StringComparer.Ordinal)
    {
        "kind", "endpoint_key", "session_name", "endpoint_kind", "canonical_location"
    };

    private readonly AppDataPaths _paths;
    private readonly ConfigurationStoreHooks _hooks;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AtomicConfigurationStore(AppDataPaths paths)
        : this(paths, null)
    {
    }

    internal AtomicConfigurationStore(AppDataPaths paths, ConfigurationStoreHooks? hooks)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _hooks = hooks ?? new ConfigurationStoreHooks();
    }

    public async ValueTask<ConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return LoadUnlocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<ConfigurationWriteResult> SaveDeviceAsync(
        DeviceProfile profile, long expectedRevision, CancellationToken cancellationToken = default) =>
        MutateAsync(expectedRevision, current => MergeDevice(current, profile), cancellationToken);

    public ValueTask<ConfigurationWriteResult> DeleteDeviceAsync(
        DeviceId device, long expectedRevision, CancellationToken cancellationToken = default) =>
        MutateAsync(expectedRevision, current => RemoveDevice(current, device), cancellationToken);

    public async ValueTask<ConfigurationWriteResult> RestoreFromBackupAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_paths.ConfigurationBackupFile))
                return FailWrite(ConfigurationCodes.BackupMissing);
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(_paths.ConfigurationBackupFile);
            }
            catch (IOException)
            {
                return FailWrite(ConfigurationCodes.BackupMissing);
            }
            var parsed = Parse(bytes);
            if (!parsed.Succeeded)
                return FailWrite(parsed.Code ?? ConfigurationCodes.Malformed);
            return CommitBytes(bytes, parsed.Snapshot!, replaceExisting: File.Exists(_paths.ConfigurationFile),
                writeBackup: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<ConfigurationWriteResult> MutateAsync(
        long expectedRevision,
        Func<ConfigurationSnapshot, ConfigurationWriteResult> mutate,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = LoadUnlocked();
            if (loaded.Code is ConfigurationCodes.Missing)
            {
                loaded = new(ConfigurationSnapshot.Empty, null);
            }
            else if (!loaded.Succeeded)
            {
                return FailWrite(loaded.Code ?? ConfigurationCodes.Malformed);
            }

            var current = loaded.Snapshot!;
            if (current.Revision != expectedRevision)
                return FailWrite(ConfigurationCodes.WriteConflict);
            var mutated = mutate(current);
            if (!mutated.Succeeded)
                return mutated;
            return CommitSnapshot(mutated.Snapshot!);
        }
        finally
        {
            _gate.Release();
        }
    }

    private ConfigurationLoadResult LoadUnlocked()
    {
        if (!File.Exists(_paths.ConfigurationFile))
            return new(null, ConfigurationCodes.Missing);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_paths.ConfigurationFile);
        }
        catch (IOException)
        {
            return new(null, ConfigurationCodes.Malformed);
        }
        return Parse(bytes);
    }

    private static ConfigurationWriteResult MergeDevice(
        ConfigurationSnapshot current, DeviceProfile profile)
    {
        var error = ValidateProfile(profile);
        if (error is not null)
            return FailWrite(error);
        var toSave = WithComputedSshRevision(current, profile);
        var devices = new List<DeviceProfile>(current.Devices.Count + 1);
        var replaced = false;
        foreach (var existing in current.Devices)
        {
            if (existing.Device == toSave.Device)
            {
                devices.Add(toSave);
                replaced = true;
            }
            else
            {
                devices.Add(existing);
            }
        }
        if (!replaced)
            devices.Add(toSave);
        return new(new ConfigurationSnapshot(
            ConfigurationSnapshot.CurrentSchemaVersion,
            current.Revision + 1,
            devices), null);
    }

    private static ConfigurationWriteResult RemoveDevice(
        ConfigurationSnapshot current, DeviceId device)
    {
        if (device.Value == Guid.Empty)
            return FailWrite(ConfigurationCodes.InvalidIdentity);
        var devices = current.Devices.Where(item => item.Device != device).ToArray();
        if (devices.Length == current.Devices.Count)
            return FailWrite(ConfigurationCodes.InvalidIdentity);
        return new(new ConfigurationSnapshot(
            ConfigurationSnapshot.CurrentSchemaVersion,
            current.Revision + 1,
            devices), null);
    }

    private ConfigurationWriteResult CommitSnapshot(ConfigurationSnapshot snapshot)
    {
        byte[] bytes;
        try
        {
            bytes = _hooks.Serialize is not null ? _hooks.Serialize(snapshot) : Serialize(snapshot);
        }
        catch (Exception)
        {
            return FailWrite(ConfigurationCodes.SerializeFailed);
        }
        return CommitBytes(bytes, snapshot, replaceExisting: File.Exists(_paths.ConfigurationFile),
            writeBackup: true);
    }

    private ConfigurationWriteResult CommitBytes(
        byte[] bytes,
        ConfigurationSnapshot snapshot,
        bool replaceExisting,
        bool writeBackup)
    {
        Directory.CreateDirectory(_paths.SettingsDirectory);
        var temp = Path.Combine(
            _paths.SettingsDirectory,
            $".device-profiles.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            _hooks.AfterTempFlushed?.Invoke(temp);
            if (_hooks.Commit is not null)
            {
                _hooks.Commit(temp, _paths.ConfigurationFile,
                    writeBackup && replaceExisting ? _paths.ConfigurationBackupFile : null);
            }
            else if (!replaceExisting)
            {
                File.Move(temp, _paths.ConfigurationFile);
            }
            else if (writeBackup)
            {
                File.Replace(temp, _paths.ConfigurationFile, _paths.ConfigurationBackupFile,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, _paths.ConfigurationFile, overwrite: true);
            }
            return new(snapshot, null);
        }
        catch (Exception)
        {
            TryDelete(temp);
            return FailWrite(ConfigurationCodes.ReplaceFailed);
        }
    }

    internal static byte[] Serialize(ConfigurationSnapshot snapshot)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", snapshot.SchemaVersion);
            writer.WriteNumber("revision", snapshot.Revision);
            writer.WritePropertyName("devices");
            writer.WriteStartArray();
            foreach (var device in snapshot.Devices)
            {
                writer.WriteStartObject();
                writer.WriteString("device_id", device.Device.Value);
                writer.WriteString("label", device.Label);
                writer.WriteString("connection_kind", device.ConnectionKind);
                writer.WriteString("verified_herdr_path", device.VerifiedHerdrPath);
                writer.WritePropertyName("sessions");
                writer.WriteStartArray();
                foreach (var session in device.Sessions)
                {
                    writer.WriteStartObject();
                    writer.WriteString("kind", KindName(session.Kind));
                    writer.WriteString("endpoint_key", session.EndpointKey);
                    if (session.SessionName is not null)
                        writer.WriteString("session_name", session.SessionName);
                    if (session.Endpoint is { } endpoint)
                        writer.WriteString("endpoint_kind", EndpointName(endpoint));
                    if (session.CanonicalLocation is not null)
                        writer.WriteString("canonical_location", session.CanonicalLocation);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                if (device.Ssh is { } ssh)
                    WriteSsh(writer, ssh);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return buffer.ToArray();
    }

    internal static ConfigurationLoadResult Parse(byte[] json)
    {
        if (json.Length is 0 or > MaxDocumentBytes)
            return new(null, ConfigurationCodes.Malformed);
        try
        {
            _ = StrictUtf8.GetCharCount(json);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(null, ConfigurationCodes.Malformed);
            var forbidden = FindForbidden(root);
            if (forbidden is not null)
                return new(null, ConfigurationCodes.ForbiddenField);
            if (!TryObjectNames(root, RootNames, out var namesError))
                return new(null, namesError);
            if (!TryGetInt32(root, "schema_version", out var version))
                return new(null, ConfigurationCodes.Malformed);
            if (version < 1)
                return new(null, ConfigurationCodes.Malformed);
            if (version > ConfigurationSnapshot.CurrentSchemaVersion)
                return new(null, ConfigurationCodes.VersionUnsupported);
            if (!TryGetInt64(root, "revision", out var revision) || revision < 0)
                return new(null, ConfigurationCodes.Malformed);
            if (!root.TryGetProperty("devices", out var devicesElement) ||
                devicesElement.ValueKind != JsonValueKind.Array)
                return new(null, ConfigurationCodes.Malformed);
            var devices = new List<DeviceProfile>();
            foreach (var deviceElement in devicesElement.EnumerateArray())
            {
                if (!TryReadDevice(deviceElement, out var device, out var code))
                    return new(null, code);
                devices.Add(device);
            }
            var snapshot = new ConfigurationSnapshot(version, revision, devices);
            var unique = ValidateSnapshot(snapshot);
            if (unique is not null)
                return new(null, unique);
            return new(snapshot, null);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException
                                       or FormatException)
        {
            return new(null, ConfigurationCodes.Malformed);
        }
    }

    private static bool TryReadDevice(JsonElement element, out DeviceProfile device, out string code)
    {
        device = null!;
        code = ConfigurationCodes.Malformed;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryObjectNames(element, DeviceNames, out code))
            return false;
        if (!TryGetString(element, "device_id", out var idText) || !Guid.TryParse(idText, out var id))
        {
            code = ConfigurationCodes.Malformed;
            return false;
        }
        if (!TryGetString(element, "label", out var label) ||
            !TryGetString(element, "connection_kind", out var kind) ||
            !TryGetString(element, "verified_herdr_path", out var path))
        {
            code = ConfigurationCodes.Malformed;
            return false;
        }
        if (!element.TryGetProperty("sessions", out var sessionsElement) ||
            sessionsElement.ValueKind != JsonValueKind.Array)
        {
            code = ConfigurationCodes.Malformed;
            return false;
        }
        var sessions = new List<SessionProfile>();
        foreach (var sessionElement in sessionsElement.EnumerateArray())
        {
            if (!TryReadSession(sessionElement, out var session, out code))
                return false;
            sessions.Add(session);
        }
        SshDeviceSettings? ssh = null;
        if (element.TryGetProperty("ssh", out var sshElement))
        {
            if (!TryReadSsh(sshElement, out ssh, out code))
                return false;
        }
        device = new DeviceProfile(new DeviceId(id), label, kind, path, sessions, ssh);
        code = ValidateProfile(device) ?? "";
        if (code.Length != 0)
        {
            device = null!;
            return false;
        }
        code = ConfigurationCodes.Malformed;
        return true;
    }

    private static bool TryReadSession(JsonElement element, out SessionProfile session, out string code)
    {
        session = null!;
        code = ConfigurationCodes.Malformed;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryObjectNames(element, SessionNames, out code))
            return false;
        if (!TryGetString(element, "kind", out var kindName) ||
            !TryGetString(element, "endpoint_key", out var endpointKey))
        {
            code = ConfigurationCodes.Malformed;
            return false;
        }
        if (!TryKind(kindName, out var kind))
        {
            code = ConfigurationCodes.InvalidProfile;
            return false;
        }
        string? sessionName = null;
        if (element.TryGetProperty("session_name", out var sessionNameElement))
        {
            if (sessionNameElement.ValueKind != JsonValueKind.String)
                return false;
            sessionName = sessionNameElement.GetString();
        }
        EndpointKind? endpoint = null;
        if (element.TryGetProperty("endpoint_kind", out var endpointKindElement))
        {
            if (endpointKindElement.ValueKind != JsonValueKind.String)
                return false;
            if (!TryEndpoint(endpointKindElement.GetString(), out var parsedEndpoint))
            {
                code = ConfigurationCodes.InvalidProfile;
                return false;
            }
            endpoint = parsedEndpoint;
        }
        string? location = null;
        if (element.TryGetProperty("canonical_location", out var locationElement))
        {
            if (locationElement.ValueKind != JsonValueKind.String)
                return false;
            location = locationElement.GetString();
        }
        session = new SessionProfile(kind, endpointKey, sessionName, endpoint, location);
        return true;
    }

    internal static string? ValidateProfile(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Device.Value == Guid.Empty)
            return ConfigurationCodes.InvalidIdentity;
        if (!IsSafeLabel(profile.Label))
            return ConfigurationCodes.InvalidProfile;
        if (LooksLikeSecret(profile.Label) || LooksLikeSecret(profile.VerifiedHerdrPath))
            return ConfigurationCodes.ForbiddenField;
        if (string.Equals(profile.ConnectionKind, ConnectionKinds.Local, StringComparison.Ordinal))
        {
            if (profile.Ssh is not null)
                return ConfigurationCodes.InvalidSshProfile;
        }
        else if (string.Equals(profile.ConnectionKind, ConnectionKinds.Ssh, StringComparison.Ordinal))
        {
            if (profile.Ssh is null)
                return ConfigurationCodes.InvalidSshProfile;
            var sshError = ValidateSsh(profile.Ssh);
            if (sshError is not null)
                return sshError;
        }
        else
        {
            return ConfigurationCodes.InvalidProfile;
        }
        if (!IsSafeHerdrPath(profile.VerifiedHerdrPath))
            return ConfigurationCodes.InvalidProfile;
        ArgumentNullException.ThrowIfNull(profile.Sessions);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in profile.Sessions)
        {
            var sessionError = ValidateSession(session);
            if (sessionError is not null)
                return sessionError;
            var key = session.EndpointKey + "\u001f" + (session.SessionName ?? "");
            if (!keys.Add(key))
                return ConfigurationCodes.DuplicateSession;
        }
        return null;
    }

    private static string? ValidateSnapshot(ConfigurationSnapshot snapshot)
    {
        var devices = new HashSet<Guid>();
        foreach (var device in snapshot.Devices)
        {
            if (!devices.Add(device.Device.Value))
                return ConfigurationCodes.InvalidProfile;
            var error = ValidateProfile(device);
            if (error is not null)
                return error;
        }
        return null;
    }

    private static string? ValidateSession(SessionProfile session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.EndpointKey))
            return ConfigurationCodes.InvalidProfile;
        switch (session.Kind)
        {
            case SessionProfileKind.LocalDefault:
                if (session.EndpointKey != SessionProfile.LocalDefaultEndpointKey)
                    return ConfigurationCodes.InvalidProfile;
                if (session.SessionName is not null || session.Endpoint is not null ||
                    session.CanonicalLocation is not null)
                    return ConfigurationCodes.InvalidProfile;
                return null;
            case SessionProfileKind.NamedSession:
                if (session.EndpointKey != SessionProfile.NamedSessionEndpointKey)
                    return ConfigurationCodes.InvalidProfile;
                if (string.IsNullOrWhiteSpace(session.SessionName) || HasUnsafeToken(session.SessionName))
                    return ConfigurationCodes.InvalidProfile;
                if (session.Endpoint is not null || session.CanonicalLocation is not null)
                    return ConfigurationCodes.InvalidProfile;
                return null;
            case SessionProfileKind.ExplicitEndpoint:
                if (session.Endpoint is null || string.IsNullOrWhiteSpace(session.CanonicalLocation))
                    return ConfigurationCodes.InvalidProfile;
                if (session.SessionName is not null && HasUnsafeToken(session.SessionName))
                    return ConfigurationCodes.InvalidProfile;
                if (!IsSafeEndpointLocation(session.CanonicalLocation, session.Endpoint.Value))
                    return ConfigurationCodes.InvalidProfile;
                if (!string.Equals(session.EndpointKey, session.CanonicalLocation, StringComparison.Ordinal))
                    return ConfigurationCodes.InvalidProfile;
                return null;
            default:
                return ConfigurationCodes.InvalidProfile;
        }
    }

    private static DeviceProfile WithComputedSshRevision(
        ConfigurationSnapshot current, DeviceProfile profile)
    {
        if (profile.Ssh is not { } ssh)
            return profile;
        var existing = current.Devices.FirstOrDefault(item => item.Device == profile.Device);
        long revision;
        if (existing?.Ssh is { } oldSsh)
        {
            revision = SshConnectionFieldsEqual(oldSsh, ssh)
                ? oldSsh.ProfileRevision
                : oldSsh.ProfileRevision + 1;
        }
        else
        {
            revision = ssh.ProfileRevision > 0 ? ssh.ProfileRevision : 1;
        }

        return profile with { Ssh = ssh with { ProfileRevision = revision } };
    }

    internal static bool SshConnectionFieldsEqual(SshDeviceSettings left, SshDeviceSettings right) =>
        left.HostAlias == right.HostAlias &&
        left.User == right.User &&
        left.Port == right.Port &&
        left.IdentityFilePath == right.IdentityFilePath &&
        left.IdentityAgent == right.IdentityAgent &&
        left.ProxyJumpAlias == right.ProxyJumpAlias &&
        left.RemoteHerdrPath == right.RemoteHerdrPath &&
        left.RemoteHelperPath == right.RemoteHelperPath &&
        left.AuthMode.Raw == right.AuthMode.Raw;

    private static void WriteSsh(Utf8JsonWriter writer, SshDeviceSettings ssh)
    {
        writer.WritePropertyName("ssh");
        writer.WriteStartObject();
        writer.WriteString("host_alias", ssh.HostAlias);
        if (ssh.User is not null)
            writer.WriteString("user", ssh.User);
        if (ssh.Port is { } port)
            writer.WriteNumber("port", port);
        if (ssh.IdentityFilePath is not null)
            writer.WriteString("identity_file_path", ssh.IdentityFilePath);
        if (ssh.IdentityAgent is not null)
            writer.WriteString("identity_agent", ssh.IdentityAgent);
        if (ssh.ProxyJumpAlias is not null)
            writer.WriteString("proxy_jump_alias", ssh.ProxyJumpAlias);
        writer.WriteString("remote_herdr_path", ssh.RemoteHerdrPath);
        if (ssh.RemoteHelperPath is not null)
            writer.WriteString("remote_helper_path", ssh.RemoteHelperPath);
        writer.WriteString("auth_mode", ssh.AuthMode.Raw);
        writer.WriteNumber("profile_revision", ssh.ProfileRevision);
        writer.WriteEndObject();
    }

    private static bool TryReadSsh(JsonElement element, out SshDeviceSettings ssh, out string code)
    {
        ssh = null!;
        code = ConfigurationCodes.Malformed;
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryObjectNames(element, SshNames, out code))
            return false;
        if (!TryGetString(element, "host_alias", out var hostAlias) ||
            !TryGetString(element, "remote_herdr_path", out var remoteHerdr) ||
            !TryGetString(element, "auth_mode", out var authRaw) ||
            !TryGetInt64(element, "profile_revision", out var revision))
        {
            code = ConfigurationCodes.Malformed;
            return false;
        }

        string? user = null;
        if (element.TryGetProperty("user", out var userElement))
        {
            if (userElement.ValueKind != JsonValueKind.String)
                return false;
            user = userElement.GetString();
        }

        int? port = null;
        if (element.TryGetProperty("port", out var portElement))
        {
            if (!portElement.TryGetInt32(out var parsedPort))
                return false;
            port = parsedPort;
        }

        string? identity = OptionalString(element, "identity_file_path");
        string? agent = OptionalString(element, "identity_agent");
        string? jump = OptionalString(element, "proxy_jump_alias");
        string? helper = OptionalString(element, "remote_helper_path");
        if ((element.TryGetProperty("identity_file_path", out _) && identity is null) ||
            (element.TryGetProperty("identity_agent", out _) && agent is null) ||
            (element.TryGetProperty("proxy_jump_alias", out _) && jump is null) ||
            (element.TryGetProperty("remote_helper_path", out _) && helper is null))
        {
            code = ConfigurationCodes.Malformed;
            return false;
        }

        ssh = new SshDeviceSettings(
            hostAlias,
            user,
            port,
            identity,
            agent,
            jump,
            remoteHerdr,
            helper,
            SshDeviceSettings.ParseAuthMode(authRaw),
            revision);
        return true;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static string? ValidateSsh(SshDeviceSettings ssh)
    {
        ArgumentNullException.ThrowIfNull(ssh);
        if (!IsSafeHostAlias(ssh.HostAlias))
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.User is not null && !IsSafeUser(ssh.User))
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.Port is { } port && (port < 1 || port > 65535))
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.IdentityFilePath is not null && !IsSafeHerdrPath(ssh.IdentityFilePath))
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.IdentityAgent is not null && !IsSafeAgentRef(ssh.IdentityAgent))
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.ProxyJumpAlias is not null && !IsSafeHostAlias(ssh.ProxyJumpAlias))
            return ConfigurationCodes.InvalidSshProfile;
        if (!IsSafePosixAbsolute(ssh.RemoteHerdrPath))
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.RemoteHelperPath is not null && !IsSafePosixAbsolute(ssh.RemoteHelperPath))
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.ProfileRevision < 0)
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.AuthMode.Known is { } known &&
            ssh.AuthMode.Raw != SshDeviceSettings.AuthModeWire(known))
            return ConfigurationCodes.InvalidSshProfile;
        if (ssh.AuthMode.Known == SshAuthMode.IdentityFile &&
            string.IsNullOrWhiteSpace(ssh.IdentityFilePath))
            return ConfigurationCodes.InvalidSshProfile;
        if (LooksLikeSecret(ssh.HostAlias) || LooksLikeSecret(ssh.User) ||
            LooksLikeSecret(ssh.IdentityFilePath) || LooksLikeSecret(ssh.IdentityAgent) ||
            LooksLikeSecret(ssh.ProxyJumpAlias) || LooksLikeSecret(ssh.RemoteHerdrPath) ||
            LooksLikeSecret(ssh.RemoteHelperPath) || LooksLikeSecret(ssh.AuthMode.Raw))
            return ConfigurationCodes.ForbiddenField;
        return null;
    }

    public static bool IsSafeHostAlias(string alias) =>
        !string.IsNullOrWhiteSpace(alias) &&
        alias.Length <= 255 &&
        alias[0] != '-' &&
        !HasControlOrSurrogate(alias) &&
        alias.IndexOfAny([' ', '\t', '/', '\\', '@', ',', '"', '\'', '%', '\0']) < 0;

    private static bool IsSafeUser(string user) =>
        user.Length is > 0 and <= 128 &&
        user[0] != '-' &&
        !HasControlOrSurrogate(user) &&
        user.IndexOfAny([' ', '\t', '/', '\\', '@', ',', '%', '\0']) < 0;

    private static bool IsSafeAgentRef(string agent) =>
        agent.Length is > 0 and <= 256 &&
        agent[0] != '-' &&
        !HasControlOrSurrogate(agent) &&
        !LooksLikeSecret(agent) &&
        agent.IndexOfAny(['\n', '\r', '\0']) < 0;

    public static bool IsSafePosixAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || HasControlOrSurrogate(path))
            return false;
        if (path[0] != '/' || path.StartsWith("//", StringComparison.Ordinal))
            return false;
        if (path.Contains('%', StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            return false;
        if (path.Contains('\\', StringComparison.Ordinal))
            return false;
        return true;
    }

    public static bool LooksLikeSecret(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        var lower = value.ToLowerInvariant();
        return lower.Contains("-----begin", StringComparison.Ordinal) ||
               lower.Contains("private key", StringComparison.Ordinal) ||
               lower.Contains("password=", StringComparison.Ordinal);
    }

    private static bool IsSafeLabel(string label) =>
        !string.IsNullOrWhiteSpace(label) &&
        label.Length <= 128 &&
        !HasControlOrSurrogate(label) &&
        label.IndexOfAny(['\\', '/', '\0']) < 0;

    private static bool IsSafeHerdrPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || HasControlOrSurrogate(path))
            return false;
        if (path.Contains('%', StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            return false;
        if (!Path.IsPathRooted(path))
            return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal) &&
            !path.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool IsSafeEndpointLocation(string location, EndpointKind kind)
    {
        if (string.IsNullOrWhiteSpace(location) || HasControlOrSurrogate(location))
            return false;
        if (location.IndexOfAny(['|', ';', '\n', '\r']) >= 0)
            return false;
        if (location.Contains("%APPDATA%", StringComparison.OrdinalIgnoreCase) ||
            location.Contains("%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase))
            return false;
        if (location.StartsWith(@"\\", StringComparison.Ordinal) &&
            !location.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
            return false;
        return kind switch
        {
            EndpointKind.NamedPipe => location.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase),
            EndpointKind.UnixSocket or EndpointKind.FilesystemPath => Path.IsPathRooted(location),
            _ => false
        };
    }

    private static bool HasUnsafeToken(string value) =>
        value.Length > 128 || HasControlOrSurrogate(value) || value.IndexOfAny(['\\', '/', '\0', '%']) >= 0;

    private static bool HasControlOrSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c))
                return true;
            if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return true;
                i++;
            }
        }
        return false;
    }

    private static string? FindForbidden(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ForbiddenNames.Contains(property.Name))
                        return property.Name;
                    var nested = FindForbidden(property.Value);
                    if (nested is not null)
                        return nested;
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindForbidden(item);
                    if (nested is not null)
                        return nested;
                }
                return null;
            case JsonValueKind.String:
                return LooksLikeSecret(element.GetString()) ? "secret_value" : null;
            default:
                return null;
        }
    }

    private static bool TryObjectNames(JsonElement element, HashSet<string> allowed, out string code)
    {
        code = ConfigurationCodes.Malformed;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (ForbiddenNames.Contains(property.Name))
            {
                code = ConfigurationCodes.ForbiddenField;
                return false;
            }
            if (!allowed.Contains(property.Name))
            {
                code = ConfigurationCodes.Malformed;
                return false;
            }
            if (!seen.Add(property.Name))
            {
                code = ConfigurationCodes.Malformed;
                return false;
            }
        }
        return true;
    }

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString();
        if (text is null)
            return false;
        value = text;
        return true;
    }

    private static bool TryGetInt32(JsonElement obj, string name, out int value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var element) && element.TryGetInt32(out value);
    }

    private static bool TryGetInt64(JsonElement obj, string name, out long value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var element) && element.TryGetInt64(out value);
    }

    private static bool TryKind(string name, out SessionProfileKind kind)
    {
        kind = default;
        switch (name)
        {
            case "local_default":
                kind = SessionProfileKind.LocalDefault;
                return true;
            case "named_session":
                kind = SessionProfileKind.NamedSession;
                return true;
            case "explicit_endpoint":
                kind = SessionProfileKind.ExplicitEndpoint;
                return true;
            default:
                return false;
        }
    }

    private static string KindName(SessionProfileKind kind) => kind switch
    {
        SessionProfileKind.LocalDefault => "local_default",
        SessionProfileKind.NamedSession => "named_session",
        SessionProfileKind.ExplicitEndpoint => "explicit_endpoint",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool TryEndpoint(string? name, out EndpointKind kind)
    {
        kind = default;
        switch (name)
        {
            case "named_pipe":
                kind = EndpointKind.NamedPipe;
                return true;
            case "unix_socket":
                kind = EndpointKind.UnixSocket;
                return true;
            case "filesystem_path":
                kind = EndpointKind.FilesystemPath;
                return true;
            default:
                return false;
        }
    }

    private static string EndpointName(EndpointKind kind) => kind switch
    {
        EndpointKind.NamedPipe => "named_pipe",
        EndpointKind.UnixSocket => "unix_socket",
        EndpointKind.FilesystemPath => "filesystem_path",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static ConfigurationWriteResult FailWrite(string code) => new(null, code);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
