# HerdDesk.Terminal.Web

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Terminal.Web

HD-014 L1 BCL renderer adapter plus HD-015 L1 IME/keyboard/selection coordinators. HD-014 L2 admits local `@xterm/xterm` 6.0.0 (MIT, `web/terminal/`) and the WinUI WebView2 control on the App windows TFM. This project stays BCL-only. L2 WebView process, L3 DPI, and L3 real IME desktop stay UNVERIFIED.

## 职责

- 导出 `WebRendererHost.Capability`（`renderer_host_windows_only`）。`PackageStatus=admitted`。
- 校验版本化 host↔web JSON allowlist（`WebMessageValidator` / `WebMessageCodec`）。
- 实现 `ITerminalRenderer` 状态机（`WebTerminalRenderer`）：epoch bind、byte apply、observe no-resize、read-only gate。
- 记录 WebView 安全拒绝码（`WebViewSecurityPolicy`）。本项目不引用 WebView2。App windows TFM `TerminalHost` 承载固定本地 origin。不拉 CDN。
- HD-015 L1：`TerminalInputController` 绑定 `PaneKey`/`ConnectionEpoch`，经 `CompositionPolicy`/`InputPolicy` 回调放行。预编辑 0 字节；commit token 恰好一次；composition 期间 Ctrl+K/Enter/Esc 让位 IME。不授予 lease。
- HD-031 L1：`OscClipboardPolicy` 默认拒绝 OSC 52 read/write，稳定码 `clipboard_read_denied` / `clipboard_write_denied`。不调用 snapshot reader，不写 Windows clipboard API。只依赖 Contracts。

`RenderFlowController` 在 Core，消费 `RendererByteWindow` / seq gate。本项目只依赖 Contracts。

## 依赖

- 只引用 Contracts。
- 禁止 PackageReference。npm `@xterm/xterm` 6.0.0 在 `web/terminal/`，不在本 csproj。WebView2 仅 App windows TFM 使用 WinUI 传递包。

Native renderer 仍不建仓。
