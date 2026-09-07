# HerdDesk.Contracts

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Contracts

生成日期：2026-09-08。HerdDesk 自有类型。上游 SDK 与 RPC schema 不在本项目。

## 职责

提供身份、终端信封、输入上下文与决策的 BCL 记录类型。本目录是 **G0 已编译子集**。规划端口草案在 [docs/plan/contracts/HerdDesk.Contracts.cs](../../docs/plan/contracts/HerdDesk.Contracts.cs)，尚未进入 `src/`。

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

## 草案多出、src 未实现

| 草案符号 | 说明 |
|---|---|
| `ConnectionPhase` | 设备会话相位 |
| `TerminalMode` | Observe / Control |
| `CapabilityProfile` | 缺能力为 unknown，禁止写成“已测 false” |
| `TerminalEvent` 层次 | 草案 `TerminalFrame` 含 `Epoch`；另有 `TransportEnded(ExitCode, SawClosedEnvelope)` |
| `IRpcConnection` | 请求与订阅分离；mutation 不盲目重试 |
| `ITerminalTransport` | 领域层先授权再构造；`ReleaseAsync` 不关 pane |
| `ITerminalRenderer` | `ApplyAsync` ≠ 呈现完成 |
| `IControlPolicy` | 对应 Core 的 `InputPolicy.Evaluate`，签名不同 |
| `IRemoteFileService` | P4 文件面 |

扩展本项目时按任务增量加入类型。禁止用草案整文件覆盖 `TerminalModels.cs`。

## 依赖

- `HerdDesk.Contracts.csproj`：空 SDK 项目，属性来自根 `Directory.Build.props`。
- 允许：BCL。禁止：WinUI、SSH、RPC 方法名、进程启动。
- 被 `HerdDesk.Core` 与 SmokeTests 引用。

## 入口与测试

类库，无可执行入口。行为由 [../HerdDesk.Core](../HerdDesk.Core/CLAUDE.md) 与 [../../tests/HerdDesk.Core.SmokeTests](../../tests/HerdDesk.Core.SmokeTests/CLAUDE.md) 覆盖。

## 约束

- `Bytes` 保持 `ReadOnlyMemory<byte>`，消费完成前不得回收缓冲。
- 当前无 JSON 映射；未来未知 enum 应保留原值并降级。
