using HerdDesk.Terminal.Web;

internal static class WebViewSecurityTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("navigation new window download and permission are denied", HostEventsDenied),
        ("only gestured http or https links are allowed", LinkPolicy)
    ];

    static void HostEventsDenied()
    {
        WebTestHost.Check(WebViewSecurityPolicy.Classify(WebViewHostAction.NavigationStarting,
            "https://example.com").Code == "navigation_denied");
        WebTestHost.Check(WebViewSecurityPolicy.Classify(WebViewHostAction.NewWindowRequested,
            "https://example.com").Code == "new_window_denied");
        WebTestHost.Check(WebViewSecurityPolicy.Classify(WebViewHostAction.DownloadStarting,
            "https://example.com/file.bin").Code == "download_denied");
        WebTestHost.Check(WebViewSecurityPolicy.Classify(WebViewHostAction.PermissionRequested)
            .Code == "permission_denied");
        WebTestHost.Check(WebViewSecurityPolicy.Classify(WebViewHostAction.HostObjectRequested)
            .Code == "host_object_denied");
        WebTestHost.Check(WebViewSecurityPolicy.Classify(WebViewHostAction.DialogRequested)
            .Code == "dialog_denied");
        WebTestHost.Check(WebViewSecurityPolicy.Classify(WebViewHostAction.DevToolsRequested)
            .Code == "devtools_denied");
        WebTestHost.Check(WebViewSecurityPolicy.RuntimeStatus == "UNVERIFIED");
        WebTestHost.Check(WebRendererHost.L2WebViewProcess == "UNVERIFIED");
        WebTestHost.Check(WebRendererHost.PackageStatus == "UNVERIFIED");
    }

    static void LinkPolicy()
    {
        var allowed = WebViewSecurityPolicy.ClassifyLink("https://example.com/path", true);
        WebTestHost.Check(allowed.Allowed);
        WebTestHost.Check(allowed.Code == "allowed");
        WebTestHost.Check(WebViewSecurityPolicy.ClassifyLink("http://example.com", true).Allowed);
        WebTestHost.Check(WebViewSecurityPolicy.ClassifyLink("https://example.com", false).Code ==
                          "link_gesture_required");
        WebTestHost.Check(WebViewSecurityPolicy.ClassifyLink("javascript:alert(1)", true).Code ==
                          "link_scheme_denied");
        WebTestHost.Check(WebViewSecurityPolicy.ClassifyLink("file:///tmp/x", true).Code ==
                          "link_scheme_denied");
        var renderer = WebTestHost.Renderer();
        WebTestHost.Check(renderer.AcceptWebMessage(WebTestHost.Utf8Json(
            """{"version":1,"kind":"linkRequest","epoch":1,"uri":"javascript:alert(1)","userGesture":true}"""))
            .Code == "link_scheme_denied");
        WebTestHost.Check(renderer.AcceptWebMessage(WebTestHost.Utf8Json(
            """{"version":1,"kind":"linkRequest","epoch":1,"uri":"https://example.com","userGesture":true}"""))
            .Allowed);
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
