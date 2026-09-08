namespace HerdDesk.Contracts;

public enum AgentStatusKind
{
    Idle,
    Working,
    Blocked,
    Done,
    Unknown
}

// AC10 TUI kinds. Muse/Qwen and other detector ids stay unknown.
public enum KnownAgentKind
{
    Claude,
    Codex,
    OpenCode
}

public enum AgentSessionRefKind
{
    Id,
    Path
}

public enum SplitDirectionKind
{
    Right,
    Down
}

public enum RpcEventKind
{
    WorkspaceCreated,
    WorkspaceUpdated,
    WorkspaceMetadataUpdated,
    WorkspaceClosed,
    WorkspaceRenamed,
    WorkspaceMoved,
    WorkspaceReordered,
    WorkspaceFocused,
    WorktreeCreated,
    WorktreeOpened,
    WorktreeRemoved,
    TabCreated,
    TabClosed,
    TabRenamed,
    TabMoved,
    TabFocused,
    PaneCreated,
    PaneClosed,
    PaneUpdated,
    PaneFocused,
    PaneMoved,
    PaneOutputChanged,
    PaneExited,
    PaneAgentDetected,
    PaneAgentStatusChanged,
    LayoutUpdated
}
