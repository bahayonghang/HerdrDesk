# HerdDesk.Core

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Core

生成日期：2026-09-08。G0 领域逻辑标本：单连接帧解析与输入放行策略。DeviceSession actor、Store、未读、错误语义等规划能力尚未实现。

## 职责

- 解析一条完整 JSON 记录（不含尾部 LF），产出 `TerminalFrame` 或 `TerminalClosed`。
- 在可信 `InputContext` 上判定 `RendererInput` 是否允许写入。
- 失败后锁存：同一 parser 实例不再接受后续记录。

传输分帧、进程生命周期、RPC、WinUI 不属于本项目。规划中的 `IControlPolicy.CanSend` 对应本目录 `InputPolicy.Evaluate`；`ITerminalTransport` 尚未实现。草案 `TerminalFrame` 含 `Epoch`，本解析器按单连接构造，帧类型本身不带 epoch。

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

## 依赖

- 项目引用：`../HerdDesk.Contracts/HerdDesk.Contracts.csproj`。
- 允许：BCL、`System.Text.Json`。
- 禁止：XAML、WebView2、OS 凭据、SSH、进程。

## 入口

类库。由 smoke runner 与未来 Infrastructure 调用。

## 测试

[../../tests/HerdDesk.Core.SmokeTests](../../tests/HerdDesk.Core.SmokeTests/CLAUDE.md) 覆盖 22 项解析与策略断言。Python 侧有对等意图的校验器，见 [../../scripts/CLAUDE.md](../../scripts/CLAUDE.md)。两套实现未自动生成，不能互相替代。

## 关键文件

- `TerminalFrameParser.cs` — 失败锁存解析器。
- `InputPolicy.cs` — 纯函数策略。
- `HerdDesk.Core.csproj` — 仅 Contracts 引用。

## 约束

- 先校验全部字段，再写入 `lastSequence` / `closed`。
- `Convert.FromBase64String` 之后必须用 `Convert.ToBase64String` 回比，拒绝非 canonical 编码。
- `JsonDocument.Parse` 默认允许重复键；本解析器自行 `CheckDuplicateKeys`。
- 规划中的 DeviceSession 串行 actor 尚未存在；不要在本目录加入 UI 或网络循环。
