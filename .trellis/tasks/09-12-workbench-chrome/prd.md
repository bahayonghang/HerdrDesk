# 工作台壳：状态条与空骨架

Parent: `.trellis/tasks/09-12-herdr-workbench-gap`. Architecture: parent `design.md`.

## Goal

把现有四区壳从「居中欢迎页 + 枚举状态」改成终端优先骨架：分区标题、中文状态、观察/控制栏、Settings 连接意图。不接 live herdr，不画 mosaic。

## Background

`ShellPage.xaml.cs:161-172` 把 Lifecycle/Connection/Agent/Access 拼进状态行。`ShellViewModel.cs:649-656` 的 `TitleSummary` 同样是枚举。Welcome 在非 Pane/设置路由时盖住中央（`ShowRoute` default）。`TerminalControlViewModel` 已有文案与动作，无 XAML。`RequestConnect` 无按钮。`ShellHost.Create` 不构造 control ViewModel。

## Requirements

- **R1**：四区在 `NoDevices` / `DaemonUnavailable` / `Failed` / `Ready` 都在。Zone1/2 有「设备与会话」「工作区」标题；空列表显示添加设备/诊断，不伪装在线。
- **R2**：顶栏状态用 `ShellStrings` 中文，字段独立（连接、agent、未读、观察/控制）。禁止枚举 dump。
- **R3**：中央默认是工作台宿主（空 banner + 未接入终端说明），不是单独 Welcome StackPanel。设置/诊断/关于仍覆盖中央，关闭回宿主。
- **R4**：操作栏绑定 `TerminalControlViewModel`；无 pane 时按钮 disabled + 原因。选择不置 `ControlVerified`。`RequestControl` 不授予 lease。
- **R5**：Settings「连接」调用 `RequestConnect`；无授权/Unavailable 时说明原因，不启动进程。
- **R6**：新控件有中文 `AutomationProperties.Name`；更新 `ShellSurface` / `AccessibilityNameCatalog`。不是 AC37。

## Acceptance Criteria

- [x] **AC1**：空配置 `StartAsync` 后 `ShellVisible`，`!DaemonOnline`，`LastStartMs < 2500`，状态文案为中文且不含 `NoDevices ·`。
- [x] **AC2**：有设备但 `DaemonAvailable=false` 时生命周期 `DaemonUnavailable`，骨架仍在，连接按钮不发 RPC（store/process 无新进程）。
- [x] **AC3**：`PrimaryActionName` 随 Access 变化；无 Challenge 时无接管按钮。
- [x] **AC4**：`just ci` 不启动 `--ui`。产品 AC 保持 `not_run`。

## Out of scope

tab mosaic、WebView 多实例、live RPC、文件区、HD-017 对话框、herdr 键位/壁纸。
