# Backend Development Guidelines

G0 conventions for the compiled C# libraries and the Python diagnostic protocol. These files describe the code in the tree. They are not a future WinUI, SSH, or database design.

Shared facts: [AGENTS.md](../../../AGENTS.md). Module indexes: nested `CLAUDE.md`. Frontend templates are deferred: [../frontend/index.md](../frontend/index.md).

**Language**: English.

---

## Overview

Current backend surface:

- `src/HerdDesk.Contracts` — BCL-only identity, frame, input, endpoint, and lease types.
- `src/HerdDesk.Core` — one-epoch `TerminalFrameParser`, `InputPolicy`, `EndpointResolver`, `TerminalLeaseProbe`, and renderer L1 specimens.
- `scripts/herddesk_g0` — Python strict JSON, frame/input checks, NDJSON, capture validator, endpoint matrix, lease matrix, renderer L1 matrix, ADR baseline.
- `scripts/probe_herdr.py` — default-readonly probe; writes need a disposable target.
- `tests/HerdDesk.Core.SmokeTests` — `dotnet run`, not `dotnet test`.
- `tests/python` — stdlib `unittest`.

There is no HTTP API, no ORM, and no application host in `src/`.

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | C# and Python layout | Filled from G0 tree |
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
- [ ] `herddesk_g0.lease` keeps `ac05_passed` and `windows_verified` false. Synthetic lease fixtures and probe selftest are not observe/control runtime proof.
- [ ] `herddesk_g0.renderer` keeps `ac08_passed`, `ac09_passed`, and `windows_verified` false. Synthetic renderer fixtures are not WinUI, WebView2, IME, or native runtime proof.
- [ ] `herddesk_g0.adr` keeps `ac44_passed`, `g0_passed`, and `windows_verified` false. Blocked or unknown ledger rows are not passed. R5 is not executed.
