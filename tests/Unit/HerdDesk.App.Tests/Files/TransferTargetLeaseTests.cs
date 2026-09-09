using HerdDesk.App;
using HerdDesk.Contracts;

internal static class TransferTargetLeaseTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("remote location requires device id not path alone", RemoteNeedsDevice),
        ("100 focus and device switches keep confirmed source and destination", HundredSwitchesHoldLease),
        ("open completed target stays on the original destination", CompletedTargetNotRetargeted),
        ("remote to local keeps device id on the source lease", RemoteToLocalLease)
    ];

    static void RemoteNeedsDevice()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        var remote = FileLocation.Remote(AppTestHost.DeviceB, fake);
        AppTestHost.Check(remote.Kind == FileLocationKind.Remote);
        AppTestHost.Check(remote.Device.Equals(AppTestHost.DeviceB));
        AppTestHost.Check(typeof(FileLocation).GetMethod("FromPath") is null);
        try
        {
            _ = FileLocation.Remote(new DeviceId(Guid.Empty), fake);
            AppTestHost.Check(false);
        }
        catch (ArgumentException)
        {
        }

        try
        {
            _ = FileLocation.Remote(AppTestHost.DeviceA, fake);
            AppTestHost.Check(false);
        }
        catch (ArgumentException)
        {
        }
    }

    static void HundredSwitchesHoldLease()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        left.AddFile("payload.bin", "hello"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("payload.bin"), true);
        var draft = workspace.CreateDraft(TransferDirection.LeftToRight);
        AppTestHost.Check(draft is not null);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Queue.Jobs.Count == 1);
        var job = workspace.Queue.Jobs[0];
        AppTestHost.Check(job.Phase == TransferJobPhase.Completed);
        var source = job.Lease.Source;
        var dest = job.Lease.Destination;
        var altLeft = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceC, "alt-left", 9));
        var altRight = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceC, "alt-right", 9));
        for (var i = 0; i < 100; i++)
        {
            workspace.SwitchFocus(i % 2 == 0 ? "left" : "right");
            workspace.SetLayout(i % 3 == 0 ? LayoutBreakpoint.Narrow : LayoutBreakpoint.Wide);
            workspace.SwitchNarrowTab(i % 2 == 0 ? "right" : "left");
            var alt = i % 2 == 0
                ? FileLocation.Remote(AppTestHost.DeviceC, altLeft)
                : FileLocation.Local(altRight);
            workspace.SwitchDevice(alt);
        }

        var frozen = workspace.Queue.Jobs[0];
        AppTestHost.Check(frozen.JobId == job.JobId);
        AppTestHost.Check(frozen.Lease.SourceDevice.Equals(source.Device));
        AppTestHost.Check(frozen.Lease.DestinationDevice.Equals(dest.Device));
        AppTestHost.Check(frozen.Source.Path.Equals(source.Path));
        AppTestHost.Check(frozen.Destination.Path.Equals(dest.Path));
        AppTestHost.Check(frozen.Lease.SourceEpoch.Value == source.Epoch.Value);
        AppTestHost.Check(frozen.Lease.DestinationEpoch.Value == dest.Epoch.Value);
        AppTestHost.Check(workspace.RouteSummary.Contains("→", StringComparison.Ordinal));
        AppTestHost.Check(workspace.NarrowLayout is false || workspace.RouteSummary.Length > 0);
    }

    static void CompletedTargetNotRetargeted()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        left.AddFile("payload.bin", "hello"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("payload.bin"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        var job = workspace.Queue.Jobs[0];
        var original = job.Destination;
        var other = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceC, "other"));
        workspace.SwitchFocus("right");
        workspace.SwitchDevice(FileLocation.Remote(AppTestHost.DeviceC, other));
        var opened = workspace.OpenCompletedTarget(job.JobId);
        AppTestHost.Check(opened is not null);
        AppTestHost.Check(opened!.Device.Equals(original.Device));
        AppTestHost.Check(opened.Path.Equals(original.Path));
        AppTestHost.Check(!opened.Device.Equals(AppTestHost.DeviceC));
    }

    static void RemoteToLocalLease()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        right.AddFile("from-remote.bin", "abc"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Right.Select(FileWorkspaceHarness.Comp("from-remote.bin"), true);
        var draft = workspace.CreateDraft(TransferDirection.RightToLeft);
        AppTestHost.Check(draft is not null);
        AppTestHost.Check(draft!.Source.Kind == FileLocationKind.Remote);
        AppTestHost.Check(draft.Source.Device.Equals(AppTestHost.DeviceB));
        AppTestHost.Check(draft.Destination.Kind == FileLocationKind.Local);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Queue.Jobs.Count == 1);
        var job = workspace.Queue.Jobs[0];
        AppTestHost.Check(job.Phase == TransferJobPhase.Completed);
        AppTestHost.Check(job.Lease.SourceDevice.Equals(AppTestHost.DeviceB));
        AppTestHost.Check(job.Lease.DestinationDevice.Equals(AppTestHost.DeviceA));
        AppTestHost.Check(left.Has("from-remote.bin"));
        workspace.SwitchFocus("left");
        workspace.SwitchDevice(FileLocation.Local(new FakeFileEndpoint(
            FileWorkspaceHarness.Key(AppTestHost.DeviceC, "other-local"))));
        AppTestHost.Check(workspace.Queue.Jobs[0].Lease.SourceDevice.Equals(AppTestHost.DeviceB));
        AppTestHost.Check(workspace.Queue.Jobs[0].Lease.DestinationDevice.Equals(AppTestHost.DeviceA));
    }
}
