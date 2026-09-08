using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Ssh;

internal static class HelperRemoteScripts
{
    public const string PrivateRoot = ".herddesk/helper";
    public const string PayloadName = "payload";
    public const string FinalName = "herddesk-bridge";
    public const string HashSha256Sum = "sha256sum";
    public const string HashShasum = "shasum";
    public const int MaxPayloadBytes = 32 * 1024 * 1024;
    public const int MaxProbeBytes = 2048;

    public static readonly IReadOnlyList<string> DeployableTriples =
    [
        "x86_64-unknown-linux-gnu",
        "aarch64-unknown-linux-gnu",
        "x86_64-apple-darwin",
        "aarch64-apple-darwin"
    ];

    public const string Probe = """
        set -eu
        s=$(uname -s)
        m=$(uname -m)
        case "$s" in
          Linux) s=linux ;;
          Darwin) s=darwin ;;
          *) s=unknown ;;
        esac
        printf '%s\n%s\n%s\n' "$s" "$m" "$HOME"
        """;

    public const string Bootstrap = """
        set -eu
        umask 077
        version=$1
        target=$2
        expect_hash=$3
        expect_len=$4
        stage_id=$5
        hash_cmd=$6
        case "$version" in
          *[!A-Za-z0-9._-]*|"") echo helper_manifest_invalid; exit 10 ;;
        esac
        case "$target" in
          x86_64-unknown-linux-gnu|aarch64-unknown-linux-gnu|x86_64-apple-darwin|aarch64-apple-darwin) ;;
          *) echo helper_platform_unsupported; exit 11 ;;
        esac
        case "$expect_hash" in
          *[!0-9a-f]*|"") echo helper_manifest_invalid; exit 10 ;;
        esac
        [ ${#expect_hash} -eq 64 ] || { echo helper_manifest_invalid; exit 10; }
        case "$expect_len" in
          *[!0-9]*|""|0) echo helper_manifest_invalid; exit 10 ;;
        esac
        case "$stage_id" in
          *[!0-9a-f]*|"") echo helper_manifest_invalid; exit 10 ;;
        esac
        [ ${#stage_id} -eq 32 ] || { echo helper_manifest_invalid; exit 10; }
        case "$hash_cmd" in
          sha256sum|shasum) ;;
          *) echo helper_platform_unsupported; exit 11 ;;
        esac
        case "$HOME" in
          /*) ;;
          *) echo helper_platform_unsupported; exit 11 ;;
        esac
        case "$HOME" in
          *..*) echo helper_platform_unsupported; exit 11 ;;
        esac
        case "$HOME" in
          /usr|/usr/*|/opt|/opt/*) echo helper_platform_unsupported; exit 11 ;;
        esac
        reject_link() {
          if [ -L "$1" ]; then echo helper_upload_failed; exit 12; fi
        }
        ensure_dir() {
          reject_link "$1"
          if [ ! -d "$1" ]; then
            mkdir -m 0700 "$1" || { echo helper_upload_failed; exit 12; }
          fi
          reject_link "$1"
        }
        ensure_dir "$HOME/.herddesk"
        ensure_dir "$HOME/.herddesk/helper"
        ensure_dir "$HOME/.herddesk/helper/$version"
        dest="$HOME/.herddesk/helper/$version/$target"
        ensure_dir "$dest"
        stage="$dest/.stage-$stage_id"
        if ! mkdir -m 0700 "$stage" 2>/dev/null; then
          echo helper_upload_failed
          exit 12
        fi
        reject_link "$stage"
        cd -P "$stage" || { echo helper_upload_failed; exit 12; }
        cat > payload || { echo helper_upload_failed; rm -f payload; rmdir "$stage" 2>/dev/null || true; exit 12; }
        got_len=$(wc -c < payload)
        got_len=$(echo "$got_len" | tr -d ' \t')
        if [ "$got_len" != "$expect_len" ]; then
          echo helper_hash_mismatch
          rm -f payload
          rmdir "$stage" 2>/dev/null || true
          exit 13
        fi
        if [ "$hash_cmd" = sha256sum ]; then
          got_hash=$(sha256sum payload)
          got_hash=${got_hash%% *}
        else
          got_hash=$(shasum -a 256 payload)
          got_hash=${got_hash%% *}
        fi
        case "$got_hash" in
          *[!0-9a-f]*) echo helper_hash_mismatch; rm -f payload; rmdir "$stage" 2>/dev/null || true; exit 13 ;;
        esac
        if [ "$got_hash" != "$expect_hash" ]; then
          echo helper_hash_mismatch
          rm -f payload
          rmdir "$stage" 2>/dev/null || true
          exit 13
        fi
        chmod 0700 payload || { echo helper_upload_failed; rm -f payload; rmdir "$stage" 2>/dev/null || true; exit 12; }
        final="../herddesk-bridge"
        created_final=0
        if [ -e "$final" ] || [ -L "$final" ]; then
          if [ -L "$final" ]; then
            echo helper_version_collision
            rm -f payload
            rmdir "$stage" 2>/dev/null || true
            exit 14
          fi
          if [ "$hash_cmd" = sha256sum ]; then
            exist_hash=$(sha256sum "$final")
            exist_hash=${exist_hash%% *}
          else
            exist_hash=$(shasum -a 256 "$final")
            exist_hash=${exist_hash%% *}
          fi
          exist_len=$(wc -c < "$final")
          exist_len=$(echo "$exist_len" | tr -d ' \t')
          if [ "$exist_hash" != "$expect_hash" ] || [ "$exist_len" != "$expect_len" ]; then
            echo helper_version_collision
            rm -f payload
            rmdir "$stage" 2>/dev/null || true
            exit 14
          fi
        else
          if ! ln payload "$final" 2>/dev/null; then
            echo helper_platform_unsupported
            rm -f payload
            rmdir "$stage" 2>/dev/null || true
            exit 11
          fi
          created_final=1
          chmod 0700 "$final" || true
        fi
        if ! "$final" --version >/dev/null 2>&1; then
          echo helper_selftest_failed
          if [ "$created_final" -eq 1 ]; then
            rm -f "$final"
          fi
          rm -f payload
          rmdir "$stage" 2>/dev/null || true
          exit 15
        fi
        rm -f payload
        rmdir "$stage" 2>/dev/null || true
        echo helper_ok
        exit 0
        """;

    public const string Cleanup = """
        set -eu
        umask 077
        version=$1
        target=$2
        stage_id=$3
        case "$version" in
          *[!A-Za-z0-9._-]*|"") echo helper_cancelled; exit 0 ;;
        esac
        case "$target" in
          x86_64-unknown-linux-gnu|aarch64-unknown-linux-gnu|x86_64-apple-darwin|aarch64-apple-darwin) ;;
          *) echo helper_cancelled; exit 0 ;;
        esac
        case "$stage_id" in
          *[!0-9a-f]*|"") echo helper_cancelled; exit 0 ;;
        esac
        [ ${#stage_id} -eq 32 ] || { echo helper_cancelled; exit 0; }
        case "$HOME" in
          /*) ;;
          *) echo helper_cancelled; exit 0 ;;
        esac
        case "$HOME" in
          *..*) echo helper_cancelled; exit 0 ;;
        esac
        case "$HOME" in
          /usr|/usr/*|/opt|/opt/*) echo helper_cancelled; exit 0 ;;
        esac
        stage="$HOME/.herddesk/helper/$version/$target/.stage-$stage_id"
        if [ -d "$stage" ] && [ ! -L "$stage" ]; then
          rm -f "$stage/payload"
          rmdir "$stage" 2>/dev/null || true
        fi
        echo helper_cancelled
        exit 0
        """;

    private static readonly Regex VersionPattern = new("^[0-9A-Za-z][0-9A-Za-z._-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex StagingPattern = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex FileNamePattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex CommitPattern = new("^(not_run|[0-9a-f]{40})$", RegexOptions.CultureInvariant);
    private static readonly Regex AppVersionPattern = new("^[0-9A-Za-z][0-9A-Za-z._+-]{0,63}$", RegexOptions.CultureInvariant);

    public static bool IsSafeVersion(string value) => VersionPattern.IsMatch(value);
    public static bool IsSha256(string value) => Sha256Pattern.IsMatch(value);
    public static bool IsStagingId(string value) => StagingPattern.IsMatch(value);
    public static bool IsSafeFileName(string value) =>
        FileNamePattern.IsMatch(value) && !value.Contains("..", StringComparison.Ordinal);
    public static bool IsCommit(string value) => CommitPattern.IsMatch(value);
    public static bool IsAppVersion(string value) => AppVersionPattern.IsMatch(value);
    public static bool IsPositiveLength(long length) => length is > 0 and <= MaxPayloadBytes;
    public static bool IsHashCommand(string value) =>
        value is HashSha256Sum or HashShasum;
    public static bool IsDeployableTriple(string triple) => DeployableTriples.Contains(triple);

    public static bool IsForbiddenInstallHome(string home) =>
        home == "/usr" ||
        home.StartsWith("/usr/", StringComparison.Ordinal) ||
        home == "/opt" ||
        home.StartsWith("/opt/", StringComparison.Ordinal);

    public static string HashCommandFor(string os) =>
        os == "darwin" ? HashShasum : HashSha256Sum;

    public static string StagingName(string stagingId) => ".stage-" + stagingId;

    public static string PrivateDirectory(string home, string version, string triple) =>
        home.TrimEnd('/') + "/" + PrivateRoot + "/" + version + "/" + triple;

    public static string HomeSha256(string home)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(home));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string PayloadSha256(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    public static string HashPrefix(string sha256) =>
        sha256.Length <= 12 ? sha256 : sha256[..12];

    public static string NewStagingId() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static bool TryMapRemoteCode(string? stdout, out string code)
    {
        code = HelperCodes.UploadFailed;
        if (string.IsNullOrEmpty(stdout))
            return false;
        var line = stdout.Replace("\r", "", StringComparison.Ordinal).Trim();
        var newline = line.IndexOf('\n');
        if (newline >= 0)
            line = line[..newline].Trim();
        code = line switch
        {
            "helper_ok" => HelperCodes.Ok,
            "helper_manifest_invalid" => HelperCodes.ManifestInvalid,
            "helper_platform_unsupported" => HelperCodes.PlatformUnsupported,
            "helper_hash_mismatch" => HelperCodes.HashMismatch,
            "helper_version_collision" => HelperCodes.VersionCollision,
            "helper_selftest_failed" => HelperCodes.SelftestFailed,
            "helper_upload_failed" => HelperCodes.UploadFailed,
            "helper_cancelled" => HelperCodes.Cancelled,
            _ => HelperCodes.UploadFailed
        };
        return line is "helper_ok" or "helper_manifest_invalid" or "helper_platform_unsupported"
            or "helper_hash_mismatch" or "helper_version_collision" or "helper_selftest_failed"
            or "helper_upload_failed" or "helper_cancelled";
    }
}
