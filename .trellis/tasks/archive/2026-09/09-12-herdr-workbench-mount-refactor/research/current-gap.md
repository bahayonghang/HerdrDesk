# 当前截图问题的代码证据

本记录只用于规划，不代表已获得 live herdr、WinUI 或 WebView2 验收证据。

## 1. 四区排版为何看起来不像 herdr

- `src/HerdDesk.App/Views/ShellPage.xaml:63-108` 已定义设备轨、工作区树、中央 `WorkbenchHost`、详情列四个 Grid 列；缺口不是“没有四列”，而是空态和可见性策略让列失去视觉边界。
- `src/HerdDesk.App/Views/ShellPage.xaml.cs:102-121` 在每次刷新时把详情列折叠为 0，只有 `Layout == Wide` 且 `Shell.Details != Collapsed` 才显示 280px 详情列。`ShellViewModel.cs:115-116` 的详情初值是 `Collapsed`，因此截图默认只呈现三块有效内容。
- `src/HerdDesk.App/Controls/DeviceSessionRail.xaml:6-50` 与 `WorkspacePaneTree.xaml:6-50` 只有标题、ListView 和空提示；无投影时两列是空黑面板，视觉上没有分隔、背景或最小宽度。
- `src/HerdDesk.App/Views/ShellPage.xaml.cs:124-161` 在 `ShellRoute.Welcome` 时只显示中央 `EmptyBanner`。`ShellViewModel.StartAsync` 在 `NoDevices`/`DaemonUnavailable` 分支（约 `:148-175`）不会有工作区选择，因此 tab/mosaic 不会出现。
- `src/HerdDesk.App/ViewModels/WorkbenchLayout.cs:201-259` 只负责已选 workspace 的 tab/layout/pane 槽位；没有 session/layout 时最多回退成一个满屏 placeholder，不能承担外层四区排版。
- `.trellis/spec/frontend/component-guidelines.md:21-24,35` 已规定四区、Zone2 仅列 workspace、空态保留 rails + docked banner，禁止居中 Welcome 覆盖工作台。

结论：需要把外层 Shell 的视觉几何、详情折叠策略、空态文案和中央工作台骨架作为独立的 App UI 任务；不能把问题归因给 `WorkbenchLayout` 或通过投放 demo catalog 掩盖无连接状态。

## 2. herdr 未挂载的根因链

- `src/HerdDesk.App/MainWindow.xaml.cs:42-53` 的 `BindRoot` 只执行 `AppServices.CreateProduction`、`ShellHost.Create`、`shell.StartAsync` 和绑定 UI，没有 endpoint 解析、RPC snapshot/subscription 或 terminal session 启动。
- `src/HerdDesk.App/Composition/AppServices.cs:64-88` 固定创建 `UnavailableAdapter`：RPC、terminal transport、renderer 均不可用，并将其能力列入 `Unavailable`。
- `src/HerdDesk.App/Composition/ShellHost.cs:8-26` 固定 `ProjectionCatalog { DaemonAvailable = false }`，所以即使有设备配置，Shell 也会落入 unavailable 状态。
- `src/HerdDesk.App/Composition/WorkbenchControlFactory.cs:10-16,34-51` 使用 `MissingLeaseStore`、NoOp host 和 NoOp terminal transport；其 observe/control 不会启动进程或连接 daemon。
- `src/HerdDesk.App/Views/SettingsPage.xaml.cs:128-135` 只调用 `Settings.RequestConnect(0)`；`src/HerdDesk.App/ViewModels/SettingsViewModel.cs:276-287` 只追加 `PendingConnects` 并写入未授权解释，没有消费者。
- 可用但未接入的实现存在于 `src/HerdDesk.Infrastructure/Terminal/TerminalCliProcessFactory.cs:43-69`（`OwnedChildProcess.Start` + `TerminalCliTransport`）和 `src/HerdDesk.Infrastructure/Rpc/RpcStdioConnectionFactory.cs:66-78`（启动 `herddesk-bridge rpc --socket-path`）。
- `src/HerdDesk.Core/DeviceSessions/DeviceSession.cs:79-138,182-188,425-499` 已提供可复用的 RPC actor、epoch、snapshot/subscription 和状态流，但 App 目前没有构造它，也没有把状态接到 `ProjectionCatalog.Aggregate`。
- `tests/Contract/Program.cs:89-116,182-207` 把生产组合根“不可用且不自动连接”作为当前契约，因此任何接线重构都必须更新注入与契约测试，不能只改 UI 文案。
- `docs/adr/approved-baseline.md:68-83,102-115` 要求 API/RPC 与 terminal stdio 分离，用户先启动 herdr；关闭 HerdDesk 只释放本应用拥有的 child，不停止 daemon/agent。

结论：截图中的“未挂载”是确定的组合根缺口，不是偶发启动失败。正确目标是“显式授权后连接现有 herdr daemon”，不是让 HerdDesk 自动拥有、启动或关闭 herdr daemon。

## 已验证与未验证边界

已验证：静态源码、现有单元/契约测试的设计意图、规划规范和用户截图中的空态表现。

未验证：真实 herdr 可执行文件和 endpoint、Windows named-pipe ACL、live RPC snapshot/subscription、WebView2 renderer、真实终端帧、WinUI 像素布局。`just ci` 或静态 XAML 检查不能把这些标为通过。
