# HerdDesk.Infrastructure

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Infrastructure

HD-007 BCL adapters: configuration store and diagnostics. HD-008: owned child process, bounded NDJSON, request/subscription RPC over two `herddesk-bridge` children. No WinUI, SSH, or herdr daemon control.

## 职责

- 把注入的 `AppDataPaths` 根目录映射到 settings/cache/logs。
- 原子写入 `device-profiles.json`，覆盖时写一份 `.bak`，显式恢复只读该备份。失败注入钩子是 `internal`（`InternalsVisibleTo` 测试程序集）。
- 受限 `DiagnosticEvent` 写入有界 JSONL（UTF-8 无 BOM）；ID 只出现 alias。写失败只增加 dropped，不使进程崩溃。
- `OwnedChildProcess` 用绝对路径和 `ArgumentList` 启动；取消最多等 3 秒后只 Kill 记录的 direct PID，不杀进程树，不 `server stop`。
- `IRpcRequestConnection` 与 `IRpcSubscriptionConnection` 各一个 OS 进程。pending map 不用于订阅事件。L2 named-pipe ACL 为 UNVERIFIED。

## 依赖

- 项目引用：Contracts、Core。
- 禁止：PackageReference、WinUI、WebView2、SSH、硬编码用户目录。

## 入口

类库。由 `HerdDesk.App` 组合根构造。测试：`tests/Unit/HerdDesk.Infrastructure.Tests`。

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
