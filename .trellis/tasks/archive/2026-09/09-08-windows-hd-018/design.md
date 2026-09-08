# HD-018 technical design

## Ownership and planned files

Core 新增 `src/HerdDesk.Core/Recovery/RecoveryPolicy.cs`、`RecoveryFailure.cs`、`RecoveryDecision.cs`、`RecoveryBackoff.cs`；均为无 I/O 的纯函数/值类型。`DeviceSession` 增加 recovery messages/timer，但仍是 RPC/Store owner；`ControlLeaseCoordinator` 增加 projection-stale、projection-ready 和 terminal-failed transitions，但仍是 terminal owner。

不建立 `RecoveryManager` 单例或第二份状态仓库。App 的 `src/HerdDesk.App/Recovery/RecoveryBindings.cs` 只把 typed `DeviceSessionState` 路由给对应 lease，并将 immutable状态投影给 UI；它无 timer、epoch、retry count 或 mutation权。

Windows测试都进入 `tests/Integration.Windows/HerdDesk.Integration.Windows.csproj` 的 `Recovery/` 目录；Core policy/actor测试进入 `tests/Unit/HerdDesk.Core.Tests/Recovery/`。不创建新的 recovery test project。

## Failure model and decision

`RecoveryFailure` 含 scope（Rpc/Terminal/Renderer/Application）、cause、SessionKey、可选 PaneKey、epoch、wasUserInitiated、retryClass 和 stable diagnostic id。raw stderr/terminal/input不进入该对象。`RecoveryPolicy.Decide` 只返回 Stop、AwaitUser、RetryNow或RetryAfter(delay)；I/O owner执行决定。

retryable transient使用第1..6次基础delay `1,2,4,8,16,30s`，加由 injected random source产生的 bounded jitter并封顶30s；第6次后继续以30s capped delay还是 AwaitUser由设备类型策略明确固定，P2本地默认 AwaitUser，P3远端由HD-024扩展。manual Retry取消唯一timer并发一次立即尝试；并发点击合并。

ManualDisconnect、AppStopping、Schema/ProtocolIncompatible、Authentication、HostKeyChanged直接Stop/AwaitUser。RequestEof、SubscriptionEof、BridgeExit、DaemonUnreachable和Terminal/Renderer transient可retry，但策略永远不返回 StartDaemon/RestartService。

## RPC recovery sequence

HD-010 actor收到current-epoch异常后在同一turn：

1. 原子把 projection标 Stale，清 capabilities，发布 state；
2. rollover/cancel旧epoch effects与pending map；
3. 向 lease发送 ProjectionBecameStale；
4. 根据 policy建立至多一个 timer/effect。

retry触发时 actor创建新 ConnectionEpoch，建立 HD-008 request/sub connections，并运行既有 subscribe-ack/snapshot/dirty reconcile。任何步骤失败回到 Stale并让 policy决定下一次；schema incompatible转 Incompatible/AwaitUser。只有干净收敛后发布 ProjectionReady(new epoch, fresh snapshot)。

旧 epoch completion被入口 guard丢弃并计数；它不能重置 retry attempt或覆盖 failure cause。manual disconnect/app stop设置terminal latch后，晚到timer只清理自己。

## Terminal and renderer recovery

`ProjectionBecameStale` 在 lease actor中的第一步是 `SetReadOnly(true)`、lease generation++、清 challenge/candidate/input ledger payload并关闭 active transport。HD-013负责将queued/writing命令归类；lease只保留不含bytes的 outcome。

`ProjectionReady` 到达后重新读取 Store：只有原 selected PaneKey完整相等、实体仍存在、fresh且仍可见时才调用 `OpenObserve`。新的 transport epoch从allocator取得，takeover字段在类型上不存在/为false。renderer销毁旧baseline并 Bind新epoch；收到full frame及parser-consumed ack后才显示 Observing/Ready。

terminal-only failure而RPC仍Ready时 lease actor可直接按同一 policy重开 observe，但每次先重验Store；`terminal.closed` 后若Store已无pane则停止。renderer crash销毁renderer local state并重建observe baseline，不请求虚构的full-frame命令。

## Input outcome and notification baseline

断开时 HD-013 NotSent映射“未发送”；Writing/flush竞态和 WrittenUnacknowledged映射“结果未知”。UI可显示command count/correlation，不能显示或缓存payload。new lease generation没有retry入口，用户只能重新输入。

DeviceSession在恢复snapshot安装时发 `BaselineEstablished`，HD-012据此清 transition comparison；只有基线之后的新 event可产生通知。无法补回的瞬态事件显示诊断事实，不伪造event replay。

## Shutdown and process ownership

AppStopping由composition root广播一次：DeviceSession停止timer/reconnect，lease先readonly，再dispose terminal；HD-008/013各自仅停止factory创建的direct child。process creation effect在启动前后检查stopping generation；若竞态创建成功，仅结束该pid并不启动下一轮。

不使用包含daemon/agent的Job Object，不查找同名process，不发server stop。可选本地crash测试由外层harness强制终止GUI process，再用独立client检查daemon/pane/agent；跨网络/SSH产品级AC15由HD-026汇总。

## UI projection and diagnostics

`RecoveryViewState` 由 owner states纯投影：Fresh、Stale(retrying, attempt, nextUtc)、AwaitingUser(reason)、RebuildingProjection、ReobservingTerminal、Recovered。它不成为状态源。last-known labels标灰并带“数据可能过期”；写命令统一消费 fresh capability gate。

诊断包含component、cause、hashed device/session id、epoch、attempt、delay、outcome和owned pid；不含endpoint、用户名、绝对路径、ANSI或input。重复EOF可聚合计数，但第一原因与最终decision保留。
