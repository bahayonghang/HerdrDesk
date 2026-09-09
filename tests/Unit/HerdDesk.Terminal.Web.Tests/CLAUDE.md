# HerdDesk.Terminal.Web.Tests

BCL console runner (`dotnet run`, not `dotnet test`) for HD-014 L1 web-message allowlist, render flow, epoch reject, and observe no-resize, plus HD-015 L1 IME/keyboard/selection/focus coordinators and HD-031 L1 `OscClipboardPolicy` (OSC 52 read/write deny, snapshot reader invocations 0). Uint8Array-equivalent byte arrays only. Tests drive shipped coordinators. No WebView2, npm/xterm, live herdr, or IME desktop. L2 WebView process and L3 IME/DPI stay UNVERIFIED. Not product AC08/AC09/AC10/AC27 pass.
