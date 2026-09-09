using HerdDesk.App.Composition;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--compose-only")
            return RunComposeOnly(args[1]).GetAwaiter().GetResult();

#if WINDOWS_DESKTOP
        if (args.Length >= 1 && args[0] == "--shell-smoke")
            return RunShellSmoke();
        if (args.Length >= 2 && args[0] == "--ui")
            return RunUi(args[1]);
#endif

        Console.WriteLine(
            "HerdDesk host; pass --compose-only <temp-root>. WinUI starts only with --ui <temp-root>.");
        return 0;
    }

    private static async Task<int> RunComposeOnly(string root)
    {
        await using var services = AppServices.CreateProduction(AppDataPaths.FromRoot(root));
        Console.WriteLine("composition_root_ready");
        Console.WriteLine(services.HasFakeSuccessAdapter ? "fake_success=true" : "fake_success=false");
        Console.WriteLine("unavailable=" + services.Unavailable.Count);
#if WINDOWS_DESKTOP
        Console.WriteLine("winui=container");
#else
        Console.WriteLine("winui=not_admitted");
#endif
        return 0;
    }

#if WINDOWS_DESKTOP
    private static int RunShellSmoke()
    {
        Console.WriteLine("window_class=" + typeof(MainWindow).FullName);
        Console.WriteLine("shell_smoke=type_only");
        Console.WriteLine("message_loop=false");
        foreach (var name in ShellSurface.AutomationNames)
            Console.WriteLine("automation=" + name);
        return 0;
    }

    private static int RunUi(string root)
    {
        var paths = AppDataPaths.FromRoot(root);
        Directory.CreateDirectory(paths.SettingsDirectory);
        var intentPath = Path.Combine(paths.SettingsDirectory, "activation.intent");
        var instanceName = WinUiActivationHost.KeyForRoot(paths.Root);
        var activation = AppActivationCoordinator.Claim(instanceName, intentPath);
        if (!activation.IsPrimary)
        {
            activation.Redirect(new ActivationIntent(ActivationKind.Normal));
            WinUiActivationHost.Redirect(instanceName);
            activation.Dispose();
            Console.WriteLine("activation=redirected");
            return 0;
        }

        WinUiActivationHost.TryOwn(instanceName, out var current);
        if (!current)
        {
            activation.Redirect(new ActivationIntent(ActivationKind.Normal));
            WinUiActivationHost.Redirect(instanceName);
            activation.Dispose();
            Console.WriteLine("activation=redirected");
            return 0;
        }

        App.DataRoot = paths.Root;
        App.Activation = activation;
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(params_ =>
        {
            _ = params_;
            var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(queue);
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }
#endif
}
