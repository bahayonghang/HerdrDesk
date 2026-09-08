# HerdDesk.Contracts

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Contracts

生成日期：2026-09-08。HerdDesk 自有类型。上游 SDK 与 RPC schema 不在本项目。

## 职责

提供身份、终端信封、输入上下文、决策、API endpoint 解析、终端 lease 观测、renderer 标本、配置/诊断端口与 unavailable adapter 类型的 BCL 记录类型。本目录是 **G0 已编译子集**。规划端口草案在 [docs/plan/contracts/HerdDesk.Contracts.cs](../../docs/plan/contracts/HerdDesk.Contracts.cs)，尚未整文件进入 `src/`。HD-014 已增量编译 `ITerminalRenderer` / `IRenderFlowController`。

## 已实现类型（`TerminalModels.cs`）

| 类型 | 作用 |
|---|---|
| `DeviceId` | `Guid` 包装 |
| `SessionKey` | Device + `EndpointKey` + 可选 `SessionName` |
| `PaneKey` | Session + `WorkspaceId` + `PaneId` |
| `ConnectionEpoch` | `long Value`；重连必须换新值 |
| `TerminalAccess` | `Disconnected`, `Observing`, `Acquiring`, `Controlling`, `Unknown` |
| `InputOrigin` | `UserKey`, `CommittedText`, `ExplicitPaste`, `EmulatorReply` |
| `TerminalEnvelope` | 抽象基类 |
| `TerminalFrame` | `Sequence`, `Columns`, `Rows`, `Full`, `Bytes`（无 Epoch；parser 单连接） |
| `TerminalClosed` | `ReasonPresent`；不枚举 reason 字符串 |
| `RendererInput` | pane、epoch、origin、bytes 压平 |
| `InputContext` | 可信 host 创建；禁止从 renderer 消息反序列化 |
| `InputDecision` | `Allowed` + 稳定 `Code` |

`InputContext.ControlVerified` 默认 `false`。

## 已实现类型（`EndpointModels.cs`）

由可信 host 创建，禁止从 renderer 消息反序列化。`DeviceId` / `SessionKey` 是 endpoint 身份输入。`PaneKey`、窗口标题、agent 类型不能替代 endpoint identity。

| 类型 | 作用 |
|---|---|
| `EndpointPreferenceKind` | `Explicit`, `Default`, `Named` |
| `EndpointKind` | `NamedPipe`, `UnixSocket`, `FilesystemPath` |
| `EndpointAccessScope` | `LocalUser`, `Unknown` |
| `EndpointObservationKind` | `Missing`, `PermissionDenied`, `CrossUser` |
| `EndpointPreference` | 解析偏好；工厂 `Explicit` / `Default` / `Named` |
| `VerifiedEndpointMapping` | 受控已验证映射，不是 OS 发现结果 |
| `EndpointObservation` | 受控 ACL/缺失事实 |
| `EndpointResolutionConfig` | 映射表 + 观察；`Empty` 无映射 |
| `ResolvedEndpoint` | `Kind`, `CanonicalLocation`, `EvidenceId`, `AccessScope` |
| `EndpointResolutionFailure` | 稳定 `Code`、`DiagnosticId`、`RequiresExplicitConfiguration` |
| `EndpointResolutionResult` | 成功 `Endpoint` 或失败 `Failure` |

## 已实现类型（`LeaseModels.cs`）

由可信 host 创建，禁止从 renderer 消息反序列化。不存在 `Granted` 线类型。

| 类型 | 作用 |
|---|---|
| `TerminalLeaseOperation` | `Observe`, `RequestControl`, `RequestTakeover`, `ResizeWhileVerified`, `Release` |
| `TerminalStreamEndKind` | `None`, `StdoutEof`, `TerminalClosed`, `BridgeProcessExit`, `Unknown`。EOF 不是 pane exit |
| `TerminalControlSignal` | `None`, `Busy`, `Rejected`, `TakeoverRequired`, `TakeoverConfirmed`, `Released`, `Unknown`。无 `Granted` |
| `TerminalLeaseObservation` | 已观察 CLI/协议事实；`AdapterProvedWriteOwnership` 才是写权证明 |
| `TerminalLeaseResult` | `Access`, `ControlVerified`, `StreamEnd`, `PaneExitVerified`, 稳定 `Code` |

首帧、进程存活、窗口焦点不是 `ControlVerified` 证据。

## 已实现类型（`RendererModels.cs`）

由可信 host 创建。禁止从 renderer 消息反序列化 `InputContext`。

| 类型 | 作用 |
|---|---|
| `ImeHostEvent` | `PreeditUpdate`, `Commit`, `KeyWhileComposing`, `KeyIdle` |
| `WebMessageDirection` | `HostToRenderer`, `RendererToHost` |
| `RendererQueueState` | `Ready`, `Backpressured`, `Faulted` |
| `WebMessage` | type / version / epoch / pane / payload 长度 / 方向；可选 claimed origin |
| `RendererQueueDecision` | 有界队列结果；`ParseConsumedIsPresented` 恒为 false |
| `RendererSurfaceState` | renderer 表面状态；不含 WinUI |
| `WebMessageLimits` | JSON 16MiB、帧 8MiB、输入 64KiB、链接 2048 |
| `RenderToken` / `RenderConsumption` / `RenderApplyResult` | 在途 token 与 apply 回执；ack 不是 GPU 呈现 |

## 草案多出、src 未实现

| 草案符号 | 说明 |
|---|---|
| `TerminalEvent` 层次 | 草案 `TerminalFrame` 含 `Epoch`。HD-013 使用独立 `TerminalTransportEvent` / `TerminalOwnedFrame`，不覆盖草案整文件 |
| `IRpcConnection` | 请求与订阅分离；mutation 不盲目重试。HD-008 已用 request/subscription 端口 |
| `IControlPolicy` | 对应 Core 的 `InputPolicy.Evaluate`，签名不同 |
| `IRemoteFileService` | P4 文件面 |

扩展本项目时按任务增量加入类型。禁止用草案整文件覆盖 `TerminalModels.cs`。

## 依赖

- `HerdDesk.Contracts.csproj`：空 SDK 项目，属性来自根 `Directory.Build.props`。
- 允许：BCL。禁止：WinUI、SSH、RPC 方法名、进程启动。
- 被 Core、Infrastructure、Terminal.Web、App 与测试引用。

## HD-007 增补

`ConfigurationModels.cs`：`DeviceProfile` / `SessionProfile` / `IDeviceProfileStore`。`DiagnosticModels.cs`：受限 `DiagnosticEvent` 与 `IDiagnosticSink`。`HostModels.cs`：unavailable adapter 端口。`Rpc/RpcPorts.cs`：`IRpcConnectionFactory`、`IRpcRequestConnection`、`IRpcSubscriptionConnection`、`RpcRequestId`、`RpcFailure`。HD-014 增量编译 `Terminal/ITerminalRenderer.cs` 与 `Terminal/IRenderFlowController.cs`。HD-016 增量编译 `Terminal/TerminalControlPorts.cs`（`IControlLeaseCoordinator`、`ControlLeaseState`、attempt outcome、public challenge view）。不覆盖规划草案整文件。公开 challenge view 不含 nonce。`busy`/`rejected`/`cancelled` 不是 `TerminalAccess` 值。

## HD-013 增补

`Terminal/`：`TerminalMode`、`TerminalOpenRequest`、`ITerminalTransport`、`ITerminalTransportFactory.OpenAsync`、typed input/resize/scroll commands、`TerminalWriteReceipt`（NotSent / WrittenUnacknowledged / UnknownAfterDisconnect）、owned frame events。公开 port 不暴露 `Process`、stdin writer 或 raw stderr。`ControlVerified` 不由首帧/进程存活/焦点置位。L2 live herdr 为 UNVERIFIED。

## HD-016 增补

`Terminal/TerminalControlPorts.cs`：`IControlLeaseCoordinator`、`ILeaseTargetStore`、`IControlBindingHost`、`ControlLeaseState`、`ControlAttemptOutcome`、`TakeoverChallengeView`、`InputSubmissionOutcome`。`RequestControl` 不能携带 takeover。公开 API 无 raw argv、nonce 或 resource CRUD。L2 live lease 为 UNVERIFIED。

## HD-017 增补

`ResourceOperationPorts.cs`：`ResourceKey`、create/rename/close intent、`IResourceCommandCoordinator`、`IResourceCommandTransport`、`IResourceQueryTransport`。Agent kind 仅 Claude/Codex/OpenCode。无动态 RPC method、argv 或 approval bypass。`workspace.close` 的 `close_group` 缺省不发送。L2 live mutation 为 UNVERIFIED。

## HD-014 增补

`Terminal/ITerminalRenderer.cs`：`BindAsync` / `ApplyAsync` / `ReadInputsAsync` / `SetReadOnlyAsync` / `FocusAsync`。`ApplyAsync` 完成只表示 parser consumed。`Terminal/IRenderFlowController.cs`：有界 enqueue、token ack、cancel、reset。`ITerminalRendererFactory.CreateAsync` 默认返回 null。不覆盖规划草案整文件。L2 WebView process 与 L3 DPI 为 UNVERIFIED。

## HD-009 增补

`State/`：`ConnectionPhase`、`CapabilityProfile`（`VerifiedOperations`，缺能力不写成已测 false）、投影与 decoded snapshot/event、`IRpcStateDecoder`。身份仍为 `DeviceId` / `SessionKey` / `PaneKey` / `ConnectionEpoch`。Muse/Qwen 为未知 agent。运行时 schema hash 默认 `UNVERIFIED`。不覆盖规划草案整文件。

## HD-010 增补

`State/IDeviceSession.cs`：`IDeviceSession`、`DeviceSessionState`、`DeviceFreshness`、`ISessionNotificationSink`。App 只订阅 typed state。`IRpcStateDecoder.DecodeEntityRead` 解码 getter 响应。`IRpcRequestConnection.WhenCompleted` / `Failure` 与 `IRpcSubscriptionConnection.WhenReady` 区分 request EOF、subscription EOF 与 ack。`RpcCodes` 增加 `rpc_subscription_lost`、`rpc_request_lost`、`rpc_reconcile_failed`。

## 入口与测试

类库，无可执行入口。行为由 [../HerdDesk.Core](../HerdDesk.Core/CLAUDE.md)、[../HerdDesk.Infrastructure](../HerdDesk.Infrastructure/CLAUDE.md)、[../../tests/HerdDesk.Core.SmokeTests](../../tests/HerdDesk.Core.SmokeTests/CLAUDE.md)、[../../tests/Unit](../../tests/Unit/HerdDesk.Core.Tests/CLAUDE.md)、[../../tests/Unit/HerdDesk.Infrastructure.Tests](../../tests/Unit/HerdDesk.Infrastructure.Tests/CLAUDE.md) 与 [../../tests/Contract](../../tests/Contract/CLAUDE.md) 覆盖。

## 约束

- `Bytes` 保持 `ReadOnlyMemory<byte>`，消费完成前不得回收缓冲。
- 未知 enum 用 `WireEnum<T>` 保留 raw；未知 object field 的 owned clone 在 decoded 输入上，不进入 UI snapshot。
