using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Terminal.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace HerdDesk.App.Controls;

public sealed partial class TerminalHost : UserControl
{
    private readonly TerminalHostSession session = new();
    private bool mapped;
    private bool started;

    public TerminalHost()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
        RefreshOverlay();
    }

    public TerminalHostSession Session => session;

    public void Bind(PaneKey pane, ConnectionEpoch epoch, bool readOnly, TerminalDisplayPreferences? display)
    {
        session.Bind(pane, epoch, readOnly);
        if (display is not null)
            session.ApplyLocal(display);
        _ = EnsureWebAsync();
        FlushOutbound();
        RefreshOverlay();
    }

    public void ApplyFrame(TerminalFrame frame)
    {
        session.ApplyFrame(frame);
        FlushOutbound();
        RefreshOverlay();
    }

    public void SetVisible(bool visible)
    {
        session.SetVisible(visible);
        Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RefreshOverlay();
    }

    private async Task EnsureWebAsync()
    {
        if (started)
            return;
        started = true;
        try
        {
            var root = App.DataRoot ?? Path.Combine(Path.GetTempPath(), "herddesk-terminal-webview");
            var userData = Path.Combine(root, "webview-terminal");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                null, userData, new CoreWebView2EnvironmentOptions()).AsTask().ConfigureAwait(true);
            await TerminalView.EnsureCoreWebView2Async(environment).AsTask().ConfigureAwait(true);
            var core = TerminalView.CoreWebView2;
            var settings = core.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsWebMessageEnabled = true;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindow;
            core.DownloadStarting += OnDownload;
            core.PermissionRequested += OnPermission;
            core.ProcessFailed += OnProcessFailed;
            core.WebMessageReceived += OnWebMessage;
            if (!mapped)
            {
                core.SetVirtualHostNameToFolderMapping(
                    new Uri(WebViewSecurityPolicy.LocalOrigin).Host,
                    AssetFolder(),
                    CoreWebView2HostResourceAccessKind.DenyCors);
                mapped = true;
            }

            core.Navigate(WebViewSecurityPolicy.LocalOrigin + "/index.html");
            FlushOutbound();
        }
        catch (Exception)
        {
            started = false;
            session.RetryObserve();
            RefreshOverlay();
        }
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var decision = WebViewSecurityPolicy.Classify(WebViewHostAction.NavigationStarting, args.Uri);
        if (!decision.Allowed)
            args.Cancel = true;
    }

    private void OnNewWindow(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        _ = WebViewSecurityPolicy.Classify(WebViewHostAction.NewWindowRequested, args.Uri);
        args.Handled = true;
    }

    private void OnDownload(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        _ = WebViewSecurityPolicy.Classify(WebViewHostAction.DownloadStarting, args.DownloadOperation?.Uri);
        args.Cancel = true;
        args.Handled = true;
    }

    private void OnPermission(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
    {
        _ = WebViewSecurityPolicy.Classify(WebViewHostAction.PermissionRequested);
        args.State = CoreWebView2PermissionState.Deny;
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        _ = (sender, args);
        session.RetryObserve();
        RefreshOverlay();
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        _ = sender;
        var json = args.WebMessageAsJson;
        if (string.IsNullOrEmpty(json))
            return;
        session.AcceptFromWeb(Encoding.UTF8.GetBytes(json));
        RefreshOverlay();
    }

    private void OnRetry(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        session.RetryObserve();
        FlushOutbound();
        RefreshOverlay();
    }

    private void FlushOutbound()
    {
        var core = TerminalView.CoreWebView2;
        if (core is null)
            return;
        while (session.DequeueOutbound() is { } json)
            core.PostWebMessageAsJson(Encoding.UTF8.GetString(json));
    }

    private void RefreshOverlay()
    {
        OverlayText.Text = session.OverlayText;
        Overlay.Visibility = session.OverlayVisible ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = session.RetryVisible ? Visibility.Visible : Visibility.Collapsed;
        Overlay.IsHitTestVisible = session.OverlayVisible;
    }

    private async void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        await session.DisposeAsync().ConfigureAwait(true);
        FlushOutbound();
    }

    private static string AssetFolder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "web", "terminal");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException(folder);
        return folder;
    }
}
