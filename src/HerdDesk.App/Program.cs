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

        Console.WriteLine(
            "HerdDesk host stub; pass --compose-only <temp-root>. WinUI container is not started from this entry.");
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
}
