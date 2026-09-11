# Quality Guidelines

G0 quality bar for C#, Python, and the offline gate.

---

## Overview

Target framework `net10.0`. SDK pin is `global.json` only (`10.0.400` floor, `rollForward=latestMinor`). Newer 10.x SDKs satisfy the floor. `Directory.Build.props` sets nullable, `TreatWarningsAsErrors`, deterministic build, and .NET analyzers. Python 3.10+ with the standard library only.

The offline gate is `just ci`. That gate is not G0 product acceptance.

---

## Forbidden Patterns

- `PackageReference` outside the admitted App windows TFM lock. `NuGet.Config` maps nuget.org to `Microsoft.WindowsAppSDK.*`, `Microsoft.Web.WebView2`, `Microsoft.Windows.SDK.BuildTools`, and `Microsoft.Windows.SDK.BuildTools.MSIX` only. Do not PackageReference the WASDK 2.4.0 umbrella. Do not invent Windows App SDK versions. npm lock is only `web/terminal/package-lock.json` with `@xterm/xterm` 6.0.0. Do not admit unscoped `xterm` or CDN URLs. `web/terminal/dist/*.js` host scripts must be browser JavaScript (Node `stripTypeScriptTypes`); TypeScript syntax in those files is forbidden.
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
| Structure | `python scripts/validate_repository.py` | Layout; PackageReference only on App windows TFM for admitted `Microsoft.WindowsAppSDK.WinUI` 2.3.6; `Directory.Packages.props` and `src/HerdDesk.App/packages.lock.json` are the App lock; no unverified `2.4.0` umbrella pin in csproj; UTF-8; `herddesk_g0.project_graph`; calls evidence/endpoint/lease/renderer/licensing/adr; HD-019 catalog + l2/l3 residuals stay UNVERIFIED; HD-025 `b_ssh_measured` and L2 live SSH/perf stay false/UNVERIFIED; HD-026 multi-device catalog + L2 live SSH stay not_run/UNVERIFIED; HD-032 file-fault catalog + L2 live FS/SSH/TOCTOU/attack stay not_run/UNVERIFIED; HD-033 performance/a11y/soak catalog + L2 Narrator presence overlay + product-UI Narrator launch record + L2 current-system-DPI overlay + L2 current-system theme overlay + product-UI soak START + soak START interruption + wall-clock elapsed overlay + optional soak-process working-set overlay + scale-only DPI matrix overlay; Narrator overlay is not AC37 and does not start Narrator; product-UI launch is not `live_narrator` success and is not AC37; keyboard chrome retry is not AC37 workflow completion; DPI overlay is not AC38, does not change display scale, and is not a 100/150/200 matrix; theme overlay is not AC38, does not change theme/high-contrast/topology, and is not a light/dark/high-contrast x monitor matrix; a committed `live-dpi-matrix.json` may set `dpi_matrix_100_150_200_executed` true on that file only and is not AC38, not theme/monitor/high-contrast, and not L3; soak START is not `eight_hour_soak_executed` and is not AC46; interrupted STARTs are not 8h; `--record-elapsed` fails closed before 8 wall-clock hours; a committed `live-soak-elapsed.json` may set `eight_hour_soak_executed` true on that file only and is not AC46; `--watch-elapsed` is not CI and does not write elapsed before due; a later START must be a new wall-clock and must not reuse interrupted owned PIDs; a soak-process working-set overlay of the running START App PID is optional until recorded and is not AC29, not AC46, not 1/4 pane, and not 100 open/close; L3 IME/Narrator/DPI and L4 soak stay not_run/UNVERIFIED; HD-034 packaging catalog + L2 unsigned layout Verify-on-fixture + lab cert overlay + optional lab MakeAppx/SignTool overlay; overlay is not AC41; L2/L3 live install/sign/update/rollback stay not_run/UNVERIFIED; HD-035 security/license catalog + L2 admitted-input auditor overlay; L2/L3 live scans/renderer/canary/signed-package reverse-audit stay not_run/UNVERIFIED; HD-036 release-docs catalog + L2 hosted-workflow pointer overlay; hosted workflow is not required-check; L2/L3/L4 live walkthrough/matrix/SHA-bind/restore/required-check/graph-rerun/publish/signed-hash stay not_run/UNVERIFIED; parser consumed is not presentation; do not derive process memory from `Q_p`; `github_required_check` stays `UNVERIFIED`; `windows_desktop_restore=admitted` is HD-007 App lock only and is not AC39; `windows_verified` and AC/G0 flags stay false |
| C# smoke | `dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build` | Parser, `InputPolicy`, `EndpointResolver`, `TerminalLeaseProbe`, and renderer L1 specimens; not `dotnet test` |
| Core unit | `dotnet run --project tests/Unit/HerdDesk.Core.Tests --configuration Release --no-build` | Fake-port Core checks including HD-009 mapper/Store, HD-010 DeviceSession L1 race, HD-012 Attention reducer, HD-016 ControlLeaseCoordinator, HD-017 ResourceCommandCoordinator, HD-018 RecoveryPolicy/DeviceSession/lease recovery, HD-019 L1 composition, HD-023 L1 aggregation, HD-025 L1 admission/queue/dirty-set, HD-028 L1 TransferCoordinator, HD-030 L1 AttachmentCoordinator, and HD-031 L1 clipboard intent/paste; BCL runner |
| Infrastructure unit | `dotnet run --project tests/Unit/HerdDesk.Infrastructure.Tests --configuration Release --no-build` | Config atomic write/backup/restore; diagnostic privacy; HD-013 fake-child `TerminalCliTransport`; HD-020 L1 fake ssh -G / trust / staged test; HD-021 L1 fake ssh -T helper planner/publisher; HD-022 L1 fake ssh -T remote session transport; HD-025 L1 `SshConnectionLease`; HD-028 L1 `LocalFileEndpoint`; HD-031 L1 `AttachmentCache` / fake clipboard snapshot |
| App unit | `dotnet run --project tests/Unit/HerdDesk.App.Tests --configuration Release --no-build` | HD-011 L1 shell/navigation/search/settings/diagnostics ViewModels; HD-012 L1 notification routing; HD-015 L1 terminal input/focus ViewModels; HD-016 L1 control ViewModel; HD-017 L1 resource command ViewModel; HD-018 L1 RecoveryBindings / kill ledger; HD-019 L1 Shell/Terminal composition; HD-020 L1 EditDevice ViewModel; HD-021 L1 HelperInstall ViewModel; HD-023 L1 multi-device navigation/search; HD-025 L1 PaneVisibilityCoordinator; HD-029 L1 dual-pane file workspace ViewModels; HD-030 L1 AttachToAgentViewModel; HD-031 L1 PastePreviewViewModel; fake Store / fake file endpoints; no WinUI |
| Integration.Windows | `dotnet run --project tests/Integration.Windows --configuration Release --no-build` | HD-011 L2 XAML name/automation parse plus BCL shell/activation/search cases; net10.0; no PackageReference; does not start a WinUI window |
| Terminal.Web unit | `dotnet run --project tests/Unit/HerdDesk.Terminal.Web.Tests --configuration Release --no-build` | HD-014 L1 message allowlist, flow controller, epoch reject, observe no-resize; HD-015 L1 IME/keyboard/selection coordinators; HD-031 L1 OSC 52 deny; Uint8Array-equivalent bytes; no WebView2 |
| Contract | `dotnet run --project tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --no-build` | Assembly graph, production composition, fake-bridge RPC, SchemaV1 decoder, HD-011 residuals, HD-014 residuals, HD-015 residuals, HD-016 residuals, HD-017 residuals, HD-018 residuals, HD-019 residuals, HD-020 residuals, HD-021 residuals, HD-022 residuals, HD-023 residuals, HD-024 residuals, HD-025 residuals, HD-026 residuals, HD-027 golden vectors and residuals, HD-028 FileBridgeClient / residuals, HD-029 file workspace residuals, HD-030 attachment residuals, HD-031 clipboard residuals, HD-032 file-fault catalog residuals, HD-033 performance/a11y/soak catalog residuals, HD-034 packaging catalog residuals, HD-035 security/license catalog residuals, HD-036 release-docs catalog residuals |
| Rust bridge | `cargo fmt/clippy/test --manifest-path bridge/Cargo.toml --locked` | Byte relay, mapping, Unix half-close; L2 UNVERIFIED |
| Rust filebridge | `cargo fmt/clippy/test --manifest-path filebridge/Cargo.toml --locked` | HD-027 codec + HD-028 L1 serve / local FS; L2 FS/SSH/TOCTOU UNVERIFIED |

`just ci` runs the full offline set. Counts in `implementation/status.json` may lag; use the current command output.

Evidence tests in `tests/python/test_evidence.py` must call `herddesk_g0.evidence` and load the real files under `evidence/`. Git blob SHA, distribution binary SHA-256, and runtime schema SHA-256 stay in distinct fields. Source inspection, synthetic fixtures, and hosted CI are not runtime proof. A protocol 22 runtime capture cannot be marked compatible with source protocol 20. Preview stays out of `compatible_by_default`. Structural validation is not product acceptance.

Licensing tests in `tests/python/test_licensing.py` must call `herddesk_g0.licensing` and load the real files under `docs/licensing/`. A pending or blocked candidate cannot be treated as approved. Public visibility is not a license grant. Structural validation does not pass AC02.

Endpoint tests in `tests/python/test_endpoint.py` must call `herddesk_g0.endpoint` and load `tests/fixtures/endpoint-cases.json`. Default must not guess `%APPDATA%` or conventional pipe names. Named must not fall back to default. UNC must be rejected. PaneKey is not an endpoint key. A mapping for another DeviceId is ignored. A synthetic fixture is not Windows runtime proof and does not pass AC03.

Lease tests in `tests/python/test_lease.py` must call `herddesk_g0.lease` and load `tests/fixtures/lease-cases.json`. ControlVerified must not be set from first frame, process alive, window focus, or a stdin write on the isolated capture. Observe must not send input. EOF must not be classified as pane exit. A fictional Granted event must not grant. A synthetic fixture is not observe/control runtime proof and does not pass AC05. Isolated Windows observe/control evidence does not pass AC05.

Renderer tests in `tests/python/test_renderer.py` must call `herddesk_g0.renderer` and load `tests/fixtures/renderer-cases.json`. UTF-8 splits must not emit U+FFFD or duplicate text. Old epochs must be rejected. Seq must not be compared across epochs. Preedit must not be sent. Observe must not forward UserKey. EmulatorReply must be denied. The queue must be a bounded FIFO; ack must match the oldest frame; a lower-epoch reset is stale; the queue must not drop deltas to stay Ready. Parse-consumed is not presentation. Unknown, oversize, and wrong-epoch web messages must be rejected. A synthetic fixture is not WinUI/IME proof and does not pass AC08 or AC09.

ADR tests in `tests/python/test_adr.py` must call `herddesk_g0.adr` and load `docs/adr/approved-baseline.json` plus the markdown freeze. A blocked or unknown ledger row cannot be recorded as passed. G0 and AC44 cannot be claimed passed. R5 phase-rule sync stays not executed. Cited `input_evidence` and gate attachments must exist. `AGENTS.md` G0 prohibitions stay while R5 is not executed. Structural validation does not pass AC44.

Release tests in `tests/python/test_hd036.py` must call `herddesk_g0.release.bind_release_candidate` and the CLI `scripts/bind_release_candidate.py`. A hosted workflow run is not a required-check ruleset. `github_required_check` stays UNVERIFIED. `published` and `complete_1_0_claimed` stay false. A git HEAD mismatch with the bound SHA is reported and does not pass AC40. A matching HEAD also does not pass AC40. Structural validation does not pass AC39/AC40/AC45/AC47/AC48.

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
