# herdr 连接与终端传输挂载重构

## Goal

补齐“herdr 进程没有挂载上”的真实连接链：在用户显式授权后，把保存的设备配置接到 endpoint、RPC 状态投影、选中 pane 的 terminal stdio 和 renderer；无授权或配置不完整时保持诚实的 unavailable 状态。

挂载含义是连接既有 daemon，不是由 HerdDesk 启动/拥有/停止 herdr daemon 或 agent。

## Evidence

- `src/HerdDesk.App/Composition/AppServices.cs:64-88` 固定使用 `UnavailableAdapter`；`ShellHost.cs:8-26` 固定 `DaemonAvailable=false`。
- `MainWindow.xaml.cs:42-53` 只组装 Shell，不创建 `DeviceSession`、RPC subscription 或 terminal transport。
- `SettingsPage.xaml.cs:128-135` 与 `SettingsViewModel.cs:276-287` 只记录 `PendingConnects`。
- `TerminalCliProcessFactory.cs:43-69`、`RpcStdioConnectionFactory.cs:66-78` 和 Core `DeviceSession.cs:79-138,182-188,425-499` 已存在但未接入 App。
- `tests/Contract/Program.cs:89-116,182-207` 当前锁定“不自动连接”的旧契约；`docs/adr/approved-baseline.md:68-83,102-115` 要求两通信平面分离且 GUI 不停止 daemon。

## Requirements

- **M1 授权**：连接按钮/授权结果触发编排器消费 `PendingConnects`；默认启动不连接、不启动 herdr。
- **M2 状态面**：显式 endpoint/path 经 `EndpointResolver` 后构造 `DeviceSession`，复用 decoder/store，将 `ReadStatesAsync` 投影到 `ProjectionCatalog`/aggregate；失败状态带稳定、脱敏 code。
- **M3 终端面**：选中 pane 后通过 `TerminalCliProcessFactory` 启动 `herdr terminal session observe`，绑定当前 `PaneKey`/`ConnectionEpoch` 到 TerminalHost；不得把 RPC 发到 terminal socket。
- **M4 生命周期**：RPC bridge、terminal transport 等本应用 child 注册到 `AppExitCoordinator` 并可释放；不注册/终止 herdr daemon/agent。
- **M5 失败关闭**：未知 endpoint、缺失 executable、版本/ACL 未验证、旧 epoch、EOF/backpressure 都不产生 fake Ready/ControlVerified；返回可诊断 unavailable/stale。
- **M6 离线证据**：使用 fake bridge/child contract 覆盖 connect→state→projection→frame→dispose；不启动真实 herdr、WinUI、SSH 或 takeover。

## Acceptance Criteria

- [ ] 生产组合在无授权/无有效配置时仍 fail-closed；在 fake 授权配置下选择真实 adapter set，而不是永久硬编码 `UnavailableAdapter`。
- [ ] fake contract 验证 `DeviceProfile → endpoint → DeviceSession → projection → terminal transport → renderer`，并验证 RPC/terminal 两平面独立。
- [ ] 失败、旧 epoch、EOF/backpressure 和退出均有稳定状态/诊断；`ControlVerified` 不由进程存活、首帧或焦点设置。
- [ ] 退出只释放已登记的 HerdDesk child，测试证明不会调用 daemon stop 或杀进程树。
- [ ] App/Infrastructure/Contract runners、`dotnet format`、`just ci` 通过；live herdr/pipe/WebView2 仍 `UNVERIFIED`。

## Out of scope

自动安装/启动/重启 daemon、control/takeover、真实 named-pipe ACL、远端 SSH、WebView2 现场渲染和发布验收。
