# src

[根索引](../CLAUDE.md) · C# 源码

生成日期：2026-09-08。solution：`HerdDesk.slnx`。属性：根 `Directory.Build.props`（`net10.0`、nullable、警告为错误、确定性、NET analyzers）。`NuGet.Config` 清空包源。HD-007 已加入 BCL App/Infrastructure/Terminal.Web；WinUI 包未准入 lock。

## 当前项目

| 项目 | 索引 |
|---|---|
| [HerdDesk.Contracts](HerdDesk.Contracts/CLAUDE.md) | 身份、信封、配置/诊断/adapter 端口 |
| [HerdDesk.Core](HerdDesk.Core/CLAUDE.md) | 帧解析 + 输入策略 + endpoint 映射 + lease 映射 + renderer L1 标本 + HD-009 投影 Store + HD-010 DeviceSession actor + HD-012 Attention reducer + HD-016 ControlLeaseCoordinator + HD-017 ResourceCommandCoordinator + HD-018 RecoveryPolicy |
| [HerdDesk.Infrastructure](HerdDesk.Infrastructure/CLAUDE.md) | 配置存储、诊断 sink、owned process、RPC stdio、SchemaV1 decoder、HD-013 TerminalCliTransport |
| [HerdDesk.Terminal.Web](HerdDesk.Terminal.Web/CLAUDE.md) | HD-014 L1 message validator + BCL renderer adapter + HD-015 L1 input coordinators（无 WebView2） |
| [HerdDesk.App](HerdDesk.App/CLAUDE.md) | 组合根 / 控制台宿主 stub + HD-011 L1 ViewModels + HD-012 L1 NotificationCenter + HD-015 L1 focus/input + HD-016 L1 control ViewModel + HD-017 L1 resource command + HD-018 L1 RecoveryBindings（无 WinUI） |

测试项目在 [../tests](../tests/CLAUDE.md)。

## 规划尚未建仓

`HerdDesk.Terminal.Native`、`herddesk-filebridge`、WinUI `App.xaml` 内容。HD-011 已落地 BCL ViewModels；WinUI 包仍未准入。`bridge/herddesk-bridge` 已由 HD-008 L1 建仓；L2 ACL 仍为 UNVERIFIED。

## 约束

- 生产与测试项目禁止 `PackageReference`，直到许可准入且 lock 由真实 restore 生成。
- Core 禁止引用 WinUI / WebView2 / SSH。
- 草案 `docs/plan/contracts/HerdDesk.Contracts.cs` 不得整文件覆盖本目录。
