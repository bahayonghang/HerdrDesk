using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed record DeviceConnectionSummary(
    DeviceId Device,
    string DisplayLabel,
    ConnectionPhase Phase,
    DeviceFreshness Freshness,
    PartitionReadiness Readiness,
    string? ErrorCode,
    bool WritesEnabled,
    string? WriteDisabledReason,
    StatusPresentation Status,
    bool ReconnectEnabled,
    string? ReconnectReason,
    string ConnectionStatus,
    string SyncStatus,
    string TerminalStatus,
    string FileStatus);
