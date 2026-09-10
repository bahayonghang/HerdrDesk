using HerdDesk.App.Composition;
using HerdDesk.App.Quality;
using HerdDesk.Infrastructure.Configuration;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace HerdDesk.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = ProductInfo.Name;
        if (SoakLaunchPolicy.SuppressWindowClose())
            AppWindow.Closing += OnSoakAppWindowClosing;
        TryApplyBackdrop();
        Closed += OnClosed;
        if (App.DataRoot is { } root)
            BindRoot(root);
    }

    public ShellViewModel? Shell { get; private set; }

    public void Bind(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        Shell = shell;
        RootShell.Bind(shell);
        Activated += OnActivated;
        WinUiActivationHost.Listen(() =>
        {
            var intent = App.Activation?.PollRedirect();
            if (intent is not null)
                shell.ReceiveActivation(intent);
            Activate();
            RootShell.Refresh();
        });
    }

    private void BindRoot(string root)
    {
        var paths = AppDataPaths.FromRoot(root);
        var services = AppServices.CreateProduction(paths);
        var shell = ShellHost.Create(services, App.Activation);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        Bind(shell);
        var intent = App.Activation?.PollRedirect();
        if (intent is not null)
            shell.ReceiveActivation(intent);
        RootShell.Refresh();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        _ = (sender, args);
        if (Shell is null)
            return;
        var intent = App.Activation?.PollRedirect();
        if (intent is null)
            return;
        Shell.ReceiveActivation(intent);
        RootShell.Refresh();
    }

    private static void OnSoakAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        _ = sender;
        args.Cancel = true;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _ = (sender, args);
        if (Shell is not null)
            await Shell.ExitAsync().ConfigureAwait(true);
        App.Activation?.Dispose();
    }

    private void TryApplyBackdrop()
    {
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception)
        {
        }
    }
}
