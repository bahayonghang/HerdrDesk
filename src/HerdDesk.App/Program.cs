using HerdDesk.App.Composition;
using HerdDesk.Infrastructure.Configuration;

if (args.Length >= 2 && args[0] == "--compose-only")
{
    await using var services = AppServices.CreateProduction(AppDataPaths.FromRoot(args[1]));
    Console.WriteLine("composition_root_ready");
    Console.WriteLine(services.HasFakeSuccessAdapter ? "fake_success=true" : "fake_success=false");
    Console.WriteLine("unavailable=" + services.Unavailable.Count);
    Console.WriteLine("winui=deferred");
    return 0;
}

Console.WriteLine("HerdDesk host stub; WinUI desktop deferred (HD-011).");
return 0;
