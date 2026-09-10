var cases = new (string Name, Action Run)[] { };
cases =
[
    .. ShellViewModelTests.All,
    .. ShellSurfaceTests.All,
    .. NavigationIdentityTests.All,
    .. SearchPaletteTests.All,
    .. LocalDeviceSettingsTests.All,
    .. DiagnosticExportPreviewTests.All,
    .. TerminalDisplaySettingsTests.All,
    .. ActivationAndExitTests.All,
    .. DeepLinkRoutingTests.All,
    .. TerminalInputTests.All,
    .. TerminalControlTests.All,
    .. ResourceCommandViewModelTests.All,
    .. RecoveryBindingsTests.All,
    .. DeviceConnectionStatusViewModelTests.All,
    .. LocalMvpShellCompositionTests.All,
    .. EditDeviceViewModelTests.All,
    .. HelperInstallViewModelTests.All,
    .. PartialStateTests.All,
    .. SharedResolverTests.All,
    .. PaneVisibilityBudgetTests.All,
    .. FilePaneGenerationTests.All,
    .. TransferTargetLeaseTests.All,
    .. TransferQueueProjectionTests.All,
    .. ConflictDialogTests.All,
    .. UntrustedNameTests.All,
    .. AttachToAgentViewModelTests.All,
    .. PastePreviewViewModelTests.All,
    .. TerminalHostSessionTests.All,
    .. Hd033CollectorTests.All,
    .. SoakLaunchPolicyTests.All
];

var failed = 0;
foreach (var test in cases)
{
    try
    {
        test.Run();
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + test.Name + ": " + error.GetType().Name + " " + error.Message);
    }
}

Console.WriteLine($"{cases.Length - failed}/{cases.Length} app unit tests passed; no live Windows/WinUI/IME validation.");
return failed == 0 ? 0 : 1;
