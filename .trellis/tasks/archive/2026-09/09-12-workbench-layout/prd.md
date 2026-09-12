# 四区工作台排版与空态重构

## Goal

把当前稀疏的空壳改成终端优先的四区工作台：设备/会话、workspace、中央 tab+mosaic、详情区在空态和有投影时都可识别，同时保留 HerdDesk 自己的 WinUI 视觉，不复制 herdr/herdrm 资产。

## Evidence

- `ShellPage.xaml:63-108` 已有四列，但 `ShellPage.xaml.cs:102-121` 会因 `DetailsPaneKind.Collapsed` 把详情列隐藏；无边界/背景的空 ListView 使四区不可读。
- `ShellPage.xaml.cs:124-161` 在 Welcome 状态只显示中央 EmptyBanner；`WorkbenchLayout.cs:201-259` 无投影时最多返回一个 placeholder 槽。
- `.trellis/spec/frontend/component-guidelines.md:21-24,35` 要求四区、workspace-only 第二列、rails + docked banner 空态。

## Requirements

- **L1**：在无设备/daemon 不可用/失败状态保留四区标题、面板边界和添加/诊断入口；空态 banner 不覆盖 rails。
- **L2**：明确 `<800`、`800–1199`、`>=1200` 的 rail/tree/details 策略；详情默认策略必须由可观察行为定义，不靠隐含宽度。
- **L3**：Zone2 只呈现 workspace；选中 workspace 后显示 tab strip、`LayoutProjection` 矩形 mosaic、最多 4 个可见 TerminalHost 和超额 WaitingForCapacity tile。
- **L4**：中央操作栏保持观察/请求控制/释放语义，选择、焦点、首帧和 renderer ready 不得提升 `ControlVerified`。
- **L5**：新增/变更 XAML 节点有 AutomationProperties.Name；不得新增 React、WASDK 2.4.0 或 live herdr。

## Acceptance Criteria

- [ ] App/surface tests 检查四区节点、workspace-only 过滤、详情/断点策略、空态 banner 与 4-slot 上限。
- [ ] 合成 snapshot 含两个 tab/两个 pane rect 时，ViewModel 产生 tab+mosaic；第 5 个 pane 为 WaitingForCapacity；没有 fake daemon/catalog。
- [ ] 固定宽度 >=1200 的真实 `--ui` 截图（若后续获准）能逐区识别；当前规划与离线测试不得把该截图证据标成已通过。
- [ ] App unit、XAML/Integration.Windows surface contract、`dotnet format` 和 `just ci` 通过；G0/AC01–AC48 状态不变。

## Out of scope

真实 herdr 数据、RPC/terminal 连接、WebView2/IME/DPI/Narrator、文件双栏与新建 workspace 对话框。
