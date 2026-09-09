# tests

[根索引](../CLAUDE.md) · 测试

生成日期：2026-09-08。三套检查分开计数，不合并覆盖率。

| 套件 | 索引 | 规模 | 运行方式 |
|---|---|---|---|
| Python unittest | [python](python/CLAUDE.md) | 以本次 discover 为准 | `python -m unittest discover -s tests/python -v` |
| C# smoke | [HerdDesk.Core.SmokeTests](HerdDesk.Core.SmokeTests/CLAUDE.md) | 以本次 `dotnet run` 为准 | `dotnet run --project tests/HerdDesk.Core.SmokeTests` |
| Core unit | [Unit/HerdDesk.Core.Tests](Unit/HerdDesk.Core.Tests/CLAUDE.md) | BCL runner | `dotnet run --project tests/Unit/HerdDesk.Core.Tests` |
| Infrastructure unit | [Unit/HerdDesk.Infrastructure.Tests](Unit/HerdDesk.Infrastructure.Tests/CLAUDE.md) | BCL runner | `dotnet run --project tests/Unit/HerdDesk.Infrastructure.Tests` |
| App unit | [Unit/HerdDesk.App.Tests](Unit/HerdDesk.App.Tests/CLAUDE.md) | BCL runner | `dotnet run --project tests/Unit/HerdDesk.App.Tests` |
| Terminal.Web unit | [Unit/HerdDesk.Terminal.Web.Tests](Unit/HerdDesk.Terminal.Web.Tests/CLAUDE.md) | BCL runner | `dotnet run --project tests/Unit/HerdDesk.Terminal.Web.Tests` |
| Contract | [Contract](Contract/CLAUDE.md) | BCL runner | `dotnet run --project tests/Contract/HerdDesk.ContractTests` |
| Rust bridge | [../bridge/CLAUDE.md](../bridge/CLAUDE.md) | cargo | `cargo test --manifest-path bridge/Cargo.toml --workspace --locked` |
| Rust filebridge | [../filebridge/CLAUDE.md](../filebridge/CLAUDE.md) | cargo | `cargo test --manifest-path filebridge/Cargo.toml --workspace --locked` |
| 合成 fixture | [fixtures](fixtures/CLAUDE.md) | NDJSON/JSON | `check_capture.py`、probe `selftest` |

probe `selftest` 合成检查计数以本次运行为准，入口在 [../scripts](../scripts/CLAUDE.md)。

规划层级 L0–L4 见 `docs/plan/docs/10_测试与验收.md`。当前 CI 只跑 L0/L1 离线部分。`tests/fixtures/real-terminal-v082/` 是 HD-004 占位索引，不是 runtime 通过证据。

无 xUnit、无 `dotnet test`、无 pytest。测试框架 NuGet 未准入 lock。HD-011 L1 ViewModel 测试在 `tests/Unit/HerdDesk.App.Tests`。HD-012 Attention reducer 测试在 `tests/Unit/HerdDesk.Core.Tests/Attention`；通知路由测试在 `tests/Unit/HerdDesk.App.Tests/Notifications`。HD-013 L1 fake-child `TerminalCliTransport` 测试在 `tests/Unit/HerdDesk.Infrastructure.Tests` 与 `tests/Contract`；L2 live herdr 与产品 AC05/AC06 仍未通过。HD-014 L1 web-message/flow 测试在 `tests/Unit/HerdDesk.Terminal.Web.Tests`。HD-015 L1 IME/keyboard/selection 测试在同一 Terminal.Web runner 的 `Input/` 与 `tests/Unit/HerdDesk.App.Tests`。HD-016 L1 ControlLeaseCoordinator 测试在 `tests/Unit/HerdDesk.Core.Tests/TerminalLease`；L2 live lease 为 UNVERIFIED。HD-017 L1 ResourceCommandCoordinator 测试在 `tests/Unit/HerdDesk.Core.Tests/Commands`、`tests/Unit/HerdDesk.App.Tests/Commands` 与 `tests/Contract/ResourceCommands`；L2 live mutation 为 UNVERIFIED。HD-018 L1 RecoveryPolicy 测试在 `tests/Unit/HerdDesk.Core.Tests/Recovery` 与 `tests/Unit/HerdDesk.App.Tests/Recovery`；L2 live disconnect 为 UNVERIFIED。HD-019 L1 catalog 在 `evidence/local-mvp/catalog.json`；composition 测试在 `tests/Unit/HerdDesk.Core.Tests/LocalMvp` 与 `tests/Unit/HerdDesk.App.Tests/LocalMvp`；L2 live local MVP 与 L3 IME/TUI 为 UNVERIFIED。HD-020 L1 SSH editor/preview/test 在 `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh`、`tests/Unit/HerdDesk.App.Tests/Devices` 与 `tests/Contract/Hd020Cases.cs`；L2 隔离 OpenSSH 为 UNVERIFIED。HD-023 L1 多设备聚合测试在 `tests/Unit/HerdDesk.Core.Tests/Aggregation` 与 `tests/Unit/HerdDesk.App.Tests/Aggregation`；L2 live 3-device p95 为 UNVERIFIED。HD-025 L1 准入/队列/dirty-set/lease/visibility 测试在 Core/Infrastructure/App unit 与 Contract；L2 live SSH/perf 为 UNVERIFIED。HD-026 L1 收口目录在 `evidence/multi-device-mvp/`；L2 live SSH 为 UNVERIFIED。HD-027 L1 filebridge codec/vectors 在 `filebridge/` 与 `tests/Contract/Files`；无 `main.rs`；L2 FS/SSH/TOCTOU 为 UNVERIFIED。HD-029 L1 文件工作区 ViewModel 测试在 `tests/Unit/HerdDesk.App.Tests/Files`；L2 live UI/SSH 为 UNVERIFIED。HD-030 L1 附件投入测试在 `tests/Unit/HerdDesk.Core.Tests/Attachments` 与 `tests/Unit/HerdDesk.App.Tests/Attachments`；L2 live agent/IME/SSH 为 UNVERIFIED。HD-031 L1 clipboard/cache 测试在 `tests/Unit/HerdDesk.Core.Tests/Clipboard`、`tests/Unit/HerdDesk.Infrastructure.Tests/Clipboard`、`tests/Unit/HerdDesk.Terminal.Web.Tests/Input` 与 `tests/Unit/HerdDesk.App.Tests/Clipboard`；L2 live clipboard/IME 为 UNVERIFIED。HD-032 L1 收口目录在 `evidence/files/`；L2 live FS/SSH/TOCTOU/attack 为 UNVERIFIED。HD-033 L1 收口目录在 `evidence/quality/`；L3 IME/Narrator/DPI 与 L4 soak 为 UNVERIFIED。HD-034 L1 收口目录在 `evidence/packaging/`；L2/L3 live 安装/签名/更新/回滚为 UNVERIFIED。`tests/Integration.Windows` 与 `tests/Integration.Ssh` 未建仓。产品 AC06/AC07/AC10/AC15/AC22/AC23/AC19/AC21/AC13/AC14/AC24/AC26/AC27/AC28/AC29/AC31/AC32/AC33/AC34/AC35/AC37/AC38/AC41/AC42/AC46 仍未通过。
