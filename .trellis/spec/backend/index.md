# Backend Development Guidelines

G0 conventions for the compiled C# libraries and the Python diagnostic protocol. These files describe the code in the tree. They are not a future WinUI, SSH, or database design.

Shared facts: [AGENTS.md](../../../AGENTS.md). Module indexes: nested `CLAUDE.md`. Frontend templates are deferred: [../frontend/index.md](../frontend/index.md).

**Language**: English.

---

## Overview

Current backend surface:

- `src/HerdDesk.Contracts` — BCL-only identity, frame, input, endpoint, lease, configuration, diagnostic, adapter-capability, and HD-009 projection types.
- `src/HerdDesk.Core` — one-epoch `TerminalFrameParser`, `InputPolicy`, `EndpointResolver`, `TerminalLeaseProbe`, renderer L1 specimens, in-memory projection Store, HD-010 `DeviceSession` actor, HD-012 attention reducer, HD-016 L1 `ControlLeaseCoordinator`, HD-017 L1 `ResourceCommandCoordinator`, HD-018 L1 `RecoveryPolicy`, HD-023 L1 multi-device aggregation, and HD-024 L1 per-SessionKey backoff/auth block.
- `src/HerdDesk.Infrastructure` — `AppDataPaths`, atomic device-profile store, JSONL diagnostic sink, owned child process, RPC stdio, SchemaV1 decoder, HD-013 `TerminalCliTransport`, HD-020 L1 OpenSSH preview/test/trust, HD-021 L1 helper publish state machine, HD-022 L1 `RemoteSessionTransportSet`, HD-024 L1 SSH failure classifier and recovery block store.
- `bridge/herddesk-bridge` — stdio ↔ local-socket byte relay. L2 named-pipe ACL UNVERIFIED.
- `src/HerdDesk.Terminal.Web` — HD-014 L1 host↔web validator and BCL renderer adapter plus HD-015 L1 IME/keyboard/selection coordinators; WebView2/xterm packages `UNVERIFIED`.
- `src/HerdDesk.App` — unique composition root / console host stub plus HD-011 L1 ViewModels, HD-012 L1 NotificationCenter, HD-015 L1 focus/input ViewModels, HD-016 L1 control ViewModel, HD-017 L1 resource command ViewModel, HD-018 L1 RecoveryBindings, HD-020 L1 EditDevice ViewModel, HD-021 L1 HelperInstall ViewModel, HD-023 L1 multi-device navigation/search ViewModels, and HD-024 L1 DeviceConnectionStatus ViewModel. WinUI XAML is not admitted. Windows toast L2, IME L3, HD-016 L2 live lease, HD-017 L2 live mutation, HD-018 L2 live disconnect, HD-019 L2 live local MVP / L3 IME-TUI, HD-020 L2 isolated OpenSSH, HD-021 L2 live helper deploy, HD-023 L2 live 3-device p95, and HD-024 L2 live auth are UNVERIFIED. HD-019 catalog lives in `evidence/local-mvp/catalog.json`.
- `scripts/herddesk_g0` — Python strict JSON, frame/input checks, NDJSON, capture validator, endpoint matrix, lease matrix, renderer L1 matrix, ADR baseline, project graph.
- `scripts/probe_herdr.py` — default-readonly probe; writes need a disposable target.
- `tests/HerdDesk.Core.SmokeTests` — `dotnet run`, not `dotnet test`.
- `tests/Unit` and `tests/Contract` — BCL console runners.
- `tests/python` — stdlib `unittest`.

There is no HTTP API, no ORM, and no WinUI window in `src/`.

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | C# and Python layout | Filled from G0 + HD-007 BCL skeleton |
| [Database Guidelines](./database-guidelines.md) | No database | N/A |
| [Error Handling](./error-handling.md) | Fail-closed parser, redaction, unpaired surrogates | Filled from G0 tree |
| [Quality Guidelines](./quality-guidelines.md) | SDK pin, no PackageReference, `just ci` | Filled from G0 tree |
| [Logging Guidelines](./logging-guidelines.md) | Stable codes; no log framework | Filled from G0 tree |

---

## Pre-Development Checklist

- [ ] Read [AGENTS.md](../../../AGENTS.md). Phase is G0. Offline gate is `just ci`. No live herdr/SSH/WinUI writes. Implement only after plan approval.
- [ ] Read the nested `CLAUDE.md` for the directory you will edit.
- [ ] Keep Contracts → Core. Do not add WinUI, WebView2, SSH, or OS credential references to Core.
- [ ] Keep JSON RPC off the terminal stdio plane.
- [ ] Do not overwrite `src/HerdDesk.Contracts` with `docs/plan/contracts/HerdDesk.Contracts.cs`.
- [ ] Do not add a database, ORM, or React/WinUI UI from the frontend template files.

---

## Quality Check

- [ ] `just ci` exit 0 on the changed tree, or the named subset the task allows.
- [ ] Protocol failures use a stable redacted code. Unpaired UTF-16 surrogates at JSON string or name materialization become `malformed_terminal_record` in C# and latch the parser.
- [ ] After a parse failure, the same C# parser instance rejects a later valid frame with `terminal_stream_not_active`.
- [ ] Default `just setup` / `Invoke-HerdDeskDotnetSetup.ps1` does not call winget and does not write User environment.
- [ ] Diagnostics omit terminal payload, credentials, and private paths.
- [ ] Do not mark G0 or AC01–AC48 passed.
- [ ] `herddesk_g0.licensing` keeps `ac02_passed` and `windows_verified` false. Pending or blocked is not approved. Public visibility is not a license grant.
- [ ] `herddesk_g0.endpoint` keeps `ac03_passed` and `windows_verified` false. Synthetic endpoint fixtures are not Windows runtime proof.
- [ ] `herddesk_g0.evidence` keeps `windows_verified` false. Protocol 22 runtime cannot be marked compatible with source protocol 20. Preview stays out of `compatible_by_default`.
- [ ] `herddesk_g0.lease` keeps `ac05_passed` and `windows_verified` false. Synthetic lease fixtures, probe selftest, and isolated observe/control captures with `control_verified=false` are not AC05 pass.
- [ ] `herddesk_g0.renderer` keeps `ac08_passed`, `ac09_passed`, and `windows_verified` false. Synthetic renderer fixtures are not WinUI, WebView2, IME, or native runtime proof.
- [ ] `herddesk_g0.adr` keeps `ac44_passed`, `g0_passed`, and `windows_verified` false. Blocked or unknown ledger rows are not passed. R5 is not executed.
