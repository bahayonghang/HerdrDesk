# HerdDesk.App

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · App

HD-007 composition root / host stub plus HD-011 L1 navigation ViewModels and HD-012 L1 NotificationCenter ViewModel. `App.xaml`、`MainWindow`、WinUI Shell 内容仍未准入。WinUI 包未准入 lock，因此本项目是 `net10.0` 控制台宿主，不是 Windows 桌面工程。 Windows toast 注册为 L2 UNVERIFIED。

## 职责

- 唯一组合根：`Composition/AppServices.CreateProduction`。
- 注册配置、诊断、Core 可调用的 BCL 类型，以及 unavailable adapter（RPC/transport/renderer）。HD-008 的 `RpcStdioConnectionFactory` 需显式 bridge 路径；生产组合根仍不自动连接。
- HD-011 L1：`ShellViewModel` 与 identity coordinators 消费 Store 投影和 HD-007 配置端口。不创建隐藏 terminal bridge，不编译 WinUI。
- HD-012 L1：`NotificationCenterViewModel` 消费 Core `AttentionReducer`；`WindowsNotificationSink.Available` 恒为 false。点击只定位完整 `PaneKey`，不携带 takeover/input/command。
- HD-015 L1：`TerminalFocusCoordinator` 在 renderer Ready 后恢复焦点；`TerminalInputViewModel` 展示观察/申请控制/控制/输入暂停/连接过期。焦点不置位 `ControlVerified`。`RequestControl` 不授予 lease。
- HD-016 L1：`TerminalControlViewModel` 调用 `ControlLeaseCoordinator`。无 WinUI ControlBar。无 always-takeover。选择变化使 takeover handle 失效。L2 live lease 为 UNVERIFIED。
- HD-017 L1：`ResourceCommandViewModel` 调用 `ResourceCommandCoordinator`。无 WinUI 对话框。无全局 bypass。选择变化使未提交对话框 stale。L2 live mutation 为 UNVERIFIED。
- HD-018 L1：`Recovery/RecoveryBindings.cs` 把 `DeviceSessionState` 路由给 lease 的 stale/ready 信号，并投影 `RecoveryViewState`。无 timer、epoch 或 mutation 权。不启动 daemon，不 `RecoverControl`。`AppExitCoordinator` kill ledger 只含 owned bridge/CLI。L2 live disconnect 为 UNVERIFIED。
- HD-019 L1：Shell/Terminal composition 测试驱动已交付 coordinators 与 ViewModels。默认观察；`RequestControl` 不 takeover；断开后无重放；GUI 关闭只杀 owned pids。L2 live local MVP 与 L3 IME/TUI 为 UNVERIFIED。产品 AC06/AC07/AC10/AC15 未通过。
- HD-020 L1：`Devices/EditDeviceViewModel.cs` 编辑 SSH `DeviceProfile`、预览与分阶段测试。无 WinUI XAML。无 raw argv。L2 隔离 OpenSSH 为 UNVERIFIED。产品 AC22/AC23 未通过。
- HD-021 L1：`Devices/HelperInstallViewModel.cs` 展示不可变 `DeploymentPlan`、本次确认、取消与回滚。无 always-allow。无 WinUI XAML。L2 live helper deploy 为 UNVERIFIED。产品 AC25 未通过。
- 生产启动不得注册 `IsFakeSuccess` adapter。
- 释放顺序：SSH tester cancel → helper cancel → renderer → transports → RPC → diagnostics。退出只释放本应用 child processes。

## 入口

```powershell
dotnet run --project src/HerdDesk.App -- --compose-only <temp-root>
```

默认不写用户 AppData。不要在 CI 里对真实用户目录跑宿主。L1 测试：`dotnet run --project tests/Unit/HerdDesk.App.Tests`。

## 依赖

Core、Infrastructure、Terminal.Web。无 PackageReference。
