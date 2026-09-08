# HD-010 technical design

## Ports and actor ownership

拟在 `src/HerdDesk.Contracts/State/IDeviceSession.cs` 定义只读 `ConnectAsync`、`DisconnectAsync`、`ReadStatesAsync`；拟在 `src/HerdDesk.Core/DeviceSessions/` 增加 `DeviceSession.cs`、`DeviceSessionState.cs`、`DeviceSessionMessage.cs`、`ReconcilePlanner.cs`、`DeviceSessionOptions.cs`。构造注入 request/subscription factory、HD-009 decoder/Store、`TimeProvider`、diagnostic sink；不依赖 Infrastructure concrete type。

`DeviceSession` 使用单 reader bounded channel。外部 command 和内部 effect completion 都变成 message；只有 handler 可以改变 phase/epoch/dirty/Store。网络 effect 在后台运行并把 owned result 发回，actor 不 `await` 长 RPC，因此 snapshot 飞行期间仍可累积 invalidation。

核心消息：`ConnectRequested`、`SubscriptionAcknowledged`、`Invalidated`、`SnapshotCompleted`、`EntityReadCompleted`、`ReconcileDue`、`CalibrationDue`、`ConnectionEnded`、`DisconnectRequested`、`StopRequested`。每个内部消息含 epoch 和 operation id；重复 completion 只记录一次安全诊断。

## State and synchronization flow

状态为 Offline→Connecting→Synchronizing→Ready；异常 transport→Stale，schema mismatch→Incompatible。Connect 先取消/await 旧 epoch effects，再生成正 epoch，清空旧 capability，打开 request/subscription。subscription ack 成功后才发 snapshot effect；HD-008 在 ack 前不 yield events。

actor 在 ack 后把 event 映射为 dirty scope（entity 或 whole session）。snapshot 完成时先验证 epoch/operation，再由 HD-009 原子安装；若 dirty generation 自请求开始已变化，则安排 250 ms reconcile。reconcile 捕获当前 dirty generation，优先 verified entity getter；任何未知/失败触发 full snapshot。完成后仅清除已覆盖 generation，期间新增 dirty 保留并再次调度。

当 snapshot 安装且覆盖到当前 generation、subscription 仍活跃，phase 才 Ready。5 s calibration 是初始可注入值；它执行只读 full snapshot 并复用同一原子安装路径。持续事件不会创建无限 timer：每个 epoch 至多一个 coalesce timer、一个 calibration timer、一个 read effect。

mailbox 使用 `BoundedChannelFullMode.Wait` 向 HD-008 subscription 施加背压；若 transport 自身 bounded queue overflow，它结束枚举并发送 ConnectionEnded。对 UI 的 state stream capacity=1、DropOldest，只合并 immutable 状态快照，不表示业务 event 已处理或 notification 已发送。

## Cancellation and failures

epoch CTS 绑定 request connection、subscription connection、timers 和 read effects。manual disconnect：cancel→dispose two connections→await effect tasks→Store Offline/readonly；unexpected EOF：先同步 MarkStale/clear capabilities，再 cancel。HD-018 只能通过新的 ConnectRequested 创建新 epoch，不能复用 handles/parser/dirty set。

诊断只记录 session alias、epoch、operation、dirty scope count、duration/outcome/error code；不记录 event JSON/title/path。`rpc_subscription_lost`、`rpc_request_lost`、`rpc_schema_incompatible`、`rpc_reconcile_failed` 分开。错误处理不能修改上一份投影内容，只改变 freshness/capability。

## Rollback

增量 reconcile 有缺陷时启用显式 degraded option：仍保持已确认 subscription 作为 invalidation，但所有 dirty 都取 full snapshot并显示“校准中”；若 subscription 不可靠则停用实时 projection、状态 stale，仅保留用户触发 snapshot。不得用旧 snapshot 保持 Ready。
