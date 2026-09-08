# HerdDesk.Terminal.Web

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Terminal.Web

HD-014 L1 BCL renderer adapter plus HD-015 L1 IME/keyboard/selection coordinators. WebView2/xterm remains the delivery baseline. Renderer NuGet/npm packages are `UNVERIFIED` and not referenced. L2 WebView process, L3 DPI, and L3 real IME desktop stay UNVERIFIED.

## 职责

- 导出 `WebRendererHost.Capability`（`renderer_packages_unverified`）。
- 校验版本化 host↔web JSON allowlist（`WebMessageValidator`）。
- 实现 `ITerminalRenderer` 状态机（`WebTerminalRenderer`）：epoch bind、byte apply、observe no-resize、read-only gate。
- 记录 WebView 安全拒绝码（`WebViewSecurityPolicy`）。不启动 WebView2，不加载 xterm，不拉 CDN。
- HD-015 L1：`TerminalInputController` 绑定 `PaneKey`/`ConnectionEpoch`，经 `CompositionPolicy`/`InputPolicy` 回调放行。预编辑 0 字节；commit token 恰好一次；composition 期间 Ctrl+K/Enter/Esc 让位 IME。不授予 lease。

`RenderFlowController` 在 Core，消费 `RendererByteWindow` / seq gate。本项目只依赖 Contracts。

## 依赖

- 只引用 Contracts。
- 禁止 PackageReference。WebView2 / WASDK / npm xterm 未准入 lock。

Native renderer 仍不建仓。
