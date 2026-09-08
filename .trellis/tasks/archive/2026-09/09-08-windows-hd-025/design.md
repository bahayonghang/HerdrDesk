# HD-025 · 技术设计

## 所有权与拟建文件

- `src/HerdDesk.Contracts/ResourceBudgets.cs`：完整 owner key、不可变预算、占用快照、拒绝原因类型。
- `src/HerdDesk.Core/Connections/ConnectionAdmissionPolicy.cs`：唯一连接准入计算。
- `src/HerdDesk.Core/Terminal/TerminalQueueBudget.cs`：按 pane/epoch 的原始字节记账。
- `src/HerdDesk.Core/Projection/DirtySetBudget.cs`：实体 dirty 合并和 full-resync 降级。
- `src/HerdDesk.Infrastructure/SshTransports/SshConnectionLease.cs`：槽租约与子进程寿命绑定。
- `src/HerdDesk.App/Services/PaneVisibilityCoordinator.cs`：可见性意图和暂停提示编排。
- `tests/Unit/HerdDesk.Core.Tests/Connections/ConnectionAdmissionPolicyTests.cs`。
- `tests/Unit/HerdDesk.Core.Tests/Terminal/TerminalQueueBudgetTests.cs`。
- `tests/Integration.Ssh/Performance/SshConnectionBudgetTests.cs`。
- `tests/Integration.Windows/Terminal/PaneVisibilityBudgetTests.cs`。
- `scripts/Run-SshPerformanceMatrix.ps1`：受控目标上的测量编排，不内置凭据。
- `evidence/ssh-performance/environment.json`、`connection-budget.json`、
  `network-matrix.json`、`README.md`：环境、原始结果索引和未验证项。

以上均为未来实施路径，当前不存在；实施时先读取届时各目录的 `CLAUDE.md` 和已批准 spec。

## 连接预算

令 `S_d(t)` 是设备 d 当前已准入的完整 SessionKey 集合；每个租约 owner 都含
`DeviceId+SessionKey+ConnectionEpoch+channel`，terminal 再含 PaneKey，file/maintenance
再含 job id。旧 epoch 的 dispose 只能释放自身租约，不能命中新 epoch 或另一 endpoint。

对 session `s` 定义 `P_s∈{0,2}`：request+event 两槽在进程启动前原子预留，只有二者都
ready 才保持 2；部分启动失败释放整个 pair。另有 terminal `T_s`、file `F_s` 与
maintenance `M_s`，于是：

`C_d(t)=Σ(s∈S_d)(P_s+T_s+F_s+M_s)`，`C_ssh(t)=Σ(d∈D)C_d(t)≤B_ssh`。

- 活跃远端 `|D|≤3`；配置可多于 3 个，但多出的设备明确等待而非假装在线。
- `ΣT_s≤4`；每个 terminal 属于一个可见 PaneKey/epoch。
- `Σ(s∈S_d)F_s≤1` 且 `ΣF_s≤2`；P3 功能期为 0，P4 才启用。
- `ΣM_s≤1`；同一 Device 有 file job 时拒绝 maintenance。

`B_ssh` 是代码内单一常量，由本任务在支持矩阵上测进程/句柄/内存、连接建立时间与失败
恢复后写入证据和测试，不暴露用户调节。验收至少覆盖 3 个设备各 1 个 RPC pair 加 4 个
terminal 的 10 槽场景，以及再加 2 file+1 maintenance 的 13 槽场景；10/13 是样本需求，
不是多 session 总上界。若候选 `B_ssh` 容不下新 pair，完整 pair 进入公平等待队列。
租约在启动子进程前原子获得；启动失败、EOF、取消或 dispose 只归还一次。旧 epoch
进程在退出宽限期内仍占槽，避免重连风暴用重叠进程突破上限。

## 队列与内存口径

对 pane `p`：`Q_p(t)=Σ len(frame.Bytes)`，只包含已解析且未收到 renderer
`frame-consumed` 的 decoded 原始 payload。入队必须满足 `Q_p + next ≤ 24 MiB`；
单帧仍受 8 MiB、NDJSON 行仍受 16 MiB 边界。确认只能单调推进当前 epoch 的 seq，
旧 epoch、越界或回退确认均拒绝且不得释放新 epoch 字节。

`Q_terminal_raw=ΣQ_p≤4*24 MiB=96 MiB` 只是最坏原始 payload 预算。
进程内存必须另测：

`W_tree = W_app + W_webview + W_bridge + W_ssh + W_native_children`

`W_tree` 包括 Base64/JSON 暂存、对象/allocator、JS heap、renderer surface、pipe/socket
缓冲和进程固定开销，不能由 `Q_terminal_raw` 推导。采样同时记录两者以识别放大比。

流量积压诊断采用 `growth_rate=max(0, ingress_Bps-consume_Bps)`；若 `growth_rate>0`，
预计耗尽时间为 `(24 MiB-Q_p)/growth_rate`。它只用于提前显示压力，不放宽硬边界。

## 状态、并发与 UI

所有预算 mutation 在 Core 串行 owner 内完成；Infrastructure 只持有不可伪造租约，
UI 不能直接改计数。pair 等待按入队顺序并按 DeviceId 轮转，避免单设备多个 session
饿死其他设备。容量不足时优先把无可见 pane、无在途 mutation/file 的最早闲置 session
先标 `PausedForCapacity`，再 teardown pair；仍不足则新 session 保持 WaitingForCapacity。

pane 从 Visible→Hidden 时先切只读、取消当前 terminal、等待租约释放，再销毁 renderer；
session pair 仅在容量足够时保持。Hidden→Visible 若 session 已暂停，必须先原子取得 pair、
完成 snapshot，再取得 terminal 槽并生成新 epoch，以 observe 等待 full baseline。

第五个可见 pane 不静默挤掉当前焦点。窗口布局先把非焦点 pane 转 Hidden；若所有
四个都仍声明可见，则拒绝第五个并提供明确切换操作。排队不保留输入或控制权。

事件 actor 只保存每实体一个 dirty key。达到上限后原子清空 keys 并设置
`FullResyncRequired=true`；一次成功 snapshot 后清除。terminal queue 不采用此策略，
因为丢任一 delta 都会制造假正常。

## 错误、恢复与可观测性

稳定类别至少区分 `connection_budget_exhausted`、`terminal_queue_limit`、
`stale_render_ack`、`transport_cancel_timeout`、`process_start_failed`。未知下游错误保留
raw category 并降级；错误消息不带终端正文、凭据或私有路径。

队列超限会终止当前 transport、丢弃该 epoch 全部未确认缓存、标记 pane stale，
随后按 session 的设备退避策略重连为 observe；只有新 full baseline 后才恢复显示。取消超时仅
终止租约绑定的直接子进程；远端 helper 是否结束由 L2/L3 证据验证，不能靠推断标成功。
