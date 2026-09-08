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
| 合成 fixture | [fixtures](fixtures/CLAUDE.md) | NDJSON/JSON | `check_capture.py`、probe `selftest` |

probe `selftest` 合成检查计数以本次运行为准，入口在 [../scripts](../scripts/CLAUDE.md)。

规划层级 L0–L4 见 `docs/plan/docs/10_测试与验收.md`。当前 CI 只跑 L0/L1 离线部分。`tests/fixtures/real-terminal-v082/` 是 HD-004 占位索引，不是 runtime 通过证据。

无 xUnit、无 `dotnet test`、无 pytest。测试框架 NuGet 未准入 lock。HD-011 L1 ViewModel 测试在 `tests/Unit/HerdDesk.App.Tests`。HD-012 Attention reducer 测试在 `tests/Unit/HerdDesk.Core.Tests/Attention`；通知路由测试在 `tests/Unit/HerdDesk.App.Tests/Notifications`。HD-013 L1 fake-child `TerminalCliTransport` 测试在 `tests/Unit/HerdDesk.Infrastructure.Tests` 与 `tests/Contract`；L2 live herdr 与产品 AC05/AC06 仍未通过。HD-014 L1 web-message/flow 测试在 `tests/Unit/HerdDesk.Terminal.Web.Tests`。HD-015 L1 IME/keyboard/selection 测试在同一 Terminal.Web runner 的 `Input/` 与 `tests/Unit/HerdDesk.App.Tests`。HD-016 L1 ControlLeaseCoordinator 测试在 `tests/Unit/HerdDesk.Core.Tests/TerminalLease`；L2 live lease 为 UNVERIFIED。HD-017 L1 ResourceCommandCoordinator 测试在 `tests/Unit/HerdDesk.Core.Tests/Commands`、`tests/Unit/HerdDesk.App.Tests/Commands` 与 `tests/Contract/ResourceCommands`；L2 live mutation 为 UNVERIFIED。HD-018 L1 RecoveryPolicy 测试在 `tests/Unit/HerdDesk.Core.Tests/Recovery` 与 `tests/Unit/HerdDesk.App.Tests/Recovery`；L2 live disconnect 为 UNVERIFIED。`tests/Integration.Windows` 未建仓：WinUI/WebView2 未准入，L2/L3 与 Windows toast / 真机 IME 为 UNVERIFIED。
