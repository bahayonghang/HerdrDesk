using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.SshTransports;
using OsProcess = System.Diagnostics.Process;

internal static class SshConnectionBudgetTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("pair start failure returns both slots", PairStartFailureReturnsSlots),
        ("auth failure returns the pair owner", AuthFailureReturnsOwner),
        ("cancel dispose returns the owner", CancelReturnsOwner),
        ("late old-epoch dispose does not free the new epoch", LateOldEpochDispose),
        ("one blocked device does not block another", BlockedDeviceIsolation),
        ("double dispose is a no-op", DoubleDispose),
        ("concurrent opens never exceed the injected ssh budget", ConcurrentOpens)
    ];

    static SshConnectionLease Open(
        ConnectionAdmissionPolicy policy,
        string mode,
        string root,
        SessionKey? session = null,
        long epoch = 1,
        ISshChildProcessStarter? starter = null)
    {
        var locator = SshTransportFixtures.ChannelLocator(mode);
        var launch = SshTransportFixtures.Launch(root, session, epoch);
        return SshConnectionLease.OpenAsync(
                policy, launch, locator, SshTransportFixtures.Probe(locator),
                starter: starter ?? new SshTransportFixtures.TerminalAwareStarter())
            .AsTask().GetAwaiter().GetResult();
    }

    static void PairStartFailureReturnsSlots()
    {
        var root = SshTransportFixtures.TempRoot();
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        var starter = new FailOnNthStarter(2);
        SshConnectionLease? lease = null;
        try
        {
            lease = Open(policy, "rpc", root, starter: starter);
            SshTransportFixtures.Check(!lease.Ready);
            SshTransportFixtures.Check(lease.Code == ResourceBudgetCodes.ProcessStartFailed);
            SshTransportFixtures.Check(policy.Snapshot().SshSlots == 0);
            SshTransportFixtures.Check(starter.Count == 2);
            SshTransportFixtures.Check(starter.StartedIds.Count == 1);
            var id = starter.StartedIds[0];
            SshTransportFixtures.WaitUntil(() =>
            {
                try
                {
                    return OsProcess.GetProcessById(id).HasExited;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            });
        }
        finally
        {
            lease?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void AuthFailureReturnsOwner()
    {
        var root = SshTransportFixtures.TempRoot();
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        SshConnectionLease? lease = null;
        try
        {
            lease = Open(policy, "banner", root);
            SshTransportFixtures.Check(!lease.Ready);
            SshTransportFixtures.Check(lease.Code == SshTransportCodes.StdoutProtocolPollution);
            SshTransportFixtures.Check(policy.Snapshot().SshSlots == 0);
        }
        finally
        {
            lease?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void CancelReturnsOwner()
    {
        var root = SshTransportFixtures.TempRoot();
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        SshConnectionLease? lease = null;
        try
        {
            lease = Open(policy, "rpc", root);
            SshTransportFixtures.Check(lease.Ready);
            SshTransportFixtures.Check(policy.Snapshot().SshSlots == 2);
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            lease = null;
            SshTransportFixtures.Check(policy.Snapshot().SshSlots == 0);
        }
        finally
        {
            lease?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void LateOldEpochDispose()
    {
        var root = SshTransportFixtures.TempRoot();
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(4));
        var session = SshTransportFixtures.Session();
        SshConnectionLease? first = null;
        SshConnectionLease? second = null;
        try
        {
            first = Open(policy, "rpc", root, session, 1);
            second = Open(policy, "rpc", root, session, 2);
            SshTransportFixtures.Check(first.Ready && second.Ready);
            SshTransportFixtures.Check(policy.Snapshot().SshSlots == 4);
            first.DisposeAsync().AsTask().GetAwaiter().GetResult();
            first.DisposeAsync().AsTask().GetAwaiter().GetResult();
            first = null;
            SshTransportFixtures.Check(second.Ready);
            SshTransportFixtures.Check(policy.Snapshot().SshSlots == 2);
        }
        finally
        {
            first?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            second?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void BlockedDeviceIsolation()
    {
        var root = SshTransportFixtures.TempRoot();
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        SshConnectionLease? blocked = null;
        SshConnectionLease? other = null;
        try
        {
            var sessionA = SshTransportFixtures.Session(SshTransportFixtures.DeviceA, name: "dev");
            var sessionB = SshTransportFixtures.Session(SshTransportFixtures.DeviceB, name: "dev");
            blocked = Open(policy, "banner", root, sessionA);
            other = Open(policy, "rpc", root, sessionB);
            SshTransportFixtures.Check(!blocked.Ready);
            SshTransportFixtures.Check(other.Ready);
            SshTransportFixtures.Check(policy.Snapshot().SshSlots == 2);
        }
        finally
        {
            blocked?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            other?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void DoubleDispose()
    {
        var root = SshTransportFixtures.TempRoot();
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(4));
        SshConnectionLease? lease = null;
        try
        {
            lease = Open(policy, "rpc", root);
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(policy.Snapshot().SshSlots == 0);
            lease = null;
        }
        finally
        {
            lease?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void ConcurrentOpens()
    {
        var root = SshTransportFixtures.TempRoot();
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(2, maxRemoteDevices: 8));
        var leases = new SshConnectionLease[4];
        try
        {
            Parallel.For(0, 4, i =>
            {
                var session = SshTransportFixtures.Session(
                    new DeviceId(Guid.Parse($"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeee{i:D2}")),
                    name: "dev");
                leases[i] = Open(policy, "rpc", root, session, 1);
            });
            SshTransportFixtures.Check(policy.HighWaterSshSlots <= 2);
            SshTransportFixtures.Check(policy.Snapshot().SshSlots <= 2);
            SshTransportFixtures.Check(leases.Count(item => item.Ready) <= 1);
        }
        finally
        {
            foreach (var lease in leases)
                lease?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    sealed class FailOnNthStarter : ISshChildProcessStarter
    {
        private readonly int _failAt;
        public int Count;
        public List<int> StartedIds { get; } = [];

        public FailOnNthStarter(int failAt) => _failAt = failAt;

        public OwnedChildProcess Start(SshProcessSpec spec)
        {
            Count++;
            if (Count == _failAt)
                throw new IOException("start_failed");
            var child = OwnedSshChildProcessStarter.Instance.Start(spec);
            StartedIds.Add(child.Id);
            return child;
        }
    }
}
