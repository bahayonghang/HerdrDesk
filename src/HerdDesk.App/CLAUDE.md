# HerdDesk.App

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · App

HD-007 composition root / host stub. `App.xaml`、`MainWindow`、Shell、settings 内容归 HD-011。WinUI 包未准入 lock，因此本项目是 `net10.0` 控制台宿主，不是 Windows 桌面工程。

## 职责

- 唯一组合根：`Composition/AppServices.CreateProduction`。
- 注册配置、诊断、Core 可调用的 BCL 类型，以及 unavailable adapter（RPC/transport/renderer）。
- 生产启动不得注册 `IsFakeSuccess` adapter。
- 释放顺序：renderer → transports → RPC → diagnostics。尚无 session actor。

## 入口

```powershell
dotnet run --project src/HerdDesk.App -- --compose-only <temp-root>
```

默认不写用户 AppData。不要在 CI 里对真实用户目录跑宿主。

## 依赖

Core、Infrastructure、Terminal.Web。无 PackageReference。
