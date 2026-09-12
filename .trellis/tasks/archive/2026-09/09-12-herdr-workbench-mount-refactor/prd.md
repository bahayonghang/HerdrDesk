# herdr 工作台排版与进程挂载优化重构

## Goal

解决用户截图中的两个可复现缺口：HerdDesk 空态看起来不像终端工作台，以及应用启动后没有把已运行的 herdr 会话接入 UI。最终结果是：无连接时仍保持清晰、可操作的四区工作台骨架；用户显式授权连接后，配置的本地 herdr endpoint 能沿 RPC 状态面和 terminal stdio 面进入设备、workspace、tab、pane 与终端渲染链路。

“挂载 herdr 进程”在本任务中定义为连接用户已经启动的 herdr daemon。HerdDesk 不拥有、自动启动、自动重启或在退出时停止 herdr daemon/agent。

## Confirmed facts

- `ShellPage.xaml:63-108` 已有四个 Grid 区域，但 `ShellPage.xaml.cs:102-121` 会在默认 `DetailsPaneKind.Collapsed` 时把详情列设为 0；四列没有明显面板边界，空 ListView 与中央 EmptyBanner 形成稀疏黑底。
- `ShellViewModel.StartAsync` 在无设备或 daemon 不可用时不会产生工作区选择；`ShellPage.xaml.cs:124-161` 因此只显示空态 banner，tab/mosaic 不会出现。
- `AppServices.CreateProduction` (`AppServices.cs:64-88`) 固定注入 `UnavailableAdapter`；`ShellHost.Create` (`ShellHost.cs:8-26`) 固定 `DaemonAvailable=false`。当前生产路径不会创建任何 herdr/RPC/terminal child。
- `SettingsPage` 的连接入口只追加 `PendingConnects`；`TerminalCliProcessFactory`、`RpcStdioConnectionFactory` 和 Core `DeviceSession` 虽已存在，但没有被 App 组合根串起来。
- 现有契约测试把“生产组合根不可用且不自动连接”作为当前行为（`tests/Contract/Program.cs:89-116,182-207`）；重构必须同步更新契约，而不是只改截图文案。

## Requirements

### R1 四区工作台可读性

在无设备、daemon 不可用、失败、已连接和有 pane 投影状态下都保留设备/会话、workspace、中央 workbench、详情四区的可识别骨架。空态采用 rails + docked banner，不以居中欢迎页覆盖工作台；详情栏的折叠行为、分隔和响应式断点要有明确策略。

### R2 herdr 语义对齐

列 1 展示 Device/Session，列 2 只展示 workspace（spaces）；中央展示当前 workspace 的 tab、最多 4 个终端槽位和观察/控制栏；pane 不再作为第二份等权的扁平导航。不得复制 herdr/herdrm 壁纸、图标或键位表。

### R3 显式连接编排

保存的 `DeviceProfile` 必须通过显式 endpoint/path 解析进入连接编排。用户点击连接后，编排器消费 `PendingConnects`，创建 `DeviceSession`，使用 RPC factory 获取 snapshot/subscription 并更新 projection；选择 pane 后才打开 `herdr terminal session observe`。无路径、无 endpoint、ACL/版本未知时 fail-closed 并显示可诊断原因。

### R4 两条通信面分离

JSON RPC 只能走 API socket 或 `herddesk-bridge`；terminal frame/input 只能走 `herdr terminal session` stdio。不得把 RPC 发到 herdr 二进制 client socket，不得把 herdr daemon 当作 HerdDesk owned child。

### R5 安全与生命周期

默认仍是 observe；选择、焦点、首帧、renderer ready、进程存活不能置 `ControlVerified`。GUI 退出只释放本应用启动的 bridge/terminal/SSH child；不自动 takeover、不回放断线输入、不停止现有 daemon/agent。

### R6 离线验证边界

代码和契约测试覆盖 fake process/bridge、状态投影、epoch/EOF/backpressure、布局策略与回滚；`just ci` 不启动 live herdr、WinUI、SSH 或 takeover。真实 herdr、named pipe、WebView2、像素布局和现场三设备体验保持 `UNVERIFIED`。

## Acceptance criteria

- [ ] 父任务能追溯两个子任务的文件边界、依赖顺序、验收和回滚点；父任务不直接进入实现。
- [ ] `workbench-layout` 子任务在静态/离线证据中验证四区节点、详情/断点策略、空态 banner、workspace-only 第二列、tab/mosaic 容量与无 fake-success；真实 WinUI 像素截图仍标 `UNVERIFIED`。
- [ ] `herdr-transport-mount` 子任务在 fake contract 中验证 `DeviceProfile → endpoint → DeviceSession → projection → terminal transport → renderer` 链路，连接失败可观察且 GUI 退出不停止 daemon；真实 herdr/pipe/WebView2 仍 `UNVERIFIED`。
- [ ] `just ci`、App/Infrastructure/Contract 相关离线 runner 与 `dotnet format --verify-no-changes --no-restore` 通过；G0 与 AC01–AC48 不得标为通过。

## Out of scope

- 自动安装、启动、重启或升级 herdr daemon；停止用户 daemon/agent；live takeover、输入回放或生产发布。
- 复制 herdr/herdrm 视觉资产或将 HerdDesk 变成 TUI 包装器。
- 文件双栏、附件、粘贴预览、SSH 编辑器和新建 workspace 对话框。
- 真实 WinUI/IME/DPI/Narrator、WebView2 多进程、named-pipe ACL、远端 SSH 和现场性能验收。

## Decisions and risks

- 先完成 layout 子任务，再完成 transport mount；后者依赖前者的 pane/terminal host 边界。
- 复用 Core `DeviceSession`/decoder/store，不在 App 自行解析 RPC；否则会产生第二份 projection source of truth。
- `LayoutProjection` 继续使用已有矩形 MVP；若真实上游矩形单位/重叠规则未知，只记录诊断，不猜测像素 split tree。
- 详情列是否默认展开由 layout 子任务以固定宽度和断点验收决定；不得仅凭静态 XAML 声称视觉通过。

## Child tasks

| 顺序 | 子任务 | 责任 | 依赖 |
|---|---|---|---|
| 1 | `09-12-workbench-layout` | 四区视觉边界、空态、详情/断点策略、tab/mosaic 宿主契约 | 无 |
| 2 | `09-12-herdr-transport-mount` | 显式连接编排、RPC/terminal 两平面接线、fake lifecycle contracts | 子任务 1 的宿主与 pane 边界 |
