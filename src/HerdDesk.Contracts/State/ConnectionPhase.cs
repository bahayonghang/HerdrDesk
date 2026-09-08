namespace HerdDesk.Contracts;

public enum ConnectionPhase
{
    Offline,
    Connecting,
    Synchronizing,
    Ready,
    Stale,
    Incompatible
}
