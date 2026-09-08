using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public static class WebRendererHost
{
    public const string DeliveryBaseline = "webview2-xterm";
    public const string PackageStatus = "UNVERIFIED";
    public const string NativeCandidateStatus = "UNVERIFIED";

    public static UnavailableCapability Capability { get; } =
        new("terminal-renderer-web", "renderer_packages_unverified");
}
