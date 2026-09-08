# HD-016 technical design

## Planned files and public surface

在 `src/HerdDesk.Contracts/Terminal/TerminalControlPorts.cs` 增量定义 `IControlLeaseCoordinator`、`ControlLeaseState`、`ControlIntent`、`ControlAttemptOutcome`、`TakeoverChallengeView`、`InputSubmissionOutcome`；不暴露 challenge secret、Process 或 raw command。Core 在 `src/HerdDesk.Core/TerminalLease/` 增加 `ControlLeaseCoordinator.cs`、`ControlLeaseMessage.cs`、`ControlTransition.cs`、`TakeoverChallenge.cs`、`TerminalInputCoordinator.cs` 与 `CandidateFrameBuffer.cs`。

App 接入点为 `src/HerdDesk.App/ViewModels/TerminalControlViewModel.cs` 和 `src/HerdDesk.App/Controls/TerminalControlBar.xaml`；它们只调用 coordinator command、显示 immutable state。HD-014 的 `TerminalHost.xaml` 继续拥有 renderer surface，trusted native host 将 transport observation 分流给 lease，把 frame 给 renderer；JS 消息不能构造 ownership proof。

## State and actor

`ControlLeaseState` 包含 target、projection epoch/revision、active observe/control binding、TerminalAccess、ControlVerified、attempt id、lease generation、outcome 与可选 public challenge metadata。single-reader bounded actor 是唯一 mutation 点；transport/render/store effects 返回带 generation 的 message，actor 不在网络 await 时阻塞生命周期消息。

稳定状态：

| Access | Verified | 含义 |
|---|---:|---|
| Disconnected | false | 无可用 terminal binding |
| Observing | false | 当前 observe full baseline 可读 |
| Acquiring | false | 保留 observe，并有一个 control candidate |
| Controlling | true | candidate 已证明 ownership 且 renderer baseline ready |
| Unknown | false | 连接可能产生过副作用但无法证明状态，只读 |

Busy 不写 Access；`LastAttempt=Busy` 且 active binding 仍是 Observing。任何 invariant 破坏直接调用 fail-closed transition：readonly、generation++、cancel candidate/current write、发布 stable code。

## Control acquisition

`RequestControl` 从 actor 内向 Store port读取 current target/freshness；通过后创建 attempt id，并要求 binding host 打开 no-takeover candidate。active observe 继续向 renderer供帧。candidate 的 ownership/lifecycle 和 frame 都由 trusted host送 actor；candidate frame buffer 必须以 full 开头且复用 HD-014 24 MiB in-flight预算，溢出即 abort。

只有 classifier 的 VerifiedOwned observation 与 attempt metadata 完全相等才 latch ownership。actor 随后暂停 observe forwarding、把 renderer 设只读并 Bind(candidate epoch)，重放 candidate full/delta；收到 last parsed seq ack 后再检查 Store。四项 gate 同一 transition 完成后，才关闭旧 observe、设置 Controlling/true 并切 writable。

若 Busy，关闭 candidate而不动 observe，并创建 challenge。若 Rejected，回 Observing 且不给 challenge。若 Unknown/timeout/protocol loss，candidate 进入 release/close，state 显示 Unknown 直到连接关闭，再恢复当前 observe；所有 candidate completion 都按 attempt id 过滤。

## Takeover confirmation

内部 `TakeoverChallenge` 保存随机 nonce、PaneKey、projection epoch/revision、observe epoch、busy evidence id、attempt id 和展示摘要 hash。public view 只含 target摘要与 confirmation handle。它没有“沉默即同意”或全局记忆；状态变化、target切换、Store stale、第二次请求、disconnect、release 或首次使用都会销毁。

`ConfirmTakeover(handle)` 在 actor 内重验全部绑定，原子 consume 后才向 host传 `TakeoverAuthorization`。Infrastructure 只接受这个 typed authorization，并按 HD-004 证实的固定 flag 构造 candidate。普通 `RequestControl` 的类型结构无法携带 takeover。

## Input and revocation

native host 将 HD-014 input 包装为当前 binding PaneKey/epoch；`TerminalInputCoordinator` 仍从 Store/lease重取权威上下文并执行现有 `InputPolicy`。通过后调用当前 control transport typed send 一次，并保存仅含 command id/disposition 的内存 ledger。bytes 在调用完成后释放，不进入 state snapshot。

actor 对 transport receipt completion 只更新同 lease generation 的 outcome。revocation 先调用 renderer `SetReadOnly(true)`，再 generation++ 并关 transport；旧 completion 不能打开 write gate。NotSent 显示未发送；在断线窗口内 flushed/in-flight 都显示结果未知。恢复的新 observe不消费旧 ledger payload。

Resize/scroll 使用相同 lease gate但各自 typed validator；local viewport变化仍可留在 renderer。EmulatorReply route 不与 user input复用，直到独立审计任务明确允许。

## Release, recovery, and UI

`ReleaseControl` 原子撤销 write gate，清 challenge，调用 HD-013 idempotent release，然后由 binding host建新 observe epoch并要求 full baseline。release receipt 只进入诊断，不把 Access直接设 Observing。HD-018 只发送 `RecoverObserve`；coordinator 不提供 `RecoverControl`。

Control bar 在 Observing 显示“请求控制”；Acquiring 显示取消；Busy 显示明确“保持观察/接管…”；Unknown 显示“状态未知，返回观察”。takeover dialog固定 breadcrumb与证据时的目标，确认前再次说明会取代现有 controller。selection change 关闭 dialog并使 handle失效。

## Error and concurrency rules

稳定错误码至少区分 target_stale、control_busy、control_rejected、ownership_unverified、takeover_confirmation_stale、candidate_backpressure、terminal_disconnected、input_not_sent、input_outcome_unknown。诊断不含 nonce、input bytes、terminal text或未脱敏路径。

actor mailbox有界；每 pane同时最多一个 observe、一个 candidate、一个 promotion和固定数量 input effects。Dispose先 readonly/revoke，再等待 owned effects；只调用 transport release/dispose，不操作 daemon或 server resource。
