# src

[根索引](../CLAUDE.md) · C# 源码

生成日期：2026-09-08。solution：`HerdDesk.slnx`。属性：根 `Directory.Build.props`（`net10.0`、nullable、警告为错误、确定性、NET analyzers）。`NuGet.Config` 清空包源。

## 当前项目

| 项目 | 索引 |
|---|---|
| [HerdDesk.Contracts](HerdDesk.Contracts/CLAUDE.md) | 身份与信封类型 |
| [HerdDesk.Core](HerdDesk.Core/CLAUDE.md) | 帧解析 + 输入策略 + endpoint 映射 + lease 映射 + renderer L1 标本 |

测试项目在 [../tests/HerdDesk.Core.SmokeTests](../tests/HerdDesk.Core.SmokeTests/CLAUDE.md)。

## 规划尚未建仓

`HerdDesk.App`、`HerdDesk.Infrastructure`、`HerdDesk.Terminal.Web`、`HerdDesk.Terminal.Native`。边界见 [../docs/plan/CLAUDE.md](../docs/plan/CLAUDE.md)。HD-007 仍为 planned：现有三项目骨架是 G0 建仓准备。

## 约束

- G0 禁止 `PackageReference`。
- Core 禁止引用 WinUI / WebView2 / SSH。
- 草案 `docs/plan/contracts/HerdDesk.Contracts.cs` 不得整文件覆盖本目录。
