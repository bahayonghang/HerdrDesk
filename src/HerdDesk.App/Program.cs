using HerdDesk.App.Composition;
using HerdDesk.Infrastructure.Configuration;

if (args.Length >= 2 && args[0] == "--compose-only")
{
    await using var services = AppServices.CreateProduction(AppDataPaths.FromRoot(args[1]));
    Console.WriteLine("composition_root_ready");
    Console.WriteLine(services.HasFakeSuccessAdapter ? "fake_success=true" : "fake_success=false");
    Console.WriteLine("unavailable=" + services.Unavailable.Count);
    Console.WriteLine("winui=not_admitted");
    return 0;
}

Console.WriteLine("HerdDesk host stub; WinUI desktop not admitted (HD-011 L1 ViewModels).");
return 0;
