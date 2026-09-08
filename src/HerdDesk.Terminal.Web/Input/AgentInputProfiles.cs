namespace HerdDesk.Terminal.Web;

/// <summary>
/// L1 synthetic profiles for Claude Code / Codex / OpenCode. Muse is unknown.
/// LiveVerified stays false until an L3 desktop matrix exists.
/// </summary>
public static class AgentInputProfiles
{
    public static AgentInputProfile ClaudeCode { get; } =
        new("claude-code", "synthetic-l1", "pty", "bcl-renderer-l1");

    public static AgentInputProfile Codex { get; } =
        new("codex", "synthetic-l1", "pty", "bcl-renderer-l1");

    public static AgentInputProfile OpenCode { get; } =
        new("opencode", "synthetic-l1", "pty", "bcl-renderer-l1");

    public static AgentInputProfile Unknown { get; } =
        new("unknown", "", "", "");

    public static AgentInputProfile Resolve(
        string? agentName,
        string? version = null,
        string? shell = null,
        string? rendererVersion = null)
    {
        var name = (agentName ?? "").Trim().ToLowerInvariant();
        var seed = name switch
        {
            "claude" or "claude-code" => ClaudeCode,
            "codex" => Codex,
            "opencode" or "open-code" => OpenCode,
            _ => Unknown
        };
        if (seed.IsUnknown)
            return Unknown;
        return seed with
        {
            Version = string.IsNullOrWhiteSpace(version) ? seed.Version : version,
            Shell = string.IsNullOrWhiteSpace(shell) ? seed.Shell : shell,
            RendererVersion = string.IsNullOrWhiteSpace(rendererVersion)
                ? seed.RendererVersion
                : rendererVersion,
            LiveVerified = false
        };
    }
}
