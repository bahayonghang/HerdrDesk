using System.Collections.Concurrent;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ConnectionAdmissionPolicyTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("two session keys on one device admit separately", TwoSessionsOneDevice),
        ("rpc pair is all or nothing", PairAtomic),
        ("three devices and four terminals occupy ten slots", TenSlotSample),
        ("file and maintenance fill a thirteen slot test budget", ThirteenSlotSample),
        ("product file occupancy stays zero", ProductFileOccupancyZero),
        ("fourth device waits and is not online", ExtraDeviceWaits),
        ("fair wait rotates by device id", FairWaitRotation),
        ("idle sessions pause before a waiter stays waiting", IdlePauseThenWait),
        ("maintenance is refused while the device holds a file job", FileBlocksMaintenance),
        ("old epoch dispose does not free a new epoch", OldEpochDispose),
        ("double dispose is a no-op", DoubleDispose),
        ("invalid owners fail closed", InvalidOwnersFailClosed),
        ("thousand concurrent acquires never exceed injected ssh slots", ConcurrentCap),
        ("duplicate same-epoch pair does not wait or change phase", DuplicateSameEpoch),
        ("fourth device pauses an idle session", ExtraDevicePausesIdle)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static DeviceId Device(byte n) => new(new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

    static SessionKey Session(byte device, string name) =>
        new(Device(device), "endpoint", name);

    static PaneKey Pane(SessionKey session, string pane) => new(session, "ws", pane);

    static ConnectionEpoch Epoch(long value) => new(value);

    static void TwoSessionsOneDevice()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        var first = policy.TryAcquireRpcPair(Session(1, "dev"), Epoch(1));
        var second = policy.TryAcquireRpcPair(Session(1, "other"), Epoch(1));
        try
        {
            Check(first.Admitted && second.Admitted);
            Check(first.Pair is not null && second.Pair is not null);
            var snap = policy.Snapshot();
            Check(snap.RemoteDevices == 1);
            Check(snap.SshSlots == 4);
        }
        finally
        {
            first.Pair?.Dispose();
            second.Pair?.Dispose();
        }
    }

    static void PairAtomic()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(3));
        var first = policy.TryAcquireRpcPair(Session(1, "dev"), Epoch(1));
        var second = policy.TryAcquireRpcPair(Session(2, "dev"), Epoch(1));
        try
        {
            Check(first.Admitted);
            Check(!second.Admitted);
            Check(second.Pair is null);
            Check(second.Code == ResourceBudgetCodes.ConnectionBudgetExhausted);
            Check(policy.Snapshot().SshSlots == 2);
        }
        finally
        {
            first.Pair?.Dispose();
        }

        Check(policy.Snapshot().SshSlots == 0);
    }

    static void TenSlotSample()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(16, fileJobsEnabled: false));
        var leases = new List<IDisposable>();
        try
        {
            for (byte i = 1; i <= 3; i++)
            {
                var admitted = policy.TryAcquireRpcPair(Session(i, "dev"), Epoch(1));
                Check(admitted.Admitted);
                leases.Add(admitted.Pair!);
            }

            var sessions = new[] { Session(1, "dev"), Session(2, "dev"), Session(3, "dev"), Session(1, "dev") };
            var names = new[] { "p1", "p2", "p3", "p4" };
            for (var i = 0; i < 4; i++)
            {
                var terminal = policy.TryAcquireTerminal(Pane(sessions[i], names[i]), Epoch(1));
                Check(terminal.Admitted);
                leases.Add(terminal.Lease!);
            }

            var snap = policy.Snapshot();
            Check(snap.SshSlots == 10);
            Check(snap.RemoteDevices == 3);
            Check(snap.Terminals == 4);
            Check(snap.FileJobs == 0);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    static void ThirteenSlotSample()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(13));
        var leases = new List<IDisposable>();
        try
        {
            for (byte i = 1; i <= 3; i++)
            {
                var admitted = policy.TryAcquireRpcPair(Session(i, "dev"), Epoch(1));
                Check(admitted.Admitted);
                leases.Add(admitted.Pair!);
            }

            for (var i = 0; i < 4; i++)
            {
                var terminal = policy.TryAcquireTerminal(
                    Pane(Session((byte)(i % 3 + 1), "dev"), "p" + i), Epoch(1));
                Check(terminal.Admitted);
                leases.Add(terminal.Lease!);
            }

            var fileA = policy.TryAcquireFileJob(Session(1, "dev"), Epoch(1), "file-a");
            var fileB = policy.TryAcquireFileJob(Session(2, "dev"), Epoch(1), "file-b");
            var fileExtra = policy.TryAcquireFileJob(Session(3, "dev"), Epoch(1), "file-c");
            var maint = policy.TryAcquireMaintenance(Session(3, "dev"), Epoch(1), "maint-1");
            Check(fileA.Admitted && fileB.Admitted);
            Check(!fileExtra.Admitted);
            Check(maint.Admitted);
            leases.Add(fileA.Lease!);
            leases.Add(fileB.Lease!);
            leases.Add(maint.Lease!);
            var snap = policy.Snapshot();
            Check(snap.SshSlots == 13);
            Check(snap.FileJobs == 2);
            Check(snap.Maintenance == 1);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    static void ProductFileOccupancyZero()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.Product);
        Check(!policy.SshSlotsMeasured);
        Check(!policy.Budgets.FileJobsEnabled);
        var file = policy.TryAcquireFileJob(Session(1, "dev"), Epoch(1), "file-a");
        Check(!file.Admitted);
        Check(policy.Snapshot().FileJobs == 0);
        Check(policy.Snapshot().SshSlots == 0);
    }

    static void ExtraDeviceWaits()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(16));
        var held = new List<RpcPairLease>();
        try
        {
            for (byte i = 1; i <= 3; i++)
            {
                var admitted = policy.TryAcquireRpcPair(Session(i, "dev"), Epoch(1));
                Check(admitted.Admitted);
                policy.SetSessionActivity(Session(i, "dev"), true, false, false);
                held.Add(admitted.Pair!);
            }

            var extra = policy.TryAcquireRpcPair(Session(4, "dev"), Epoch(1));
            Check(!extra.Admitted);
            Check(extra.Phase == ConnectionPhase.WaitingForCapacity);
            Check(extra.PauseCandidates.Count == 0);
            Check(policy.Snapshot().RemoteDevices == 3);
            Check(policy.PhaseOf(Session(4, "dev")) == ConnectionPhase.WaitingForCapacity);
        }
        finally
        {
            foreach (var lease in held)
                lease.Dispose();
        }
    }

    static void FairWaitRotation()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(2, maxRemoteDevices: 3));
        var a1 = Session(1, "one");
        var a2 = Session(1, "two");
        var b1 = Session(2, "one");
        var a3 = Session(1, "three");
        var first = policy.TryAcquireRpcPair(a1, Epoch(1));
        policy.SetSessionActivity(a1, true, false, false);
        try
        {
            Check(first.Admitted);
            Check(!policy.TryAcquireRpcPair(a2, Epoch(1)).Admitted);
            Check(!policy.TryAcquireRpcPair(b1, Epoch(1)).Admitted);
            Check(!policy.TryAcquireRpcPair(a3, Epoch(1)).Admitted);
            Check(policy.PeekFairWaiter() == b1);
            first.Pair!.Dispose();
            first = null!;
            Check(policy.PeekFairWaiter() == b1);
            var second = policy.TryAcquireRpcPair(b1, Epoch(1));
            try
            {
                Check(second.Admitted);
                Check(policy.PeekFairWaiter() == a2);
            }
            finally
            {
                second.Pair?.Dispose();
            }
        }
        finally
        {
            first?.Pair?.Dispose();
        }
    }

    static void IdlePauseThenWait()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(2));
        var idle = Session(1, "idle");
        var next = Session(2, "next");
        var first = policy.TryAcquireRpcPair(idle, Epoch(1));
        policy.SetSessionActivity(idle, false, false, false);
        try
        {
            Check(first.Admitted);
            var waiting = policy.TryAcquireRpcPair(next, Epoch(1));
            Check(!waiting.Admitted);
            Check(waiting.Phase == ConnectionPhase.WaitingForCapacity);
            Check(waiting.PauseCandidates.Contains(idle));
            Check(policy.PhaseOf(idle) == ConnectionPhase.PausedForCapacity);
            first.Pair!.Dispose();
            first = null!;
            var admitted = policy.TryAcquireRpcPair(next, Epoch(1));
            try
            {
                Check(admitted.Admitted);
                Check(policy.PhaseOf(idle) == ConnectionPhase.PausedForCapacity);
            }
            finally
            {
                admitted.Pair?.Dispose();
            }
        }
        finally
        {
            first?.Pair?.Dispose();
        }
    }

    static void FileBlocksMaintenance()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        var pair = policy.TryAcquireRpcPair(Session(1, "dev"), Epoch(1));
        var file = policy.TryAcquireFileJob(Session(1, "dev"), Epoch(1), "file-a");
        try
        {
            Check(file.Admitted);
            var maint = policy.TryAcquireMaintenance(Session(1, "dev"), Epoch(1), "maint-1");
            Check(!maint.Admitted);
            Check(policy.Snapshot().Maintenance == 0);
        }
        finally
        {
            file.Lease?.Dispose();
            pair.Pair?.Dispose();
        }
    }

    static void OldEpochDispose()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(4));
        var session = Session(1, "dev");
        var old = policy.TryAcquireRpcPair(session, Epoch(1));
        var next = policy.TryAcquireRpcPair(session, Epoch(2));
        try
        {
            Check(old.Admitted && next.Admitted);
            Check(policy.Snapshot().SshSlots == 4);
            old.Pair!.Dispose();
            Check(policy.Snapshot().SshSlots == 2);
            old.Pair.Dispose();
            Check(policy.Snapshot().SshSlots == 2);
        }
        finally
        {
            next.Pair?.Dispose();
        }

        Check(policy.Snapshot().SshSlots == 0);
    }

    static void DoubleDispose()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(4));
        var admitted = policy.TryAcquireRpcPair(Session(1, "dev"), Epoch(1));
        Check(admitted.Admitted);
        admitted.Pair!.Dispose();
        admitted.Pair.Dispose();
        Check(policy.Snapshot().SshSlots == 0);
    }

    static void InvalidOwnersFailClosed()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        var bad = policy.TryAcquireRpcPair(new SessionKey(new DeviceId(Guid.Empty), "", null), Epoch(1));
        Check(!bad.Admitted);
        Check(bad.Code == ResourceBudgetCodes.InvalidIdentity);
        var stale = policy.TryAcquireRpcPair(Session(1, "dev"), Epoch(0));
        Check(stale.Code == ResourceBudgetCodes.StaleEpoch);
        var lone = policy.TryAcquire(LeaseOwner.RequestRpc(Session(1, "dev"), Epoch(1)));
        Check(!lone.Admitted);
        Check(policy.Snapshot().SshSlots == 0);
    }

    static void DuplicateSameEpoch()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(4));
        var session = Session(1, "dev");
        var first = policy.TryAcquireRpcPair(session, Epoch(1));
        try
        {
            Check(first.Admitted);
            Check(policy.PhaseOf(session) == ConnectionPhase.Connecting);
            var again = policy.TryAcquireRpcPair(session, Epoch(1));
            Check(!again.Admitted);
            Check(again.Pair is null);
            Check(again.Phase == ConnectionPhase.Connecting);
            Check(policy.PhaseOf(session) == ConnectionPhase.Connecting);
            Check(policy.PeekFairWaiter() is null);
            Check(policy.Snapshot().SshSlots == 2);
            Check(policy.Snapshot().WaitingSessions == 0);
        }
        finally
        {
            first.Pair?.Dispose();
        }
    }

    static void ExtraDevicePausesIdle()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(16));
        var idle = Session(1, "dev");
        var busyA = Session(2, "dev");
        var busyB = Session(3, "dev");
        var extra = Session(4, "dev");
        var held = new List<RpcPairLease>();
        try
        {
            var first = policy.TryAcquireRpcPair(idle, Epoch(1));
            Check(first.Admitted);
            policy.SetSessionActivity(idle, false, false, false);
            held.Add(first.Pair!);
            foreach (var session in new[] { busyA, busyB })
            {
                var admitted = policy.TryAcquireRpcPair(session, Epoch(1));
                Check(admitted.Admitted);
                policy.SetSessionActivity(session, true, false, false);
                held.Add(admitted.Pair!);
            }

            var waiting = policy.TryAcquireRpcPair(extra, Epoch(1));
            Check(!waiting.Admitted);
            Check(waiting.Phase == ConnectionPhase.WaitingForCapacity);
            Check(waiting.PauseCandidates.Contains(idle));
            Check(policy.PhaseOf(idle) == ConnectionPhase.PausedForCapacity);
            Check(policy.PhaseOf(extra) == ConnectionPhase.WaitingForCapacity);
            Check(policy.PhaseOf(extra) != ConnectionPhase.Ready);
            Check(policy.Snapshot().RemoteDevices == 3);
        }
        finally
        {
            foreach (var lease in held)
                lease.Dispose();
        }
    }

    static void ConcurrentCap()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8, maxRemoteDevices: 32));
        var held = new ConcurrentBag<RpcPairLease>();
        Parallel.For(0, 1000, i =>
        {
            var device = Device((byte)(i % 30 + 1));
            var session = new SessionKey(device, "endpoint", "s" + i);
            var admitted = policy.TryAcquireRpcPair(session, Epoch(1));
            if (admitted.Admitted && admitted.Pair is not null)
                held.Add(admitted.Pair);
        });
        try
        {
            Check(policy.HighWaterSshSlots <= 8);
            Check(policy.Snapshot().SshSlots <= 8);
            Check(held.Count <= 4);
        }
        finally
        {
            foreach (var lease in held)
                lease.Dispose();
        }

        Check(policy.Snapshot().SshSlots == 0);
    }
}
