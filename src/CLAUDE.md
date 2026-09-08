# src

[根索引](../CLAUDE.md) · C# 源码

生成日期：2026-09-08。solution：`HerdDesk.slnx`。属性：根 `Directory.Build.props`（`net10.0`、nullable、警告为错误、确定性、NET analyzers）。`NuGet.Config` 清空包源。HD-007 已加入 BCL App/Infrastructure/Terminal.Web；WinUI 包未准入 lock。

## 当前项目

| 项目 | 索引 |
|---|---|
| [HerdDesk.Contracts](HerdDesk.Contracts/CLAUDE.md) | 身份、信封、配置/诊断/adapter 端口 |
| [HerdDesk.Core](HerdDesk.Core/CLAUDE.md) | 帧解析 + 输入策略 + endpoint 映射 + lease 映射 + renderer L1 标本 + HD-009 投影 Store |
| [HerdDesk.Infrastructure](HerdDesk.Infrastructure/CLAUDE.md) | 配置存储、诊断 sink、owned process、RPC stdio、SchemaV1 decoder |
| [HerdDesk.Terminal.Web](HerdDesk.Terminal.Web/CLAUDE.md) | Web renderer capability stub |
| [HerdDesk.App](HerdDesk.App/CLAUDE.md) | 组合根 / 控制台宿主 stub |

测试项目在 [../tests](../tests/CLAUDE.md)。

## 规划尚未建仓

`HerdDesk.Terminal.Native`、`herddesk-filebridge`、WinUI `App.xaml` 内容（HD-011）。`bridge/herddesk-bridge` 已由 HD-008 L1 建仓；L2 ACL 仍为 UNVERIFIED。

## 约束

- 生产与测试项目禁止 `PackageReference`，直到许可准入且 lock 由真实 restore 生成。
- Core 禁止引用 WinUI / WebView2 / SSH。
- 草案 `docs/plan/contracts/HerdDesk.Contracts.cs` 不得整文件覆盖本目录。
