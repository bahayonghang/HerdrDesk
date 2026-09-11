# HD-005 Renderer decision

Status: G0 spike record. Not product AC08/AC09 pass. Native is not promoted.

Date: 2026-09-08. Operator scope: local implementation only. WinUI product UI, live interactive desktop, IME machine tests, npm/WebView2 host, Windows App SDK install, and PackageReference were not granted.

## Selection

**Delivery baseline remains WebView2 + xterm.js** (ADR-002). The native raw-stream candidate is `UNVERIFIED` / not run. It does not replace the default adapter.

Hard gates are correctness and security. A candidate that fails either gate cannot be promoted. Performance, memory, and cold-start numbers cannot override a correctness or security failure.

Rollback: a native fail or an untested native does not rewrite the WinUI shell. Keep `ITerminalRenderer` as a future replacement port. Failed spike views destroy only their own child processes. They do not stop herdr, kill an agent, or close a user pane.

## Environment and versions

| Item | Value | Evidence class |
|---|---|---|
| .NET SDK pin | `10.0.400` floor, `rollForward=latestMinor` (`global.json`) | source |
| Target framework | `net10.0` (`Directory.Build.props`) | source |
| Python | 3.10+ standard library; no third-party packages | source |
| Windows App SDK | not installed | `UNVERIFIED` |
| WebView2 Runtime | not launched | `UNVERIFIED` |
| xterm.js npm lock | not present; no CDN policy still applies | `UNVERIFIED` |
| Native candidate (EWTC / other) | not built, not run | `UNVERIFIED` |
| herdr | v0.8.2 commit `9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c`, `api_protocol=20` | source inspection; no live pane |
| Hosted interactive desktop | not granted | blocked |

## Candidates

### WebView2 / xterm (delivery baseline)

| Gate | Result | Evidence class | Environment | Untested |
|---|---|---|---|---|
| Correctness: UTF-8 cross-chunk, no U+FFFD, no duplication | L1 `PASS` on host specimen; candidate consumption `UNVERIFIED` | synthetic L1 (`Utf8ChunkAssembler`, `tests/fixtures/renderer-cases.json` RD01–RD04) | G0 BCL/Python | real xterm.write / WebView2 |
| Correctness: control-key bytes not text-normalized | L1 `PASS` on host specimen | synthetic L1 (RD05) | G0 BCL/Python | live PTY/agent |
| Correctness: epoch/seq gate | L1 `PASS` | synthetic L1 (RD06–RD07) | G0 BCL/Python | live reconnect |
| Security: web-message allowlist | L1 `PASS` | synthetic L1 (RD16–RD18) | G0 BCL/Python | real WebView2 PostWebMessage |
| Security: observe does not forward UserKey / EmulatorReply | L1 `PASS` | synthetic L1 (RD10–RD11) + `InputPolicy` | G0 BCL/Python | live observe pane |
| Security: local origin, no CDN, no host objects, no generic exec | `UNVERIFIED` | none | no npm/WebView2 host | all |
| Backpressure: bounded queue; parse-consumed ≠ presented; no delta drop | L1 `PASS` classification | synthetic L1 (RD12–RD15) | G0 BCL/Python | live slow xterm |
| IME preedit / commit / candidate window | `UNVERIFIED` / blocked | `evidence/runtime/windows-renderer-ime.blocked.json` | no interactive desktop | all L3 IME |
| Keyboard, selection, DPI 100/150/200%, cross-display | `UNVERIFIED` / blocked | same capture | no interactive desktop | all L3 |
| Accessibility (Narrator, high contrast) | `UNVERIFIED` / blocked | same capture | no interactive desktop | all L3 |
| Resource / maintainability / replaceability | `UNVERIFIED` | source review is not L3 | — | process working set, cold start |

Default remains WebView2/xterm because ADR-002 already selected it, L1 host gates did not fail, and native did not pass the replacement bar.

### Native raw-stream

| Gate | Result | Evidence class | Environment | Untested |
|---|---|---|---|---|
| All hard gates in `docs/plan/docs/04_技术选型与ADR.md` lines 51–53 | `UNVERIFIED` / not run | none | no native host | ANSI without a second ConPTY, IME, Ctrl/Caps/AltGr, paste, resize, mouse, read-only, overlay, accessibility, UTF-8 across frames |
| Promotion | **not promoted** | decision record | — | — |

Native is not a spike failure. It stays a future adapter behind `ITerminalRenderer`.

## Future replacement port

`ITerminalRenderer` is not compiled in G0. Draft shape (`docs/plan/contracts/HerdDesk.Contracts.cs`):

- `BindAsync(PaneKey, ConnectionEpoch)`
- `ApplyAsync(TerminalFrame)` completion = parser consumption, not GPU presentation
- `ReadInputsAsync` yields `RendererInput` with `InputOrigin`
- `SetReadOnlyAsync`
- `FocusAsync`
- `Dispose` destroys only this view

HD-014 may compile a BCL-only port when that task is authorized. This spike does not add WinUI, WebView2, or npm.

## L1 host specimens shipped

Same frame identity (epoch / seq / full / size) is the unit of comparison. Terminal payload is not decoded with per-frame `GetString`.

| Scenario | Code path | Result |
|---|---|---|
| CJK / emoji / combining split | `Utf8ChunkAssembler` | `PASS` |
| Incomplete lead byte holds, no U+FFFD | `Utf8ChunkAssembler` | `PASS` |
| Ctrl+C / Tab / Esc / CSI up raw join | byte concatenation | `PASS` |
| Old epoch rejected; seq not compared across epochs | `RendererEpochGate` | `PASS` |
| Preedit not sent; commit is `CommittedText` | `CompositionPolicy` + `InputPolicy` | `PASS` |
| Observe UserKey denied; EmulatorReply denied | `InputPolicy` | `PASS` |
| Bounded FIFO; oldest-frame ack; stale ack does not release; drop forbidden | `RendererByteWindow` | `PASS` |
| Unknown type / oversize / wrong epoch web message | `WebMessagePolicy` | `PASS` |

Live candidate consumption of the same fixture through WebView2 or native is `UNVERIFIED`.

## L2 / L3 matrix (blocked)

Category: `no_authorized_winui_interactive_desktop_or_ime_grant`.

| Item | Result |
|---|---|
| Microsoft Pinyin preedit not sent; candidate window near caret | blocked / `UNVERIFIED` |
| Space/Enter commit exactly once | blocked / `UNVERIFIED` |
| Ctrl+K / Enter / Esc during composition | blocked / `UNVERIFIED` |
| DPI 100 / 150 / 200% | blocked / `UNVERIFIED` |
| Cross-display drag | blocked / `UNVERIFIED` |
| Narrator / high contrast | blocked / `UNVERIFIED` |
| Native raw-stream host | blocked / `UNVERIFIED` |

HD-005 child AC4 is **not ticked**. Source review and L1 fixtures are not L3 IME evidence.

## Child ACs (HD-005 PRD)

| Child AC | Result | Notes |
|---|---|---|
| AC1 decision matrix | document shipped; live cells `UNVERIFIED` | this file |
| AC2 synthetic chunks on both candidates | L1 host `PASS`; candidate run `UNVERIFIED` | no WebView2/native host |
| AC3 control-key bytes | L1 `PASS` | live renderer `UNVERIFIED` |
| AC4 real IME desktop | blocked / not ticked | missing grant |
| AC5 observe + message allowlist | L1 `PASS` | live observe `UNVERIFIED` |
| AC6 bounded queue / full reset | L1 classification `PASS` | live slow renderer `UNVERIFIED` |
| AC7 native promotion bar | native not promoted | gate unmet / not run |
| AC8 this document | shipped | inputs to HD-014/015 below |

Product AC08 / AC09 / G0 remain `not_run` / `not_passed`.

## Evidence paths

- Decision: `docs/spikes/renderer-decision.md`
- L1 fixture: `tests/fixtures/renderer-cases.json` (`simulation=true`, `runtime_pass=false`)
- Blocked IME/desktop capture: `evidence/runtime/windows-renderer-ime.blocked.json`
- Baseline: `evidence/compatibility-baseline.json` `runtime_verification.ime=blocked`
- C#: `src/HerdDesk.Core/Utf8ChunkAssembler.cs`, `RendererEpochGate.cs`, `CompositionPolicy.cs`, `RendererByteWindow.cs`, `WebMessagePolicy.cs`
- Python: `scripts/herddesk_g0/renderer.py`
- Types: `src/HerdDesk.Contracts/RendererModels.cs`

## Inputs to HD-014 (WebView2 adapter)

1. Keep WebView2/xterm as the delivery adapter. Do not switch the default to native.
2. Bind `PaneKey + ConnectionEpoch`. Reject old-epoch frames, input, resize, and parse acks.
3. Feed xterm with `Uint8Array` / decoder state. Do not call per-frame `GetString` on payload.
4. Versioned allowlist: `frame.apply` (host→web), `parse.consumed`, `input.user_key`, `input.committed_text`, `input.explicit_paste`, `terminal.resize` (web→host). Unknown type, oversize, wrong epoch, wrong pane, `host.exec`, navigation, and downloads are rejects.
5. `ime.preedit` is never transport. `input.emulator_reply` is known and denied by `InputPolicy`.
6. Parse-consumed is not GPU presentation. Ack the oldest queued frame size, not an arbitrary byte count. Over-budget dispatch pauses; dropping a delta to stay Ready is forbidden. Recovery is destroy local baseline and re-observe for a full frame. Same-epoch reset is allowed. A lower epoch is `stale_epoch`.
7. Observe is read-only in renderer **and** host. Do not rely on JS `disabled` alone.
8. Local resources, fixed origin, no CDN. Tests belong in `tests/Unit/HerdDesk.Terminal.Web.Tests` after HD-007/HD-014 create that project. Do not add that csproj in G0.
9. Consume L1 codes from this spike. Do not treat this document as AC08 pass.

## Inputs to HD-015 (IME / keyboard / selection)

1. Preedit updates local composition UI only. Commit is exactly one `CommittedText`.
2. During composition, Ctrl+K / Enter / Esc stay with the IME (`ime_owns_shortcut`).
3. L3 Microsoft Pinyin, DPI, caret-relative candidate window, and Narrator remain `UNVERIFIED` until an authorized interactive desktop exists.
4. Reuse `InputOrigin` and `InputPolicy`. Focus is not `ControlVerified`.
5. Integration.Windows IME scenarios stay with HD-011 shell + HD-015. This spike did not create that project.
6. HD-015 owns product AC08/AC09 roll-up. Do not mark them passed from L1 specimens.

## Promotion / stay / block conditions

- **Stay on WebView2** (current): ADR-002 already selected WebView2/xterm as the delivery baseline. Native has not met the replacement gates (not run). L1 host specimens did not fail. L1 host PASS is not WebView2/xterm candidate consumption and is not product AC08.
- **Promote native** only if a later authorized spike feeds ANSI without a second ConPTY, passes the same L1 codes plus L3 IME/keyboard/read-only/accessibility, and names a maintainer.
- **Keep blocked** for AC09 and HD-005 AC4 until L3 interactive desktop evidence exists. Do not convert this file into a pass. Source review is not L3 IME evidence.
