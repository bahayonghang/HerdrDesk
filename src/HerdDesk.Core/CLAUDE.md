# HerdDesk.Core

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Core

生成日期：2026-09-08。G0 领域逻辑标本：单连接帧解析、输入放行策略、endpoint 纯映射、终端 lease 观测映射、HD-005 renderer L1 纯函数标本、HD-009 内存投影 Store、HD-010 每 SessionKey 一个 `DeviceSession` actor、HD-012 L1 注意力 reducer / 未读聚合、HD-016 L1 `ControlLeaseCoordinator`、HD-017 L1 `ResourceCommandCoordinator`，以及 HD-018 L1 `RecoveryPolicy` 与跨 owner 恢复信号。

## 职责

- 解析一条完整 JSON 记录（不含尾部 LF），产出 `TerminalFrame` 或 `TerminalClosed`。
- 在可信 `InputContext` 上判定 `RendererInput` 是否允许写入。
- 把 `DeviceId` / `SessionKey` / `EndpointPreference` 与受控配置映射到 API endpoint 候选。
- 把已观察的 observe/control/takeover/resize/release 信号映射到 `TerminalAccess`。
- 失败后锁存：同一 parser 实例不再接受后续记录。
- renderer L1：跨块 UTF-8 组装、epoch/seq 门、预编辑拒绝、有界队列分类、web message allowlist。不启动 WinUI 或 WebView2。

传输分帧、进程生命周期、RPC、WinUI 不属于本项目。规划中的 `IControlPolicy.CanSend` 对应本目录 `InputPolicy.Evaluate`。`ITerminalTransport` 在 Contracts；HD-013 实现位于 Infrastructure。草案 `TerminalFrame` 含 `Epoch`，本解析器按单连接构造，帧类型本身不带 epoch。HD-009 mapper 只消费 Contracts decoded 输入，不解析 raw JSON。HD-010 actor 把 event 当 invalidation，不直接 patch Store。

## 接口

### `TerminalFrameParser`

| 成员 | 说明 |
|---|---|
| `MaxLineBytes` | 16 MiB |
| `MaxFrameBytes` | 8 MiB |
| `Parse(ReadOnlyMemory<byte> json)` | 一条记录；UTF-8 严格解码 JSON 文本，终端 payload 不按 UTF-8 解码 |

校验要点：对象、禁止重复键、深度随 `JsonDocumentOptions.MaxDepth=64`、`encoding=ansi`、`seq` 为正 `u64`、连续 +1、首帧必须 `full=true`、宽高为正 `u16`、`full` 必须是 JSON bool、Base64 canonical、关闭后拒绝后续帧。`reason` 只记录是否非空字符串。JSON/UTF-8/Base64 异常统一转为 `TerminalProtocolException("malformed_terminal_record")`，不回显载荷。

异常码包括：`terminal_stream_not_active`、`line_bytes_limit`、`object_required`、`duplicate_json_key`、`unknown_terminal_type`、`unsupported_encoding`、`invalid_sequence`、`sequence_gap_or_replay`、`invalid_frame_dimensions`、`boolean_full_required`、`initial_full_frame_required`、`decoded_bytes_limit`、`noncanonical_base64`、`invalid_closed_reason`、`string_field_required`、`malformed_terminal_record`。

重连必须 `new TerminalFrameParser()`。`seq` 不可跨 epoch 比较。

### `InputPolicy`

静态 `Evaluate(InputContext, RendererInput) → InputDecision`。`MaxInputBytes = 64 KiB`。

拒绝码：`invalid_identity`、`wrong_pane`、`stale_epoch`、`control_not_verified`、`input_origin_denied`、`input_bytes_limit`。通过时 `Code=allowed`。

仅 `UserKey` / `CommittedText` / `ExplicitPaste` 可通过。`EmulatorReply` 必须另开审计通道。`Access` 必须为 `Controlling` 且 `ControlVerified=true`。pane 各字段非空，`DeviceId` 非 `Guid.Empty`。

本策略不获取控制权，也不能从终端帧推断授权。

### `EndpointResolver`

静态 `Resolve(DeviceId, SessionKey, EndpointPreference, EndpointResolutionConfig) → EndpointResolutionResult`。纯映射。不查询 OS 凭据、环境变量或 named pipe。不创建 terminal session，不发送 JSON RPC。

规则：explicit 只返回同一 canonical location；Default 无已验证映射时失败并要求显式配置，不猜 `%APPDATA%`；Named 不回退 default；Unicode 不改写为替换字符；permission-denied 与 cross-user 只分类、不改用户重试；远程 UNC/SMB 拒绝。

失败码：`invalid_identity`、`invalid_preference`、`explicit_configuration_required`、`named_session_unmapped`、`endpoint_not_found`、`permission_denied`、`cross_user_denied`、`remote_unc_rejected`、`unicode_encoding_error`、`ambiguous_mapping`。码与 `DiagnosticId` 不含路径。

### `TerminalLeaseProbe`

静态 `Map(TerminalLeaseObservation) → TerminalLeaseResult`。纯映射。不启动 herdr、不发送输入、不伪造 `terminal.granted`。

`ControlVerified` 仅当 `Access=Controlling` 且观测含 `AdapterProvedWriteOwnership`。首帧、进程存活、窗口焦点、stdin 写入都不能置位。Observe 发送输入返回 `observe_input_denied`。stdout EOF 与 `terminal.closed` 不是 pane 退出。无明确授权时为 `Acquiring`/`Unknown`。

结果码包括：`observing`、`observe_input_denied`、`acquiring`、`control_unconfirmed`、`control_verified`、`busy`、`rejected`、`takeover_not_confirmed`、`takeover_required`、`control_not_verified`、`resize_unacknowledged`、`resized`、`released`、`release_unacknowledged`、`input_result_unknown`、`fictional_granted_rejected`、`unknown_control_signal`、`disconnected`、`invalid_observation`。

`ClassifyStreamEnd` 区分 stdout EOF、`terminal.closed` 与桥进程退出。pane 死亡只能来自独立观测。

### Renderer L1 specimens

| 类型 | 说明 |
|---|---|
| `Utf8ChunkAssembler` | `Decoder.Convert(flush=false)` 保持不完整序列。`HeldIncomplete` 为真时不得已发出 U+FFFD。禁止逐块 `Encoding.UTF8.GetString` |
| `RendererEpochGate` | 每 epoch 一个 `TerminalFrameParser`；旧 epoch 为 `stale_epoch` 并锁存；`Reconnect` 新建 parser，seq 不跨 epoch 比较 |
| `CompositionPolicy` | `PreeditUpdate` → `preedit_not_sent`；`KeyWhileComposing` → `ime_owns_shortcut`；`Commit` 必须是 `CommittedText`，再走 `InputPolicy` |
| `RendererByteWindow` | 有界 FIFO；ack 必须等于最旧帧大小；`ParseConsumedIsPresented` 恒 false；越界不丢 delta；`ClassifyDeltaDrop` → `delta_drop_forbidden` 且 `RequiresFullReset`；`Reset` 拒绝更小 epoch |
| `WebMessagePolicy` | 版本 1 allowlist；未知 type / 超长 / 错 epoch / 错 pane 拒绝；输入走 `InputPolicy`；从不从消息构造 `InputContext` |
| `RenderFlowController` | 在 `RendererByteWindow` 上叠加 seq/full 与 token generation；cancel/reset 使旧 token 失效；ack 不是呈现 |

L1 通过不是 AC08/AC09 或 IME 真机通过。

## 依赖

- 项目引用：`../HerdDesk.Contracts/HerdDesk.Contracts.csproj`。
- 允许：BCL、`System.Text.Json`。
- 禁止：XAML、WebView2、OS 凭据、SSH、进程。

## 入口

类库。由 smoke runner、[../../tests/Unit/HerdDesk.Core.Tests](../../tests/Unit/HerdDesk.Core.Tests/CLAUDE.md) 与 App 组合根引用。Infrastructure 项目引用 Core。HD-009 `CapabilityGate` 在 Core；Infrastructure decoder 不判定能力。

## 测试

[../../tests/HerdDesk.Core.SmokeTests](../../tests/HerdDesk.Core.SmokeTests/CLAUDE.md) 覆盖解析、策略、endpoint resolver 与 lease mapper 断言。[../../tests/Unit/HerdDesk.Core.Tests](../../tests/Unit/HerdDesk.Core.Tests/CLAUDE.md) 覆盖 InputPolicy、parser latch、Core 程序集边界、HD-009 mapper/Store（fake decoded 输入）、HD-010 DeviceSession L1 race（fake RPC ports、barrier，不用 sleep）、HD-012 Attention reducer（基线/去重/未读/stale）、HD-016 ControlLeaseCoordinator（fake HD-013 事件）、HD-017 ResourceCommandCoordinator（fake RPC/Store），以及 HD-018 RecoveryPolicy/DeviceSession/lease 恢复（barrier，fake TimeProvider）。Python 侧有对等意图的校验器，见 [../../scripts/CLAUDE.md](../../scripts/CLAUDE.md)。两套实现未自动生成，不能互相替代。计数以本次 `dotnet run` 为准。L2 live subscribe interleave 为 UNVERIFIED，见 `implementation/hd-010-l2.json`。HD-012 Windows toast L2 为 UNVERIFIED，见 `implementation/hd-012-l2.json`。HD-016 L2 live lease 为 UNVERIFIED，见 `implementation/hd-016-l2.json`。HD-017 L2 live mutation 为 UNVERIFIED，见 `implementation/hd-017-l2.json`。HD-018 L2 live disconnect 为 UNVERIFIED，见 `implementation/hd-018-l2.json`。

## 关键文件

- `TerminalFrameParser.cs` — 失败锁存解析器。
- `InputPolicy.cs` — 纯函数策略。
- `EndpointResolver.cs` — 受控配置到 endpoint 的纯映射。
- `TerminalLeaseProbe.cs` — 已观察 lease 信号到 `TerminalAccess` 的纯映射。
- `Utf8ChunkAssembler.cs` / `RendererEpochGate.cs` / `CompositionPolicy.cs` / `RendererByteWindow.cs` / `WebMessagePolicy.cs` / `RenderFlowController.cs` — HD-005 L1 标本与 HD-014 flow controller。
- `HerdDesk.Core.csproj` — 仅 Contracts 引用。
- `Store/CapabilityGate.cs` — protocol/schema/hash 不匹配时 `VerifiedOperations` 为空。
- `Store/ProjectionMapper.cs` — 先校验全图再构造 immutable graph。
- `Store/DeviceProjectionStore.cs` — 当前 epoch、原子安装、stale、本地 revision。
- `DeviceSessions/DeviceSession.cs` — 单 reader mailbox actor：subscribe ack → snapshot → dirty 权威重读；event 不 patch Store。
- `DeviceSessions/ReconcilePlanner.cs` — create/close/graph 风险全量 snapshot；已验证 getter 定向读取。
- `DeviceSessions/DeviceSessionOptions.cs` — 250 ms coalesce、5 s calibration、可注入 `TimeProvider`。
- `Attention/AttentionReducer.cs` — HD-012 L1 纯 reducer：首次/重连基线抑制、transition 去重、stale/mute/限流、层级未读。Windows toast 不在 Core。
- `TerminalLease/ControlLeaseCoordinator.cs` — HD-016 L1 单 pane actor：默认观察；`RequestControl` 永不 takeover；busy 后一次性 challenge；`ControlVerified` 仅在适配器证明 + full baseline + renderer ack + Store 仍 fresh。L2 live lease 为 UNVERIFIED。
- `Commands/ResourceCommandCoordinator.cs` — HD-017 L1 schema-gated create/rename/close：capability/freshness/confirmation gate；mutation 只发送一次；timeout 后 UnknownOutcome 并只读查询；`close_group` 不得默认带上。不借用 HD-016 lease 作 CRUD 授权。L2 live mutation 为 UNVERIFIED。
- `Recovery/RecoveryPolicy.cs` — HD-018 L1 纯函数：failure taxonomy、1–30s backoff、不启动 daemon。`DeviceSession` 异常先发布 Stale 再单 timer 重连。`ControlLeaseCoordinator` 消费 projection-stale/ready 与 terminal/renderer 故障，只 `RecoverObserve`。L2 live disconnect 为 UNVERIFIED。

## 约束

- 先校验全部字段，再写入 `lastSequence` / `closed`。
- `Convert.FromBase64String` 之后必须用 `Convert.ToBase64String` 回比，拒绝非 canonical 编码。
- `JsonDocument.Parse` 默认允许重复键；本解析器自行 `CheckDuplicateKeys`。
- DeviceSession 不 await 长 RPC；effect 把 owned 结果发回 mailbox。协议事件不得静默丢弃。首次基线 snapshot 不调用 notification sink。恢复 snapshot 发 `baseline-established`。每 SessionKey 至多一个 reconnect 与一个 retry timer。永不自动启动/升级 herdr，不恢复旧控制权。
