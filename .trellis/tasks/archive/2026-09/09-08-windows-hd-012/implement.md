# HD-012 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start。
- [ ] HD-010（`.trellis/tasks/09-08-windows-hd-010`）提供 baseline/epoch/dirty 收敛边界。
- [ ] HD-011（`.trellis/tasks/09-08-windows-hd-011`）提供可取消、可报告 Expired 的完整身份路由。
- [ ] HD-007 定义配置/诊断端口；不自行新增第二套存储或日志框架。

## 顺序清单

- [ ] 定义 AttentionKey、BusinessState、Transition、Decision 与 ReadMarker。
- [ ] 先实现纯 reducer 和 baseline/reconnect/old-epoch 抑制。
- [ ] 实现去重、限流和全局/设备/session mute policy。
- [ ] 实现最小注意力缓存与层级未读派生。
- [ ] 实现应用内 NotificationCenter 全状态、键盘和 AutomationProperties。
- [ ] 接入 Windows notification sink；权限/注册失败回退 CenterOnly。
- [ ] 接入 HD-011 deep-link 路由，覆盖 Ready wait、取消和 Expired。
- [ ] 加入诊断分类和脱敏，检查无 terminal 正文/输入。
- [ ] 先跑 L1 reducer/ViewModel；再跑 L2 Windows notification activation。
- [ ] 用 L3 交互桌面验证 toast 点击、Narrator、focus 和多窗口前后台行为。

## 拟建测试路径

- `tests/Unit/HerdDesk.Core.Tests/Attention/BaselineSuppressionTests.cs`：initial/reconnect 不补发。
- `tests/Unit/HerdDesk.Core.Tests/Attention/TransitionDedupTests.cs`：event/refresh/snapshot 交错。
- `tests/Unit/HerdDesk.Core.Tests/Attention/UnreadAggregationTests.cs`：pane/session/device 精确清除。
- `tests/Unit/HerdDesk.Core.Tests/Attention/StaleAndUnknownTests.cs`：stale 与未知业务状态。
- `tests/Unit/HerdDesk.App.Tests/Notifications/DeepLinkRoutingTests.cs`：同名目标、关闭、旧 epoch、取消。
- `tests/Integration.Windows/Components/NotificationCenterStatesTests.cs`：empty/error/muted/expired/focus。
- `tests/Integration.Windows/Notifications/ActivationScenarios.md`：系统权限、前后台点击、打包状态。

## 命令与证据

- [ ] `[现有] rtk proxy just ci`：现有离线回归；不证明通知 UI。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj -c Release --filter Attention`：L1 reducer。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.App.Tests/HerdDesk.App.Tests.csproj -c Release --filter Notification`：L1 ViewModel。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Notifications`：L2 activation。
- [ ] L3 记录 Windows notification permission、安装形态、OS、实际点击目标、焦点与屏幕阅读器结果。

## 回滚与交接

- [ ] Windows sink 可关闭并回退应用内中心，不影响 Store 或 agent。
- [ ] 若 reducer 不能可靠区分 baseline，关闭 toast，仅显示侧边栏业务状态和 UNVERIFIED。
- [ ] 清理只限本应用注意力缓存；不枚举/删除远端数据。
- [ ] HD-023 接手跨设备聚合压力与同名身份回归；HD-034 接手安装包通知注册。
- [ ] AC17/AC18 只有相应 L1/L2/L3 证据齐全后才可申请改状态。
