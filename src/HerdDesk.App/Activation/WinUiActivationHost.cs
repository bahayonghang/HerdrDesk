using System.Security.Cryptography;
using System.Text;
using Microsoft.Windows.AppLifecycle;

namespace HerdDesk.App;

internal static class WinUiActivationHost
{
    public static string KeyForRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root)));
        return "HerdDesk_" + Convert.ToHexString(hash)[..16];
    }

    public static bool TryOwn(string key, out bool isCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        isCurrent = true;
        try
        {
            var instance = AppInstance.FindOrRegisterForKey(key);
            isCurrent = instance.IsCurrent;
            return true;
        }
        catch (Exception)
        {
            isCurrent = true;
            return false;
        }
    }

    public static void Redirect(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        try
        {
            var instance = AppInstance.FindOrRegisterForKey(key);
            if (instance.IsCurrent)
                return;
            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            instance.RedirectActivationToAsync(args).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }
    }

    public static void Listen(Action onActivated)
    {
        ArgumentNullException.ThrowIfNull(onActivated);
        try
        {
            AppInstance.GetCurrent().Activated += (_, args) =>
            {
                _ = args;
                onActivated();
            };
        }
        catch (Exception)
        {
        }
    }
}
