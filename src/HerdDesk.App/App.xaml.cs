using Microsoft.UI.Xaml;

namespace HerdDesk.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    internal static string? DataRoot { get; set; }
    internal static AppActivationCoordinator? Activation { get; set; }

    public MainWindow? MainWindow => _window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = args;
        _window ??= new MainWindow();
        _window.Activate();
    }
}
