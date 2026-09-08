using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class PaneVisibilityBudgetTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("hide one hundred times leaves no terminal slot", HideOneHundred),
        ("restore is observe on a new epoch", RestoreNewEpochObserve),
        ("fifth pane waits and does not steal focus", FifthPaneWaits),
        ("cached projection is not live", CachedProjectionNotLive)
    ];

    static PaneKey Pane(string pane, byte device = 1) =>
        new(new SessionKey(new DeviceId(new Guid(device, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)), "endpoint", "dev"),
            "ws", pane);

    static void HideOneHundred()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(16));
        var host = new FakeHost();
        var coordinator = new PaneVisibilityCoordinator(policy, host);
        var pane = Pane("p1");
        for (var i = 0; i < 100; i++)
        {
            var shown = coordinator.Show(pane, true);
            AppTestHost.Check(shown.Accepted);
            AppTestHost.Check(shown.ObserveOnly);
            AppTestHost.Check(!shown.Live);
            coordinator.NoteFullBaseline(pane, shown.Epoch);
            AppTestHost.Check(coordinator.IsLive(pane));
            var hidden = coordinator.Hide(pane);
            AppTestHost.Check(hidden.Visibility == PaneVisibilityKind.Hidden);
            AppTestHost.Check(!coordinator.IsLive(pane));
        }

        AppTestHost.Check(host.Terminals == 0);
        AppTestHost.Check(host.Renderers == 0);
        AppTestHost.Check(policy.Snapshot().Terminals == 0);
        AppTestHost.Check(!coordinator.InputReplayed);
        AppTestHost.Check(!coordinator.ControlRestored);
    }

    static void RestoreNewEpochObserve()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        var host = new FakeHost();
        var coordinator = new PaneVisibilityCoordinator(policy, host);
        var pane = Pane("p1");
        var first = coordinator.Show(pane, true);
        AppTestHost.Check(first.Accepted);
        AppTestHost.Check(first.Epoch.Value == 1);
        AppTestHost.Check(first.ObserveOnly);
        AppTestHost.Check(!first.Live);
        coordinator.NoteFullBaseline(pane, first.Epoch);
        coordinator.Hide(pane);
        var second = coordinator.Show(pane, true);
        AppTestHost.Check(second.Accepted);
        AppTestHost.Check(second.Epoch.Value > first.Epoch.Value);
        AppTestHost.Check(second.ObserveOnly);
        AppTestHost.Check(!second.Live);
        AppTestHost.Check(!second.CachedProjectionIsLive);
        AppTestHost.Check(host.Modes[pane] == TerminalMode.Observe);
        AppTestHost.Check(!coordinator.ControlRestored);
        AppTestHost.Check(!coordinator.InputReplayed);
        coordinator.Hide(pane);
    }

    static void FifthPaneWaits()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(16));
        var host = new FakeHost();
        var coordinator = new PaneVisibilityCoordinator(policy, host);
        var focus = Pane("p1");
        AppTestHost.Check(coordinator.Show(focus, true).Accepted);
        for (var i = 2; i <= 4; i++)
            AppTestHost.Check(coordinator.Show(Pane("p" + i), false).Accepted);
        var fifth = coordinator.Show(Pane("p5"), true);
        AppTestHost.Check(!fifth.Accepted);
        AppTestHost.Check(fifth.Code == ResourceBudgetCodes.SwitchPaneRequired);
        AppTestHost.Check(fifth.Visibility == PaneVisibilityKind.WaitingForCapacity);
        AppTestHost.Check(coordinator.VisibilityOf(focus) == PaneVisibilityKind.Visible);
        AppTestHost.Check(coordinator.VisibleCount == 4);
        AppTestHost.Check(fifth.NonFocusPanes.Count == 3);
        AppTestHost.Check(fifth.StatusText == ShellStrings.WaitingForCapacity);
        AppTestHost.Check(!fifth.CachedProjectionIsLive);
        AppTestHost.Check(policy.Snapshot().Terminals == 4);
        for (var i = 1; i <= 4; i++)
            coordinator.Hide(Pane("p" + i));
    }

    static void CachedProjectionNotLive()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        var host = new FakeHost { KeepPair = true };
        var coordinator = new PaneVisibilityCoordinator(policy, host);
        var pane = Pane("p1");
        var shown = coordinator.Show(pane, true);
        coordinator.NoteFullBaseline(pane, shown.Epoch);
        AppTestHost.Check(coordinator.IsLive(pane));
        coordinator.Hide(pane);
        AppTestHost.Check(!coordinator.IsLive(pane));
        AppTestHost.Check(coordinator.StatusText(pane) == ShellStrings.PausedForCapacity);
        var restored = coordinator.Show(pane, true);
        AppTestHost.Check(!restored.Live);
        AppTestHost.Check(!restored.CachedProjectionIsLive);
        AppTestHost.Check(restored.StatusText == ShellStrings.OverloadedReobserve);
        coordinator.Hide(pane);
    }

    sealed class FakeHost : IPaneVisibilityHost
    {
        public int Terminals;
        public int Renderers;
        public bool KeepPair = true;
        public Dictionary<PaneKey, TerminalMode> Modes { get; } = [];
        public bool InputReplayed => false;
        public bool ControlRestored => false;

        public void SetReadOnly(PaneKey pane, bool readOnly) => _ = (pane, readOnly);

        public void CancelTerminal(PaneKey pane)
        {
            _ = pane;
            if (Terminals > 0)
                Terminals--;
        }

        public void DestroyRenderer(PaneKey pane)
        {
            _ = pane;
            if (Renderers > 0)
                Renderers--;
        }

        public bool TryKeepRpcPair(SessionKey session)
        {
            _ = session;
            return KeepPair;
        }

        public ConnectionEpoch BeginObserve(PaneKey pane, ConnectionEpoch epoch)
        {
            Terminals++;
            Renderers++;
            Modes[pane] = TerminalMode.Observe;
            return epoch;
        }
    }
}
