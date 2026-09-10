# HerdDesk.Infrastructure

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Infrastructure

HD-007 BCL adapters: configuration store and diagnostics. HD-008: owned child process, bounded NDJSON, request/subscription RPC over two `herddesk-bridge` children. HD-009: SchemaV1 RPC document decoder. HD-013: process-level `TerminalCliTransport` over `herdr terminal session` stdio. HD-020 L1: OpenSSH config preview, staged connection test, and a private host-key trust store. HD-021 L1: helper manifest, in-memory consent, and atomic publish state machine. HD-022 L1: per-SessionKey `RemoteSessionTransportSet` wrapping HD-021 helper / configured herdr over `ssh -T` (request RPC, event RPC, per-pane terminal). HD-027 L1: `Files/FileBridgeProtocolCodec` reads `filebridge/spec/test-vectors/`. HD-028 L1: `LocalFileEndpoint`, `FileBridgeClient`, `RemoteFileService` on disposable local roots. No WinUI, live SSH host, live helper install, `herdr machine` catalog, or herdr daemon control.

## 职责

- 把注入的 `AppDataPaths` 根目录映射到 settings/cache/logs。
- 原子写入 `device-profiles.json`，覆盖时写一份 `.bak`，显式恢复只读该备份。失败注入钩子是 `internal`（`InternalsVisibleTo` 测试程序集）。
- 受限 `DiagnosticEvent` 写入有界 JSONL（UTF-8 无 BOM）；ID 只出现 alias。写失败只增加 dropped，不使进程崩溃。
- `OwnedChildProcess` 用绝对路径和 `ArgumentList` 启动；取消最多等 3 秒后只 Kill 记录的 direct PID，不杀进程树，不 `server stop`。
- `IRpcRequestConnection` 与 `IRpcSubscriptionConnection` 各一个 OS 进程。pending map 不用于订阅事件。`WhenCompleted` / `WhenReady` 供 HD-010 区分 request EOF 与 subscribe ack。L2 named-pipe ACL 为 UNVERIFIED。
- `TerminalCliTransport` 每个实例一个 PaneKey/ConnectionEpoch 和独立 `TerminalFrameParser`。argv 为 `herdr [--session S] terminal session {observe|control} TARGET --cols --rows`。默认永不 takeover。仅当 `TerminalTakeoverAuthorization.Confirmed` 且 attempt id 匹配时，control argv 追加 `--takeover`。stdout 为 `terminal.frame` NDJSON；stdin 为 typed input/resize/scroll/release，无逐命令 ACK。Dispose 只停 direct CLI child。L2 live herdr 为 UNVERIFIED。

## 依赖

- 项目引用：Contracts、Core。
- `Ssh/` — HD-020 L1 `OpenSshConfigResolver`、`SshProcessSpecFactory`、`HostKeyTrustStore`、`SshConnectionTestService`。HD-021 L1 helper planner/publisher。HD-024 L1 `SshFailureClassifier` 与 `SshRecoveryBlockStore`（认证/host-key block 持久化）。ViewModel 不接收 raw argv。L2 隔离 Windows/OpenSSH、live helper deploy 与 live auth 为 UNVERIFIED。
- `SshTransports/` — HD-022 L1 `RemoteSessionTransportSet`：每 SessionKey 两个 `ssh -T` RPC child 加按需 terminal child；stdout 污染 fail-closed；stderr 单独排空；重建换 epoch。HD-025 L1 `SshConnectionLease` 在启动 pair/terminal 前取得准入租约。L2 live SSH 为 UNVERIFIED。
- 禁止：PackageReference、WinUI、WebView2、SSH.NET、硬编码用户目录、读写 `herdr machine` catalog。

## 入口

类库。由 `HerdDesk.App` 组合根构造。测试：`tests/Unit/HerdDesk.Infrastructure.Tests`。SchemaV1 decoder 失败用例在 `tests/Contract`。

## 关键文件

- `Configuration/AppDataPaths.cs`
- `Configuration/AtomicConfigurationStore.cs`
- `Diagnostics/JsonlDiagnosticSink.cs`
- `Diagnostics/DiagnosticAliasProjector.cs`
- `Host/UnavailableAdapter.cs`
- `Process/OwnedChildProcess.cs`
- `Process/BoundedNdjsonReader.cs`
- `Rpc/RpcStdioConnectionFactory.cs`
- `Rpc/RpcRequestConnection.cs`
- `Rpc/RpcSubscriptionConnection.cs`
- `Rpc/RpcEnvelopeParser.cs`
- `Rpc/SchemaV1/` — snapshot/event DTOs and `RpcStateDecoder` (owned `JsonElement` extensions; runtime schema hash UNVERIFIED)
- `Terminal/TerminalCliProcessFactory.cs`、`TerminalCliTransport.cs`、stdout pump、stderr drainer、command serializer、write queue
- `Rpc/ResourceCommandAdapter.cs` — HD-017 verified operation mapping；无 generic method/argv；`close_group` 仅在显式确认时写入。L2 live mutation 为 UNVERIFIED。
- `Ssh/` — HD-020 L1 OpenSSH locator/resolver/spec factory/trust store/test service。HD-021 L1 `HelperDeployment/` manifest/probe/receipt/planner/publisher。L2 isolated OpenSSH 与 live helper deploy UNVERIFIED。
- `SshTransports/` — HD-022 L1 remote session transport set。复用 HD-008 RPC 与 HD-013 terminal port，不另建 parser。HD-025 L1 `SshConnectionLease` 在启动子进程前向 `ConnectionAdmissionPolicy` 取 pair/terminal 租约；失败归还。L2 live SSH UNVERIFIED。
- `Files/` — HD-027 L1 codec 与 Windows 本地名 mapping-required。HD-028 L1 `LocalFileEndpoint`、`FileBridgeClient`、`FileBridgeProcessFactory`、`RemoteFileService`。L2 FS/SSH/TOCTOU UNVERIFIED。
- `Clipboard/` — HD-031 L1 `WindowsClipboardSnapshotReader`（显式 Read，无 watcher）与 `AttachmentCache`（注入 AppDataPaths 私有目录、原子 temp+rename、TTL/容量、仅 exact-id 且 lease=0 淘汰）。测试注入 fake snapshot 与 disposable cache root。L2 live clipboard UNVERIFIED。
- `Quality/` — HD-033 L2 环境清单与短窗 process/handle/idle CPU 采集。不发明 8h 序列。不是 AC 通过。
