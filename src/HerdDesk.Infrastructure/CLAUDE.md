# HerdDesk.Infrastructure

[根索引](../../CLAUDE.md) · [src](../CLAUDE.md) · Infrastructure

HD-007 BCL adapters: configuration store and diagnostics. No WinUI, SSH, or herdr process control.

## 职责

- 把注入的 `AppDataPaths` 根目录映射到 settings/cache/logs。
- 原子写入 `device-profiles.json`，覆盖时写一份 `.bak`，显式恢复只读该备份。失败注入钩子是 `internal`（`InternalsVisibleTo` 测试程序集）。
- 受限 `DiagnosticEvent` 写入有界 JSONL（UTF-8 无 BOM）；ID 只出现 alias。写失败只增加 dropped，不使进程崩溃。

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
