# HerdDesk.Terminal.Web

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Terminal.Web

HD-007 BCL stub. WebView2/xterm remains the delivery baseline. Renderer NuGet/npm packages are `UNVERIFIED` and not referenced.

## 职责

导出 `WebRendererHost.Capability`（`renderer_packages_unverified`）。不启动 WebView2，不加载 xterm。

## 依赖

- 只引用 Contracts。
- 禁止 PackageReference，直到 HD-005 核验且许可准入的 renderer 包进入 lock。

Native renderer 仍不建仓。
