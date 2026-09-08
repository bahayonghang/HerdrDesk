using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public interface IPaneVisibilityHost
{
    void SetReadOnly(PaneKey pane, bool readOnly);
    void CancelTerminal(PaneKey pane);
    void DestroyRenderer(PaneKey pane);
    bool TryKeepRpcPair(SessionKey session);
    ConnectionEpoch BeginObserve(PaneKey pane, ConnectionEpoch epoch);
    bool InputReplayed { get; }
    bool ControlRestored { get; }
}

public sealed record PaneVisibilityDecision(
    bool Accepted,
    string Code,
    PaneVisibilityKind Visibility,
    ConnectionEpoch Epoch,
    bool Live,
    bool ObserveOnly,
    bool CachedProjectionIsLive,
    IReadOnlyList<PaneKey> NonFocusPanes,
    string StatusText);

public sealed class PaneVisibilityCoordinator
{
    public const int MaxVisiblePanes = ResourceBudgets.ProductGlobalTerminals;
    private readonly ConnectionAdmissionPolicy _admission;
    private readonly IPaneVisibilityHost _host;
    private readonly object _gate = new();
    private readonly Dictionary<SessionKey, RpcPairLease> _pairs = [];
    private readonly Dictionary<PaneKey, ConnectionLease> _terminals = [];
    private readonly Dictionary<PaneKey, ConnectionEpoch> _epochs = [];
    private readonly Dictionary<SessionKey, long> _sessionEpoch = [];
    private readonly HashSet<PaneKey> _visible = [];
    private readonly HashSet<PaneKey> _live = [];
    private readonly HashSet<PaneKey> _waiting = [];
    private PaneKey? _focused;

    public PaneVisibilityCoordinator(ConnectionAdmissionPolicy admission, IPaneVisibilityHost host)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(host);
        _admission = admission;
        _host = host;
    }

    public bool InputReplayed => false;
    public bool ControlRestored => false;
    public int VisibleCount
    {
        get { lock (_gate) return _visible.Count; }
    }

    public PaneVisibilityKind VisibilityOf(PaneKey pane)
    {
        lock (_gate)
        {
            if (_visible.Contains(pane))
                return PaneVisibilityKind.Visible;
            if (_waiting.Contains(pane))
                return PaneVisibilityKind.WaitingForCapacity;
            return PaneVisibilityKind.Hidden;
        }
    }

    public bool IsLive(PaneKey pane)
    {
        lock (_gate)
            return _live.Contains(pane);
    }

    public ConnectionEpoch EpochOf(PaneKey pane)
    {
        lock (_gate)
            return _epochs.GetValueOrDefault(pane);
    }

    public PaneVisibilityDecision Show(PaneKey pane, bool focused)
    {
        if (pane.Session.Device.Value == Guid.Empty || string.IsNullOrWhiteSpace(pane.PaneId))
            return Decision(false, ResourceBudgetCodes.InvalidIdentity, pane, PaneVisibilityKind.Hidden, default);

        lock (_gate)
        {
            if (_visible.Contains(pane))
            {
                if (focused)
                    _focused = pane;
                return Decision(true, "visible", pane, PaneVisibilityKind.Visible, _epochs[pane]);
            }

            if (_visible.Count >= MaxVisiblePanes)
            {
                _waiting.Add(pane);
                var nonFocus = _visible.Where(item => item != _focused).ToArray();
                return Decision(
                    false,
                    ResourceBudgetCodes.SwitchPaneRequired,
                    pane,
                    PaneVisibilityKind.WaitingForCapacity,
                    _epochs.GetValueOrDefault(pane),
                    nonFocus);
            }

            var acquiredPairNow = false;
            if (!_pairs.ContainsKey(pane.Session))
            {
                var epoch = NextSessionEpoch(pane.Session);
                var pair = _admission.TryAcquireRpcPair(pane.Session, epoch);
                if (!pair.Admitted || pair.Pair is null)
                {
                    _waiting.Add(pane);
                    return Decision(
                        false,
                        pair.Code,
                        pane,
                        PaneVisibilityKind.WaitingForCapacity,
                        epoch,
                        PauseHint());
                }

                _pairs[pane.Session] = pair.Pair;
                _sessionEpoch[pane.Session] = epoch.Value;
                acquiredPairNow = true;
            }

            var terminalEpoch = new ConnectionEpoch(_epochs.GetValueOrDefault(pane).Value + 1);
            if (terminalEpoch.Value <= 0)
                terminalEpoch = new ConnectionEpoch(1);
            var terminal = _admission.TryAcquireTerminal(pane, terminalEpoch);
            if (!terminal.Admitted || terminal.Lease is null)
            {
                if (acquiredPairNow && _pairs.Remove(pane.Session, out var unusedPair))
                    unusedPair.Dispose();
                _waiting.Add(pane);
                return Decision(
                    false,
                    terminal.Code,
                    pane,
                    PaneVisibilityKind.WaitingForCapacity,
                    terminalEpoch,
                    PauseHint());
            }

            _waiting.Remove(pane);
            _terminals[pane] = terminal.Lease;
            _epochs[pane] = terminalEpoch;
            _visible.Add(pane);
            _live.Remove(pane);
            if (focused)
                _focused = pane;
            _admission.SetSessionActivity(pane.Session, true, false, false);
            _host.SetReadOnly(pane, true);
            _host.BeginObserve(pane, terminalEpoch);
            return Decision(true, "observe", pane, PaneVisibilityKind.Visible, terminalEpoch);
        }
    }

    public PaneVisibilityDecision Hide(PaneKey pane)
    {
        lock (_gate)
        {
            _waiting.Remove(pane);
            if (_terminals.Remove(pane, out var lease))
                lease.Dispose();
            if (_visible.Remove(pane))
            {
                _host.SetReadOnly(pane, true);
                _host.CancelTerminal(pane);
                _host.DestroyRenderer(pane);
            }

            _live.Remove(pane);
            if (_focused == pane)
                _focused = _visible.Count == 0 ? null : _visible.First();
            var sessionVisible = _visible.Any(item => item.Session == pane.Session);
            _admission.SetSessionActivity(pane.Session, sessionVisible, false, false);
            if (!sessionVisible && _pairs.TryGetValue(pane.Session, out var pair))
            {
                if (!_host.TryKeepRpcPair(pane.Session))
                {
                    pair.Dispose();
                    _pairs.Remove(pane.Session);
                }
            }

            return Decision(
                true,
                "hidden",
                pane,
                PaneVisibilityKind.Hidden,
                _epochs.GetValueOrDefault(pane));
        }
    }

    public void NoteFullBaseline(PaneKey pane, ConnectionEpoch epoch)
    {
        lock (_gate)
        {
            if (_visible.Contains(pane) && _epochs.GetValueOrDefault(pane) == epoch)
                _live.Add(pane);
        }
    }

    public string StatusText(PaneKey pane)
    {
        lock (_gate)
        {
            if (_waiting.Contains(pane))
                return ShellStrings.WaitingForCapacity;
            if (_visible.Contains(pane))
                return _live.Contains(pane) ? ShellStrings.Observing : ShellStrings.OverloadedReobserve;
            return ShellStrings.PausedForCapacity;
        }
    }

    private ConnectionEpoch NextSessionEpoch(SessionKey session)
    {
        var next = _sessionEpoch.GetValueOrDefault(session) + 1;
        if (next <= 0)
            next = 1;
        _sessionEpoch[session] = next;
        return new ConnectionEpoch(next);
    }

    private PaneKey[] PauseHint() =>
        [.. _visible.Where(item => item != _focused)];

    private PaneVisibilityDecision Decision(
        bool accepted,
        string code,
        PaneKey pane,
        PaneVisibilityKind visibility,
        ConnectionEpoch epoch,
        IReadOnlyList<PaneKey>? nonFocus = null)
    {
        var live = accepted && visibility == PaneVisibilityKind.Visible && _live.Contains(pane);
        var status = visibility switch
        {
            PaneVisibilityKind.WaitingForCapacity => ShellStrings.WaitingForCapacity,
            PaneVisibilityKind.Visible when live => ShellStrings.Observing,
            PaneVisibilityKind.Visible => ShellStrings.OverloadedReobserve,
            _ => ShellStrings.PausedForCapacity
        };
        return new PaneVisibilityDecision(
            accepted,
            code,
            visibility,
            epoch,
            live,
            ObserveOnly: true,
            CachedProjectionIsLive: false,
            nonFocus ?? [],
            status);
    }
}
