using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Ssh;

internal static class HelperDeploymentReceiptStoreTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("same device two sessions keep separate receipts", SessionIsolation),
        ("activate failure leaves previous current", AtomicFailure),
        ("old epoch release does not drop a newer lease", EpochLease),
        ("rollback switches current only", RollbackSwitches)
    ];

    static HelperReceiptKey Key(SessionKey session, string home = "/home/lab") =>
        new(session.Device, session.EndpointKey, session.SessionName, HelperFixtures.LinuxX64,
            HelperRemoteScripts.HomeSha256(home));

    static void SessionIsolation()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var store = new HelperDeploymentReceiptStore(AppDataPaths.FromRoot(root));
            var firstHash = HelperFixtures.Sha(HelperFixtures.Payload);
            var secondHash = HelperFixtures.Sha(HelperFixtures.OtherPayload);
            var a = store.ActivateAsync(Key(HelperFixtures.Session), "0.1.0", firstHash).AsTask()
                .GetAwaiter().GetResult();
            var b = store.ActivateAsync(Key(HelperFixtures.OtherSession), "0.1.0", secondHash).AsTask()
                .GetAwaiter().GetResult();
            HelperFixtures.Check(a.Code is null && b.Code is null);
            var readA = store.ReadCurrentAsync(Key(HelperFixtures.Session)).AsTask().GetAwaiter().GetResult();
            var readB = store.ReadCurrentAsync(Key(HelperFixtures.OtherSession)).AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(readA!.Sha256 == firstHash);
            HelperFixtures.Check(readB!.Sha256 == secondHash);
            HelperFixtures.Check(readA.Key.SessionName == "dev");
            HelperFixtures.Check(readB.Key.SessionName == "other");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void AtomicFailure()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var store = new HelperDeploymentReceiptStore(paths);
            var firstHash = HelperFixtures.Sha(HelperFixtures.Payload);
            HelperFixtures.Check(store.ActivateAsync(Key(HelperFixtures.Session), "0.1.0", firstHash)
                .AsTask().GetAwaiter().GetResult().Code is null);
            var original = File.ReadAllBytes(paths.HelperReceiptsFile);
            var failing = new HelperDeploymentReceiptStore(paths, new HelperReceiptStoreHooks
            {
                Commit = (_, _, _) => throw new IOException("replace_boom")
            });
            var second = failing.ActivateAsync(
                    Key(HelperFixtures.Session), "0.1.1", HelperFixtures.Sha(HelperFixtures.OtherPayload))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(second.Code == HelperCodes.ActivationFailed);
            HelperFixtures.Check(File.ReadAllBytes(paths.HelperReceiptsFile).SequenceEqual(original));
            var current = store.ReadCurrentAsync(Key(HelperFixtures.Session)).AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(current!.Sha256 == firstHash);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void EpochLease()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var store = new HelperDeploymentReceiptStore(AppDataPaths.FromRoot(root));
            var hashA = HelperFixtures.Sha(HelperFixtures.Payload);
            var hashB = HelperFixtures.Sha(HelperFixtures.OtherPayload);
            var key = Key(HelperFixtures.Session);
            HelperFixtures.Check(store.ActivateAsync(key, "0.1.0", hashA).AsTask().GetAwaiter().GetResult().Code is null);
            HelperFixtures.Check(store.AcquireLeaseAsync(key, new ConnectionEpoch(2), "0.1.0", hashA)
                .AsTask().GetAwaiter().GetResult() is null);
            var blocked = store.ActivateAsync(key, "0.1.1", hashB).AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(blocked.Code == HelperCodes.VersionCollision);
            store.ReleaseLeaseAsync(key, new ConnectionEpoch(1)).AsTask().GetAwaiter().GetResult();
            var still = store.ActivateAsync(key, "0.1.1", hashB).AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(still.Code == HelperCodes.VersionCollision);
            store.ReleaseLeaseAsync(key, new ConnectionEpoch(2)).AsTask().GetAwaiter().GetResult();
            var allowed = store.ActivateAsync(key, "0.1.1", hashB).AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(allowed.Code is null);
            HelperFixtures.Check(allowed.Receipt!.PreviousSha256 == hashA);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void RollbackSwitches()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var store = new HelperDeploymentReceiptStore(AppDataPaths.FromRoot(root));
            var hashA = HelperFixtures.Sha(HelperFixtures.Payload);
            var hashB = HelperFixtures.Sha(HelperFixtures.OtherPayload);
            var key = Key(HelperFixtures.Session);
            HelperFixtures.Check(store.ActivateAsync(key, "0.1.0", hashA).AsTask().GetAwaiter().GetResult().Code is null);
            HelperFixtures.Check(store.ActivateAsync(key, "0.1.1", hashB).AsTask().GetAwaiter().GetResult().Code is null);
            var rolled = store.RollbackAsync(key).AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(rolled.Code is null);
            HelperFixtures.Check(rolled.Receipt!.Sha256 == hashA);
            HelperFixtures.Check(rolled.Receipt.PreviousSha256 == hashB);
            var current = store.ReadCurrentAsync(key).AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(current!.Sha256 == hashA);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
