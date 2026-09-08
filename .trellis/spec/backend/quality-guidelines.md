# Quality Guidelines

G0 quality bar for C#, Python, and the offline gate.

---

## Overview

Target framework `net10.0`. SDK pin is `global.json` only (`10.0.400`, `rollForward=disable`). `Directory.Build.props` sets nullable, `TreatWarningsAsErrors`, deterministic build, and .NET analyzers. Python 3.10+ with the standard library only.

The offline gate is `just ci`. That gate is not G0 product acceptance.

---

## Forbidden Patterns

- `PackageReference` in C# projects until a unit is license-admitted and a real restore lock is committed. `NuGet.Config` keeps package sources empty. Do not invent Windows App SDK versions.
- Core referencing WinUI, WebView2, SSH, OS credentials, or process control.
- Overwriting `src/HerdDesk.Contracts` with `docs/plan/contracts/HerdDesk.Contracts.cs`.
- Per-frame `Encoding.UTF8.GetString` on terminal payload bytes.
- Sending JSON RPC on herdr binary client-socket / terminal stdio.
- Default setup calling winget or writing User `DOTNET_ROOT` / `DOTNET_MULTILEVEL_LOOKUP`. A second hardcoded SDK version besides `global.json` is not an authority.
- Live herdr, SSH, WinUI, takeover, input replay, or `publish_github.py --publish` in this G0 gate.
- Inventing a database or a React/WinUI UI from Trellis frontend templates.
- Marking G0 or AC01–AC48 passed without product evidence.

---

## Required Patterns

- Contracts depend on BCL only. Core depends on Contracts only.
- Frame parse is one connection epoch. Reconnect uses `new TerminalFrameParser()` / a new `TerminalCaptureValidator`.
- Canonical Base64: decode then `Convert.ToBase64String` / `b64encode` and compare to the wire string.
- Duplicate JSON keys are errors (`CheckDuplicateKeys` / `object_pairs_hook`).
- JSON depth 64. Limits: NDJSON line 16MiB, decoded frame 8MiB, input 64KiB. Over limit stops the connection.
- Unpaired surrogates at JSON string/name materialization: C# `malformed_terminal_record` plus failed latch (see [error-handling.md](./error-handling.md)).
- `just setup` / `Invoke-HerdDeskDotnetSetup.ps1` default path is read-only. Read `global.json` first. `-InstallPinnedSdk` and `-PersistUserEnvironment` are opt-in after that read.
- Metadata JSON/Markdown reads use UTF-8. Fixtures stay LF.

---

## Testing Requirements

| Suite | How to run | Scope |
|---|---|---|
| Python | `python -m unittest discover -s tests/python -v` | Protocol, probe gates, setup stubs, repository, evidence, licensing, endpoint, lease, renderer, ADR baseline, publish synthetic |
| Probe selftest | `python scripts/probe_herdr.py selftest` | Synthetic; `herdr_executed=false` |
| Capture | `python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson` | Offline NDJSON |
| Structure | `python scripts/validate_repository.py` | Layout, no PackageReference, no `Directory.Packages.props`, no `packages.lock.json`, no unverified `2.4.0` pin in csproj, UTF-8, `herddesk_g0.project_graph`; calls evidence/endpoint/lease/renderer/licensing/adr; `github_required_check` stays `UNVERIFIED`; `windows_verified` and AC/G0 flags stay false |
| C# smoke | `dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build` | Parser, `InputPolicy`, `EndpointResolver`, `TerminalLeaseProbe`, and renderer L1 specimens; not `dotnet test` |
| Core unit | `dotnet run --project tests/Unit/HerdDesk.Core.Tests --configuration Release --no-build` | Fake-port Core checks including HD-009 mapper/Store, HD-010 DeviceSession L1 race, and HD-012 Attention reducer; BCL runner |
| Infrastructure unit | `dotnet run --project tests/Unit/HerdDesk.Infrastructure.Tests --configuration Release --no-build` | Config atomic write/backup/restore; diagnostic privacy; HD-013 fake-child `TerminalCliTransport` |
| App unit | `dotnet run --project tests/Unit/HerdDesk.App.Tests --configuration Release --no-build` | HD-011 L1 shell/navigation/search/settings/diagnostics ViewModels; HD-012 L1 notification routing; fake Store; no WinUI |
| Terminal.Web unit | `dotnet run --project tests/Unit/HerdDesk.Terminal.Web.Tests --configuration Release --no-build` | HD-014 L1 message allowlist, flow controller, epoch reject, observe no-resize; Uint8Array-equivalent bytes; no WebView2 |
| Contract | `dotnet run --project tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --no-build` | Assembly graph, production composition, fake-bridge RPC, SchemaV1 decoder, HD-011 residuals, HD-014 residuals |
| Rust bridge | `cargo fmt/clippy/test --manifest-path bridge/Cargo.toml --locked` | Byte relay, mapping, Unix half-close; L2 UNVERIFIED |

`just ci` runs the full offline set. Counts in `implementation/status.json` may lag; use the current command output.

Evidence tests in `tests/python/test_evidence.py` must call `herddesk_g0.evidence` and load the real files under `evidence/`. Git blob SHA, distribution binary SHA-256, and runtime schema SHA-256 stay in distinct fields. Source inspection, synthetic fixtures, and hosted CI are not runtime proof. A protocol 22 runtime capture cannot be marked compatible with source protocol 20. Preview stays out of `compatible_by_default`. Structural validation is not product acceptance.

Licensing tests in `tests/python/test_licensing.py` must call `herddesk_g0.licensing` and load the real files under `docs/licensing/`. A pending or blocked candidate cannot be treated as approved. Public visibility is not a license grant. Structural validation does not pass AC02.

Endpoint tests in `tests/python/test_endpoint.py` must call `herddesk_g0.endpoint` and load `tests/fixtures/endpoint-cases.json`. Default must not guess `%APPDATA%` or conventional pipe names. Named must not fall back to default. UNC must be rejected. PaneKey is not an endpoint key. A mapping for another DeviceId is ignored. A synthetic fixture is not Windows runtime proof and does not pass AC03.

Lease tests in `tests/python/test_lease.py` must call `herddesk_g0.lease` and load `tests/fixtures/lease-cases.json`. ControlVerified must not be set from first frame, process alive, window focus, or a stdin write on the isolated capture. Observe must not send input. EOF must not be classified as pane exit. A fictional Granted event must not grant. A synthetic fixture is not observe/control runtime proof and does not pass AC05. Isolated Windows observe/control evidence does not pass AC05.

Renderer tests in `tests/python/test_renderer.py` must call `herddesk_g0.renderer` and load `tests/fixtures/renderer-cases.json`. UTF-8 splits must not emit U+FFFD or duplicate text. Old epochs must be rejected. Seq must not be compared across epochs. Preedit must not be sent. Observe must not forward UserKey. EmulatorReply must be denied. The queue must be a bounded FIFO; ack must match the oldest frame; a lower-epoch reset is stale; the queue must not drop deltas to stay Ready. Parse-consumed is not presentation. Unknown, oversize, and wrong-epoch web messages must be rejected. A synthetic fixture is not WinUI/IME proof and does not pass AC08 or AC09.

ADR tests in `tests/python/test_adr.py` must call `herddesk_g0.adr` and load `docs/adr/approved-baseline.json` plus the markdown freeze. A blocked or unknown ledger row cannot be recorded as passed. G0 and AC44 cannot be claimed passed. R5 phase-rule sync stays not executed. Cited `input_evidence` and gate attachments must exist. `AGENTS.md` G0 prohibitions stay while R5 is not executed. Structural validation does not pass AC44.

Protocol regressions for unpaired surrogates must assert the exact C# code `malformed_terminal_record`, then `terminal_stream_not_active` on the next valid frame, and must keep a valid surrogate pair accepted. Python uses the same fixture names.

Setup tests in `tests/python/test_setup.py` stub `Install-PinnedSdk` and `Set-UserDotnetEnvironment`. They must not install a real SDK or write the real User environment.

---

## Code Review Checklist

- [ ] File list matches the approved task. No extra product modules.
- [ ] Errors redacted; fail latch held.
- [ ] `ControlVerified` not set from frame, process, or focus.
- [ ] No live herdr/SSH/WinUI in the change.
- [ ] `just ci` or the named subset passed.
- [ ] Nested `CLAUDE.md` still matches the code you edited.
