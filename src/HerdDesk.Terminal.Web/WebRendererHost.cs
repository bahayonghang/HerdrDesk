using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public static class WebRendererHost
{
    public const string DeliveryBaseline = "webview2-xterm";
    public const string PackageStatus = "admitted";
    public const string NativeCandidateStatus = "UNVERIFIED";
    public const string L2WebViewProcess = "UNVERIFIED";
    public const string L3DpiThemeFocus = "UNVERIFIED";
    public const int ProtocolVersion = WebMessageLimits.SchemaVersion;

    public static UnavailableCapability Capability { get; } =
        new("terminal-renderer-web", "renderer_host_windows_only");
}
