# HerdDesk.Terminal.Web

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Terminal.Web

HD-014 L1 BCL renderer adapter. WebView2/xterm remains the delivery baseline. Renderer NuGet/npm packages are `UNVERIFIED` and not referenced. L2 WebView process and L3 DPI stay UNVERIFIED.

## 职责

- 导出 `WebRendererHost.Capability`（`renderer_packages_unverified`）。
- 校验版本化 host↔web JSON allowlist（`WebMessageValidator`）。
- 实现 `ITerminalRenderer` 状态机（`WebTerminalRenderer`）：epoch bind、byte apply、observe no-resize、read-only gate。
- 记录 WebView 安全拒绝码（`WebViewSecurityPolicy`）。不启动 WebView2，不加载 xterm，不拉 CDN。

`RenderFlowController` 在 Core，消费 `RendererByteWindow` / seq gate。本项目只依赖 Contracts。

## 依赖

- 只引用 Contracts。
- 禁止 PackageReference。WebView2 / WASDK / npm xterm 未准入 lock。

Native renderer 仍不建仓。
