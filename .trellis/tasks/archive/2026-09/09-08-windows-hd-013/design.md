# HD-013 technical design

## Planned files and ports

在 `src/HerdDesk.Contracts/Terminal/` 增量定义 `ITerminalTransportFactory.cs`、`TerminalOpenRequest.cs`、`TerminalTransportEvent.cs`、`TerminalWriteReceipt.cs`；不整文件复制 `docs/plan/contracts/HerdDesk.Contracts.cs`。在 `src/HerdDesk.Infrastructure/Terminal/` 增加 `TerminalCliTransport.cs`、`TerminalCliProcessFactory.cs`、`TerminalStdoutPump.cs`、`TerminalStderrDrainer.cs`、`TerminalCommandSerializer.cs`、`TerminalWriteQueue.cs` 与 `TerminalExitClassifier.cs`。

`TerminalOpenRequest` 包含 PaneKey、epoch、Observe/Control、initial columns/rows、绝对 executable path 以及由 HD-016 生成的可选 takeover authorization。factory 再校验 identity/epoch；它只映射固定参数。session name 只通过 `--session` 的独立 ArgumentList 项传入，target 取已验证 pane target mapping，不用标题或 agent type。

`ITerminalTransport` 暴露只读 identity/mode、`ReadEventsAsync` 和 typed `SendInputAsync`、`ResizeAsync`、`ScrollAsync`、`ReleaseAsync`。每项返回 `TerminalWriteReceipt(CommandId, Disposition, Code)`；Disposition 只有 NotSent、WrittenUnacknowledged、UnknownAfterDisconnect。receipt 不进入持久化，也不能被 ViewModel 显示成 remote success。

## Pumps and ownership

factory 启动进程后立即并行启动 stdout pump、stderr drainer、stdin writer 和 exit watcher，由一个 transport CTS 管理。stdout 是唯一 producer：固定 buffer 累积到 LF，允许末尾 CR 仅按实际 capture 规则处理，把完整 record交给当前 epoch parser。每个 decoded buffer 转成 owned array/owner，事件 consumer ack/dispose 后才释放。v0.9.0 CLI 已丢弃内部 Graphics 消息；pump 只解析出现在 stdout 的 NDJSON，不为图像补通道。

frame channel 同时限制 item 与 bytes；budget reservation 先于 publish，consumer 完成才归还。channel 满或 consumer 停滞时，不等待到 child pipe 永久阻塞：lifecycle latch 记录 Backpressure，取消当前 transport、结束 direct child，并发出最终 TransportEnded。所有 exit paths 经同一一次性 latch，避免 EOF、exit watcher 和 dispose 重复发布终态。

stderr drainer 不把 raw 行送 UI 队列。它按 HD-004 fixture 匹配最小 signal fingerprint，输出 `TerminalDiagnosticCode`/计数和可选 ownership observation；未知内容只记长度/计数。classifier 输入绑定 executable hash、mode、pane、epoch、control attempt id；未匹配或版本不符返回 Unknown。

## Writer state machine

typed serializer 先生成单条 canonical UTF-8 JSON + LF，不接受 raw JSON。`TerminalWriteQueue` 使用有界 Channel 加 byte semaphore；每项状态为 Queued→Writing→Flushed 或 Queued→NotSent / Writing→Unknown。writer 独占 StandardInput，整条 write 后 flush，再完成 receipt，因此两个 caller 不会交错。

transport 从 Open 进入 Closing 后原子拒绝 enqueue。断连时 drain 未开始项为 NotSent；当前 Writing 项为 UnknownAfterDisconnect；已 flush 项保持 WrittenUnacknowledged。无论何种 disposition，coordinator 都不会据此重试。stderr 的 “input ignored” 无 request id，只产生会话级警告，不反向改写某一 receipt。

Release 在 control mode 只 enqueue 一个 `terminal.release` barrier；在 observe mode 直接开始连接关闭。barrier 完成后关闭 stdin 并等待 stdout/process 的有限退出。超时只停止 factory 创建的 direct client；不能遍历 parent/child tree，也不能定位或停止 daemon。`DisposeAsync` 可重复调用并等待四个 pump 收敛。

## Lifecycle and consumers

事件带 PaneKey、epoch 与 monotonically increasing local event id。`TerminalClosedObserved` 保存 reason-present/脱敏分类但不枚举未知 upstream reason；`StdoutEnded`、`ProcessExited`、`ProtocolFailed`、`ConsumerBackpressure`、`OwnershipObserved` 独立。HD-014 消费 frame，HD-016 消费 ownership/lifecycle；二者必须再次核对 current Store pane/epoch。

依赖方向为 Infrastructure→Core/Contracts；Contracts 保持 BCL-only。进程 abstraction 仅 internal 并在 Infrastructure 测试中 fake；Core 测试不引用 Process。配置和 diagnostics 分别复用 HD-007 的 path/options 与 redacted sink，不另建日志系统。

## Failure and cancellation contract

caller 取消单个未开始 command 只取消该 item；连接级 EOF/protocol/exit 取消全部 pumps。外部 app stopping 先禁止 enqueue、完成 NotSent/Unknown 分类、尽力释放 direct client；它不会触发 HD-018 reconnect。异常默认日志只含稳定 code、pane 的非敏感内部 correlation id、epoch、exit code 和计数。
