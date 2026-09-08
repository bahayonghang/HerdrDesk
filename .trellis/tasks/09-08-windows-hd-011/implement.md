# HD-011 实施计划

## 开始前

- [ ] 用户批准父计划及本任务实施，任务已 start；当前 planning 不授权创建 App。
- [ ] HD-009（`.trellis/tasks/09-08-windows-hd-009`）提供稳定投影、未知值与 epoch 语义。
- [ ] HD-010（`.trellis/tasks/09-08-windows-hd-010`）提供首次同步/重连/stale 投影和取消边界；这是完整 UI 集成前置。
- [ ] HD-007（`.trellis/tasks/09-08-windows-hd-007`）已建立并锁定实际 WinUI 项目/包版本和应用端口；不要凭规划文档猜 PackageReference。
- [ ] 从真实 WinUI 工程生成前端 Trellis spec，再编码；当前模板不得作为规范。
- [ ] 首次创建 App/测试项目时加入 `HerdDesk.slnx`、`just ci` 与 `.github/workflows/ci.yml`；未接入统一门禁不得称可交付。

## 顺序清单

- [ ] 定义 Shell/Navigation/Search 的 UI-only state、route 与命令，复用 Contracts 身份类型。
- [ ] 建立 App 启动壳和主题/本地化资源，先完成 Starting/NoDevices/Failed。
- [ ] 实现 device/session rail 与 workspace/pane tree，加入 loading/empty/stale/incompatible。
- [ ] 实现 selection coordinator，确保同名项保留完整 key 且切换不隐式控制。
- [ ] 实现 Ctrl+K 搜索索引、最近访问与 expired result。
- [ ] 接入 HD-007 配置端口，实现 Local Device/named session/explicit endpoint 编辑、validation、atomic save、恢复和首次连接。
- [ ] 实现 terminal font family/font size/zoom preview/save/restore，并接 HD-014/015 的本地 render/capability resize 端口。
- [ ] 实现 Diagnostics 版本/连接/epoch/queue/error 信息、默认脱敏导出预览与确认导出。
- [ ] 实现 single-instance ownership、secondary activation redirect、通知只导航和 startup/exit disposal 顺序。
- [ ] 接入完整 Settings/Diagnostics/About 路由；SSH provider 未到 HD-020 时只禁用对应分区，不阻塞本地连接。
- [ ] 实现宽/中/窄布局、details collapse、focus restore 和 AutomationProperties。
- [ ] 用 fake Store 跑 L1 component/ViewModel 测试；测 30 pane 和 100 results。
- [ ] 在 Windows 11 跑 L2 启动、resize、关闭资源与导航集成。
- [ ] 在交互桌面跑 L3 键盘、IME 冲突、屏幕阅读器、DPI、主题；记录 UNVERIFIED 项。

## 拟建测试路径

- `tests/Unit/HerdDesk.App.Tests/ShellViewModelTests.cs`：启动、空、失败、重试与 route availability。
- `tests/Unit/HerdDesk.App.Tests/NavigationIdentityTests.cs`：同名 pane、旧 epoch、删除后选择。
- `tests/Unit/HerdDesk.App.Tests/SearchPaletteTests.cs`：排序、最近、过期、composition、100 项时延。
- `tests/Unit/HerdDesk.App.Tests/LocalDeviceSettingsTests.cs`：空设备、named/explicit endpoint、validation/save/recover。
- `tests/Unit/HerdDesk.App.Tests/DiagnosticExportPreviewTests.cs`：默认脱敏、provider unavailable、用户确认。
- `tests/Unit/HerdDesk.App.Tests/TerminalDisplaySettingsTests.cs`：font/size/zoom validation/save/recover/observe no-resize。
- `tests/Integration.Windows/AppActivationTests.cs`：双实例重定向、通知 target、existing window focus、expired。
- `tests/Integration.Windows/AppExitOwnershipTests.cs`：启动取消/退出释放顺序和 pane 存活。
- `tests/Integration.Windows/Components/NavigationStatesTests.cs`：loading/empty/offline/disabled/selection/focus。
- `tests/Integration.Windows/Components/ResponsiveShellTests.cs`：区域可达、overlay focus trap 与恢复。
- `tests/Integration.Windows/Shell/AccessibilityScenarios.md`：Narrator、键盘、缩放、主题。

## 命令和证据

- [ ] `[现有] rtk proxy just ci`：保护当前 Core/Contracts；不证明 App UI。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.App.Tests/HerdDesk.App.Tests.csproj -c Release`：L1 ViewModel。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Components`：L2/桌面授权下的控件状态，不以 fake L1 替代。
- [ ] `[拟建] dotnet build HerdDesk.slnx -c Release`：App 建仓后纳入统一 build。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Shell`：L2。
- [ ] L3 记录真实 Windows/App SDK/缩放/主题/Narrator 和操作步骤；源码截图不算 native visual acceptance。

## 回滚与交接

- [ ] 壳失败可回退为稳定层级导航并关闭 recent cache；不更改 Core 投影。
- [ ] SSH provider 失败只禁用远端分区；Local Device 配置/诊断仍可用。配置保存失败保留最后有效 snapshot。
- [ ] 关闭窗口只释放本应用资源，不调用 server stop 或关闭 pane。
- [ ] App activation/config ownership 失败时拒绝第二 writer 并显示错误，不降级为两个独立配置写进程。
- [ ] 将 terminal placeholder 接口交给 HD-014，通知 deep-link 交给 HD-012，全局聚合交给 HD-023。
- [ ] AC19 只记录本地贡献；跨 3 设备通过前保持 not_run。
