# HD-011 设计

## 原 plan 决定与补充建议

已定：四区主窗口、完整身份键、WinUI 壳、Mica 外壳、语义色和 Ctrl+K（`docs/plan/docs/02_产品定义与名称.md:39-51`）。建议：使用单一 `ShellViewModel` 组合只读投影，页面 ViewModel 不复制 Store；breakpoint、密度和标题栏高度留首次 WinUI 视觉验证后冻结。

## 拟建文件责任

- `src/HerdDesk.App/App.xaml(.cs)`：拟建应用启动、主题资源和顶层异常路由。
- `src/HerdDesk.App/Activation/AppActivationCoordinator.cs`：拟建 single-instance 注册、二次启动/通知 activation 重定向和目标重解析。
- `src/HerdDesk.App/MainWindow.xaml(.cs)`：拟建窗口、标题栏和响应式壳。
- `src/HerdDesk.App/Views/ShellPage.xaml`：拟建四区布局。
- `src/HerdDesk.App/Controls/DeviceSessionRail.xaml`：拟建设备/session 列表。
- `src/HerdDesk.App/Controls/WorkspacePaneTree.xaml`：拟建 workspace/pane 分层树。
- `src/HerdDesk.App/Controls/SearchPalette.xaml`：拟建 Ctrl+K 搜索、最近与过期结果。
- `src/HerdDesk.App/Views/SettingsPage.xaml`：拟建本地设备/session/endpoint、外观、通知、诊断隐私与未来 SSH provider 分区。
- `src/HerdDesk.App/Views/DiagnosticsPage.xaml`：拟建诊断概览、连接详情、复制错误码、脱敏导出预览与明确导出。
- `src/HerdDesk.App/Views/AboutPage.xaml`：拟建版本、独立客户端声明、许可与支持矩阵入口。
- `src/HerdDesk.App/ViewModels/`：拟建 `ShellViewModel`、`SearchPaletteViewModel`、`SettingsViewModel`、`DiagnosticsViewModel`。
- `src/HerdDesk.App/Resources/Strings/`、`Themes/`：拟建中英文资源键与语义主题资源。

## 屏幕与组件树

```text
MainWindow
├─ AppTitleBar: product/device/session/connection/control summary
└─ ShellPage
   ├─ DeviceSessionRail
   ├─ WorkspacePaneTree
   ├─ ContentHost
   │  ├─ Welcome/Empty/LaunchDiagnostics
   │  └─ TerminalHost placeholder（HD-014 挂载）
   ├─ DetailsPane placeholder（Files/Info/Diagnostics）
   └─ SearchPalette + teaching/error notifications
```

## ViewModel 状态

- `ShellState`：`Starting | Ready | NoDevices | DaemonUnavailable | Failed`，包含可本地化 error category 与 retry capability。
- `NavigationItemState`：稳定 key、label、kind、connection phase、agent status、unread count、isSelected、isExpanded、isStale、capability reasons。
- `SelectionState`：`None | Device | Session | Workspace | Pane`，pane 必须携完整 `PaneKey` 与投影 epoch。
- `SearchState`：query、results、selectedIndex、isOpen、isComposing、latency、result freshness。
- `DetailsPaneState`：`Collapsed | Info | Files | Diagnostics`；文件内容到 HD-029 才启用。
- `LocalDeviceDraft`：稳定 DeviceId、label、已核验本机 herdr 路径、多个 typed SessionProfile 草稿及 validation/save state；凭据字段不在本任务。同设备可配置两个 named session/endpoint，各自生成完整 SessionKey。
- `TerminalDisplayPreferences`：font family、font size、zoom、theme；只保存验证过的本地显示值，确切范围随 HD-005/007 实际控件锁定。
- `SettingsState`：`Loading | Ready | Dirty | Saving | Saved | ValidationFailed | SaveFailed | PermissionDenied`，保存失败保留 draft 并恢复已提交 snapshot。
- `DiagnosticsState`：版本/连接 phase/epoch/queue/error categories/export preview；敏感项默认 redacted。
- `RouteAvailability`：`Enabled | Loading | Disabled(reason) | Failed(error)`，用于设置/诊断/详情而非散落 bool。

## 命令及效果

- `SelectNavigationItem`：只改变选择并请求路由；不抢控制权。
- `OpenSearch`/`CloseSearch`/`MoveSearchSelection`/`ActivateSearchResult`：composition 时全局 accelerator 让位。
- `RetryProjection`：请求 HD-009/HD-010 facade 重取状态；不盲重试 mutation。
- `OpenSettings`/`OpenDiagnostics`/`OpenAbout`/`ToggleDetailsPane`：打开稳定路由。
- `EditLocalDevice`/`ValidateLocalEndpoint`/`SaveLocalDevice`/`DiscardSettings`：调用 HD-007 的 `IDeviceProfileStore` 和唯一原子配置存储；失败不覆盖最后有效配置。修改某个 session 的 endpoint/named identity 时释放该旧 SessionKey 的自有连接并建立新绑定，不继承控制权，保留同设备其他 session。
- `BuildDiagnosticPreview`/`ConfirmDiagnosticExport`：先展示默认脱敏字段，再由用户明确选择保存位置；预览/导出不含 ANSI、输入、clipboard/file content。
- `ApplyTerminalDisplayPreview`/`SaveTerminalDisplayPreferences`/`RestoreTerminalDisplayPreferences`：preview 只到 renderer；保存经 HD-007 原子配置端口。
- `RemoveRecentReference`：只删除本地引用，不删除远端实体。
- `RequestAddDevice`：导航到 Local Device 设置并可在 P1 直接保存/连接；选择 SSH 类型时展示“P3/HD-020 提供”或已注入的 SSH provider。

## 状态到 UI 的投影

- Starting：先显示窗口骨架和 progress ring；超时转 DaemonUnavailable/Failed，不用无限 spinner。
- NoDevices：显示添加本机/设备与诊断；按钮若无 provider 显示 disabled reason。
- Offline/Stale：保留最后名称但整体灰态、显示时间与重新连接；缓存不能显示绿色 Ready。
- Incompatible：保留只读诊断和版本信息，隐藏/禁用写命令。
- Error：展示稳定错误类别、复制诊断 ID、重试/打开诊断；不展示未脱敏 stderr。
- Settings：加载时可取消返回；validation 在保存前完成；原子保存/恢复由 HD-007 端口负责，UI 显示 SaveFailed/PermissionDenied。
- Terminal display：font/zoom 变更由 HD-014 重算 cell pixels 和 IME anchor；observe 保持 server cols/rows 且只本地缩放，control 也必须经 HD-015/016 capability+lease 才可请求 resize。
- Diagnostics：连接/renderer/file provider 未到阶段时标 unavailable，不把占位值写成 0/healthy；导出前必须预览 redaction。
- Selection/Hover/Focus：分别使用背景、边框和 focus visual；未读点带 accessible name/count。

## 焦点、键盘与响应式

- Tab 顺序：title commands → device rail → tree → content → details；F6 在区域间循环。
- 树遵守 WinUI TreeView 的左右展开/收起与上下移动；Enter 激活，Space 不隐式 takeover。
- 搜索弹层打开保存当前 focus token，激活结果等待 content Ready 后聚焦，关闭恢复原控件。
- 宽屏同时显示 rail/tree/content/details；中宽收起 details；窄屏 rail/tree 用 overlay 且保持目标 breadcrumb 可见。
- 所有状态使用资源字符串与 AutomationProperties；pane label 不作为 automation ID。

## 数据、安全与错误语义

- App 只消费 Core 的不可变投影；herdr 是权威，本地 Store 可丢弃（`docs/plan/docs/03_架构与数据流.md:56-82`）。
- 搜索索引存完整 key、规范化 label、kind、recent timestamp；不存 terminal text。
- renderer/通知携带的目标只作为请求，host 用当前 Store 重新解析；过期即停止。
- 未知 enum 显示 Unknown 并禁用相关写操作，契合 `src/HerdDesk.Contracts/CLAUDE.md:56-59`。

## 实例激活与退出顺序

- 启动先通过 Windows App SDK/受支持 API 获取跨进程 single-instance ownership，再初始化配置 writer；`SemaphoreSlim` 仅能串行进程内调用，不能作为实例互斥证据。
- secondary activation 只传版本化 route intent（normal/settings/diagnostics/notification target），主实例重新解析当前 Store；通知 intent 无 command/input/takeover 字段。
- 主实例把已有窗口恢复/置前，等待 route Ready 后聚焦；expired target 显示通知上下文，不启第二窗口。
- 退出先拒绝新 activation，取消 pending route/startup，停止建立新连接，释放 renderer/本应用 bridge/SSH/file child，再释放配置 owner；daemon/agent/pane 不在 disposal 集合。
