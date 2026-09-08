# Quality Guidelines

G0 quality bar for C#, Python, and the offline gate.

---

## Overview

Target framework `net10.0`. SDK pin is `global.json` only (`10.0.400`, `rollForward=disable`). `Directory.Build.props` sets nullable, `TreatWarningsAsErrors`, deterministic build, and .NET analyzers. Python 3.10+ with the standard library only.

The offline gate is `just ci`. That gate is not G0 product acceptance.

---

## Forbidden Patterns

- `PackageReference` in G0 C# projects. `NuGet.Config` keeps package sources empty.
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
| Python | `python -m unittest discover -s tests/python -v` | Protocol, probe gates, setup stubs, repository, evidence, licensing, publish synthetic |
| Probe selftest | `python scripts/probe_herdr.py selftest` | Synthetic; `herdr_executed=false` |
| Capture | `python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson` | Offline NDJSON |
| Structure | `python scripts/validate_repository.py` | Layout, no PackageReference, UTF-8; calls `herddesk_g0.evidence`, `herddesk_g0.endpoint`, and `herddesk_g0.licensing`; `windows_verified`, `ac02_passed`, and `ac03_passed` stay false |
| C# smoke | `dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build` | Parser, `InputPolicy`, and `EndpointResolver`; not `dotnet test` |

`just ci` runs the full offline set. Counts in `implementation/status.json` may lag; use the current command output.

Evidence tests in `tests/python/test_evidence.py` must call `herddesk_g0.evidence` and load the real files under `evidence/`. Git blob SHA, distribution binary SHA-256, and runtime schema SHA-256 stay in distinct fields. Source inspection, synthetic fixtures, and hosted CI are not runtime proof. Structural validation is not product acceptance.

Licensing tests in `tests/python/test_licensing.py` must call `herddesk_g0.licensing` and load the real files under `docs/licensing/`. A pending or blocked candidate cannot be treated as approved. Public visibility is not a license grant. Structural validation does not pass AC02.

Endpoint tests in `tests/python/test_endpoint.py` must call `herddesk_g0.endpoint` and load `tests/fixtures/endpoint-cases.json`. Default must not guess `%APPDATA%` or conventional pipe names. Named must not fall back to default. UNC must be rejected. PaneKey is not an endpoint key. A mapping for another DeviceId is ignored. A synthetic fixture is not Windows runtime proof and does not pass AC03.

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
