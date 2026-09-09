using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public enum WebViewHostAction
{
    NavigationStarting,
    NewWindowRequested,
    DownloadStarting,
    PermissionRequested,
    HostObjectRequested,
    DialogRequested,
    DevToolsRequested,
    LinkRequest
}

public sealed record WebViewSecurityDecision(bool Allowed, string Code, string? Uri = null);

public static class WebViewSecurityPolicy
{
    public const string LocalOrigin = "https://herddesk.terminal.local";
    public const string RuntimeStatus = "UNVERIFIED";

    public static WebViewSecurityDecision Classify(
        WebViewHostAction action,
        string? uri = null,
        bool userGesture = false)
    {
        if (action == WebViewHostAction.LinkRequest)
            return ClassifyLink(uri, userGesture);
        if (action == WebViewHostAction.NavigationStarting)
            return ClassifyNavigation(uri);
        return action switch
        {
            WebViewHostAction.NewWindowRequested => Deny("new_window_denied", uri),
            WebViewHostAction.DownloadStarting => Deny("download_denied", uri),
            WebViewHostAction.PermissionRequested => Deny("permission_denied", uri),
            WebViewHostAction.HostObjectRequested => Deny("host_object_denied", uri),
            WebViewHostAction.DialogRequested => Deny("dialog_denied", uri),
            WebViewHostAction.DevToolsRequested => Deny("devtools_denied", uri),
            _ => Deny("navigation_denied", uri),
        };
    }

    public static bool IsLocalOriginUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
            return false;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;
        return parsed.Scheme == Uri.UriSchemeHttps &&
               parsed.Host.Equals("herddesk.terminal.local", StringComparison.OrdinalIgnoreCase);
    }

    public static WebViewSecurityDecision ClassifyNavigation(string? uri)
    {
        if (IsLocalOriginUri(uri) || IsBootstrapUri(uri))
            return new WebViewSecurityDecision(true, "allowed", uri);
        return Deny("navigation_denied", uri);
    }

    private static bool IsBootstrapUri(string? uri) =>
        uri is "about:blank" or "about:blank/";

    public static WebViewSecurityDecision ClassifyLink(string? uri, bool userGesture)
    {
        if (!userGesture)
            return Deny("link_gesture_required", uri);
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return Deny("link_scheme_denied", uri);
        if (parsed.Scheme is not ("http" or "https"))
            return Deny("link_scheme_denied", uri);
        if (uri!.Length > WebMessageLimits.MaxLinkUriChars)
            return Deny("web_message_bytes_limit", uri);
        return new WebViewSecurityDecision(true, "allowed", uri);
    }

    private static WebViewSecurityDecision Deny(string code, string? uri) =>
        new(false, code, uri);
}
