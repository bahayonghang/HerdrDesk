# HerdDesk 工作台相对 herdr TUI 的差距

## Goal

让牧台主窗口在「已配置本机设备、已有工作区投影」时读起来像终端工作台：窄设备/会话轨、工作区（spaces）树、tab 条、分栏终端占中央；默认观察、不抢控制。空配置或未授权连接时仍是同一骨架，并诚实说明下一步，而不是居中设置页。

用户价值：从原版 herdr 的 spaces / 分栏终端能对应到牧台里同一类目标，而不要求像素复刻 TUI。

## Background

对照证据：[research/screenshot-gap.md](research/screenshot-gap.md)。

左图是 `just dev` 的 HerdDesk `--ui probe-results/dev-ui`；右图是原版 herdr TUI。产品是独立 Windows 客户端，不是 TUI 包装（`docs/plan/docs/02_产品定义与名称.md:9`）。四区 IA 仍有效（`02:37-39`，`.trellis/tasks/09-08-windows-desktop-full/ui-blueprint.md`）。禁止复制 herdr/herdrm 素材（`02:17`；HD-011 PRD 非目标）。

`--ui` 经 `MainWindow.BindRoot` 调用 `AppServices.CreateProduction`（`src/HerdDesk.App/MainWindow.xaml.cs:43-48`）。该工厂把 RPC、terminal transport、renderer 登记为 `UnavailableAdapter`（`src/HerdDesk.App/Composition/AppServices.cs:74-76`）。`ShellHost.Create` 把 `DaemonAvailable` 固定为 `false`（`src/HerdDesk.App/Composition/ShellHost.cs:11`）。空配置进入 `ShellLifecycle.NoDevices`，中央路由 Welcome（`ShellViewModel.cs:83,156-160`；`ShellPage.xaml.cs:137-139`）。状态行是枚举拼接（`ShellPage.xaml.cs:161-172`；`ShellViewModel.cs:649-656`）。

四区 XAML 已在：轨 220、树 240、中央单个 `TerminalHost`、详情 280（`ShellPage.xaml:63-108`）。`LayoutProjection` 已含 pane 单元格矩形（`src/HerdDesk.Contracts/State/ProjectionModels.cs:42-50`），UI 未消费。可见终端预算为 4（`src/HerdDesk.Contracts/ResourceBudgets.cs:67`；`PaneVisibilityCoordinator.MaxVisiblePanes`）。`SettingsViewModel.RequestConnect` 无 XAML（`SettingsViewModel.cs:264-269`）。`TerminalControlViewModel` 无 ControlBar。测试夹具 `AppTestHost.SessionState` 目前传入空 `Tabs` / `Layouts`（`tests/Unit/HerdDesk.App.Tests/AppTestHost.cs:60-69`）。

## Requirements

- **R1 诚实连接面**：默认 `--ui` 不得假装 daemon 在线或投放伪成功投影。未授权 live observe 时，骨架显示 `DaemonUnavailable` / `NoDevices` 及下一步（添加设备、诊断）。`CreateProduction` 的 Unavailable 登记必须在 UI 上可读，不能只表现为空白 ListView。生产路径禁止 `IsFakeSuccess`。
- **R2 工作台骨架**：任何生命周期都呈现四区骨架。空轨保留「设备与会话」「工作区」分区标题和添加/诊断入口；Welcome 不得盖住整窗。设置/诊断/关于仍是中央路由，返回骨架。
- **R3 可读状态**：顶栏用中文独立显示设备或会话、连接、agent 业务状态、未读、观察/控制。使用已有 `StatusPresentation` / `ShellStrings`（`ShellModels.cs:176-183`；`ShellStrings.cs:37-73`），禁止 `NoDevices · Offline · Unknown` 枚举 dump。
- **R4 导航语义**：列 1 = 设备与命名会话；列 2 = 工作区（spaces）。pane 不是与工作区等权的第二份扁平清单；选中工作区后，pane 出现在 tab mosaic。选择始终带完整 `DeviceId` / `SessionKey` / `PaneKey` / `ConnectionEpoch`。
- **R5 tab 与分栏**：选中工作区后，中央显示该工作区的 `TabProjection` 条。当前 tab 的 `LayoutProjection` pane 矩形按比例铺成 mosaic，受 4 个可见槽位约束；超出走 `WaitingForCapacity` / 需切换，不静默丢 pane。无 layout 时单槽占满。`zoomed` 只显示 focused pane。
- **R6 控制权**：选择、聚焦、第一帧、renderer Ready 不得置 `ControlVerified`。中央操作栏绑定已有 `TerminalControlViewModel`（观察 / 请求控制 / 释放 / 接管确认）。`RequestControl` 仍不授予 lease。
- **R7 独立品牌**：不复制 herdr TUI 壁纸、图标、键位表。快捷键沿用产品表（Ctrl+K 等）。密度目标是终端优先，不是像素克隆。
- **R8 授权后的 observe**：仅在用户明确授予 live herdr 观察后，组合根才允许真实 RPC / terminal session / WebView renderer。Settings「连接」在未授权时只记录 `PendingConnects` 并说明原因；授权后才观察，默认不控制。GUI 退出只杀本应用 child。

## Acceptance Criteria

父任务规划验收：

- [x] **AC-P1**：缺口清单与文件锚点见 Background 与 research。
- [x] **AC-P2**：视觉语言选定为 A（终端优先四区）。`design.md` / `implement.md` 与三个子任务已建立。
- [x] **AC-P3**：live observe 单独放在子任务 `observe-opt-in`，默认不授权。

实施验收（由子任务完成；父任务集成时复核）：

- [x] **AC1（R2, R3, R7）chrome**：`NoDevices` / `DaemonUnavailable` 下四区骨架可见；状态为中文；中央不是单独 Welcome 表。App 单测覆盖状态文案与 `!DaemonOnline`。
- [x] **AC2（R4, R5）mosaic**：合成 snapshot 含两个 tab、同一 tab 两个 pane 矩形时，ViewModel 产出 tab 条与两槽 mosaic；第 5 个可见 pane 为 WaitingForCapacity。选择 tab/pane 不置 `ControlVerified`。不启动 herdr。
- [x] **AC3（R6）control bar**：操作栏主键随 `TerminalAccess` 切换（请求控制 / 取消 / 释放）；无 challenge 时无接管。单测不发输入。
- [ ] **AC4（R1, R8）observe-opt-in**：无授权时组合根仍 Unavailable 且 UI 不显示绿色就绪。授权开关的测试只断言工厂选择与 `PendingConnects` 消费，CI 不启动 herdr/`--ui`。
- [ ] **AC5（R7）**：不新增 herdr 键位或壁纸资源；产品 AC 保持 `not_run`。

## Out of scope

- 嵌入或像素复刻 herdr TUI；复制 herdrm 素材。
- 替代 herdr daemon；自动安装/启动 daemon。
- 文件双栏、附件、粘贴预览、SSH 表单、helper 安装、创建 workspace/agent 对话框（HD-017 对话框可后续接「新建」禁用原因）。
- 本规划轮次改 `src/`、live herdr、takeover、输入。
- 将 G0 或 AC01–AC48 标为通过；L3 IME/DPI/Narrator；L4 soak。

## Key decisions

- 视觉语言 **A**：终端优先的四区工作台。不是 B（TUI 模仿），不是 C（只接线、外观不动）。
- 四区保留，权重改为：窄设备/会话 | 工作区 spaces | tab+操作栏+mosaic | 详情默认收起。
- 默认 `--ui` 保持诚实空/不可用。mosaic 用合成投影在单测中验收，不在生产投放伪 catalog。
- live observe 是子任务且另需授权；chrome 与 mosaic 不依赖 live herdr。

## Risks and deferred

- `LayoutProjection` 只有单元格矩形、没有 split 树；mosaic 用矩形比例布局。若上游矩形重叠或单位变化，只显示可解析槽位并保留诊断，不猜像素墙纸。
- `PaneVisibilityCoordinator.Show` 会调用 `BeginObserve`。无真实 transport 时 host 必须是空操作，不能假 Ready。
- WebView 多实例成本：遵守 4 槽；隐藏即 `Hide` 释放 renderer。
- 窄窗 (<800) 仍用现有 overlay，不在本任务重做 breakpoint。

## Children

| Child | 交付 | 拥有 AC |
|---|---|---|
| `workbench-chrome` | 骨架、中文状态、操作栏、Settings 连接按钮（不发 RPC） | AC1, AC3, 部分 AC5 |
| `tab-mosaic` | tab 条、layout mosaic、4 槽预算 | AC2, 部分 AC5 |
| `observe-opt-in` | 授权后的组合根接线 | AC4；启动须 live herdr 授权 |

子任务依赖：chrome 可先于 mosaic。mosaic 依赖 chrome 的中央宿主。`observe-opt-in` 依赖前两者，且默认不 `task.py start`。
