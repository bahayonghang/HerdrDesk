# HD-029 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start。
- [ ] HD-028（`.trellis/tasks/09-08-windows-hd-028`）交付结构化 list/stat/copy/progress/conflict/cancel port 和 fixture。
- [ ] HD-023 提供稳定 DeviceId/freshness；HD-024 的 offline/auth 状态可显示。
- [ ] 使用 disposable local/remote directories；不得用用户研究数据做 destructive 测试。

## 顺序清单

- [ ] 与 HD-028 冻结 UI 所需 location/entry/job/progress/conflict/cleanup 投影。
- [ ] 实现单 FilePane 的 generation/cancel、breadcrumb、list 和全状态。
- [ ] 组合双栏/窄屏 tab，持续显示明确 source→destination。
- [ ] 实现 immutable TransferDraft/target lease 和确认 dialog。
- [ ] 实现 TransferQueue、进度/速度/ETA unknown、取消和完成条件。
- [ ] 实现 Replace/KeepBoth/Cancel conflict dialog 和 concurrent conflict token。
- [ ] 实现 active jobs 退出确认、stale cached directory 和 completed target focus。
- [ ] 加入键盘、AutomationProperties、本地化、主题与 focus restore。
- [ ] 跑 L1 ViewModel/component/property tests；覆盖 focus/device switch 100 次。
- [ ] 跑 L2 filebridge Windows/SSH disposable 传输；L3 人工验证拖放/对话框/辅助功能。
- [ ] 把完整性与攻击场景交 HD-032 最终汇总。

## 拟建测试路径

- `tests/Unit/HerdDesk.App.Tests/Files/FilePaneGenerationTests.cs`：迟到 list、取消、stale/offline。
- `tests/Unit/HerdDesk.App.Tests/Files/TransferTargetLeaseTests.cs`：device/pane/focus 切换 100 次。
- `tests/Unit/HerdDesk.App.Tests/Files/TransferQueueProjectionTests.cs`：0B、unknown total、failure/cleanup。
- `tests/Unit/HerdDesk.App.Tests/Files/ConflictDialogTests.cs`：Replace/KeepBoth/Cancel/token mismatch。
- `tests/Integration.Windows/Components/Files/FileWorkspaceStatesTests.cs`：loading/empty/error/permission/responsive。
- `tests/Integration.Windows/Files/DualPaneScenarios.md`：真实 local/remote、拖放、退出与 screen reader。

## 命令和证据

- [ ] `[现有] rtk proxy just ci`：当前离线 gate；不证明文件 UI/传输。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.App.Tests/HerdDesk.App.Tests.csproj -c Release --filter Files`：L1 ViewModel。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Files`：L2 component + disposable directories。
- [ ] L3 记录 Windows/remote OS/filebridge/SSH/DPI/Narrator、每个 job 的脱敏 route/hash/outcome。

## 回滚与结束门

- [ ] 传输后端/安全不可靠时可隐藏 copy/upload，仅保留结构化只读浏览。
- [ ] 冲突/target lease 缺陷时禁用拖放，回退显式选源、选目标、确认。
- [ ] 取消/退出只停止本应用 job，绝不按通配符删除文件或停止 herdr。
- [ ] AC31/AC33 的 UI 贡献交 HD-032；完整后端/故障证据未齐前保持 not_run。
