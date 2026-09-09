# src

[根索引](../CLAUDE.md) · C# 源码

生成日期：2026-09-08。solution：`HerdDesk.slnx`。属性：根 `Directory.Build.props`（默认 `net10.0`、nullable、警告为错误、确定性、NET analyzers）。`NuGet.Config` 将 nuget.org 映射到 `Microsoft.WindowsAppSDK.*`、`Microsoft.Web.WebView2`、`Microsoft.Windows.SDK.BuildTools`、`Microsoft.Windows.SDK.BuildTools.MSIX`、`Microsoft.Windows.SDK.NET.Ref` 与 `Microsoft.Windows.SDK.NET.Ref.Windows`。HD-007 L2 准入 App windows TFM 的 WinUI 2.3.6 lock；Contracts/Core/Infrastructure/Terminal.Web 仍无 PackageReference。

## 当前项目

| 项目 | 索引 |
|---|---|
| [HerdDesk.Contracts](HerdDesk.Contracts/CLAUDE.md) | 身份、信封、配置/诊断/adapter 端口 |
| [HerdDesk.Core](HerdDesk.Core/CLAUDE.md) | 帧解析 + 输入策略 + endpoint 映射 + lease 映射 + renderer L1 标本 + HD-009 投影 Store + HD-010 DeviceSession actor + HD-012 Attention reducer + HD-016 ControlLeaseCoordinator + HD-017 ResourceCommandCoordinator + HD-018 RecoveryPolicy + HD-023 L1 多设备聚合 + HD-025 L1 连接准入与队列预算 + HD-028 L1 TransferCoordinator + HD-030 L1 AttachmentCoordinator + HD-031 L1 clipboard intent/paste |
| [HerdDesk.Infrastructure](HerdDesk.Infrastructure/CLAUDE.md) | 配置存储、诊断 sink、owned process、RPC stdio、SchemaV1 decoder、HD-013 TerminalCliTransport、HD-020 L1 SSH preview/test、HD-021 L1 helper publish、HD-031 L1 clipboard reader/cache |
| [HerdDesk.Terminal.Web](HerdDesk.Terminal.Web/CLAUDE.md) | HD-014 L1 message validator + BCL renderer adapter + HD-015 L1 input coordinators + HD-031 L1 OSC 52 deny（无 WebView2） |
| [HerdDesk.App](HerdDesk.App/CLAUDE.md) | 组合根 / `net10.0` 控制台宿主 + windows TFM 空白 `App.xaml`/`MainWindow` + HD-011 L1 ViewModels + HD-012 L1 NotificationCenter + HD-015 L1 focus/input + HD-016 L1 control ViewModel + HD-017 L1 resource command + HD-018 L1 RecoveryBindings + HD-020 L1 SSH EditDevice ViewModel + HD-021 L1 HelperInstall ViewModel + HD-023 L1 multi-device navigation/search ViewModels + HD-029 L1 双栏文件工作区 ViewModels + HD-030 L1 AttachToAgentViewModel + HD-031 L1 PastePreviewViewModel。HD-019 L1 catalog/composition 在测试与 `evidence/local-mvp/`。Shell 内容仍为 HD-011 |

测试项目在 [../tests](../tests/CLAUDE.md)。

## 规划尚未建仓

`HerdDesk.Terminal.Native`、WinUI Shell 内容。HD-011 已落地 BCL ViewModels；空白 `App.xaml`/`MainWindow` 已由 HD-007 L2 准入 windows TFM。`bridge/herddesk-bridge` 已由 HD-008 L1 建仓；L2 ACL 仍为 UNVERIFIED。`filebridge/` HD-027 codec 与 HD-028 L1 `herddesk-filebridge serve` 已建仓；L2 FS/SSH/TOCTOU 为 UNVERIFIED。

## 约束

- 除 App windows TFM 已准入的 `Microsoft.WindowsAppSDK.WinUI` 2.3.6 外，生产与测试项目禁止 `PackageReference`。禁止 umbrella WASDK 2.4.0。lock 由真实 restore 生成。
- Core 禁止引用 WinUI / WebView2 / SSH。
- 草案 `docs/plan/contracts/HerdDesk.Contracts.cs` 不得整文件覆盖本目录。
