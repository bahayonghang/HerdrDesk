# HD-006 · Approved G0 security baseline

Status: adopted as policy. Not G0 pass. Not AC44 pass.

Date: 2026-09-08. Operator scope: local implementation. Live herdr, named-pipe ACL, terminal lease, WinUI, and IME were not granted.

G0 is not passed.
AC44 is not passed.
R5 phase-rule sync is not executed.
Unknown remains Unknown.
A blocked capability stays observe or explicit configuration.

This freeze keeps ADR-001 through ADR-007 from docs/plan/docs/04_技术选型与ADR.md. It does not create a parallel ADR series.

ADR-0001 (`docs/adr/0001-g0-bootstrap.md`) is a repository-organization ADR. That file is not plan ADR-001.

Machine ledger: [approved-baseline.json](approved-baseline.json). Validator: `scripts/herddesk_g0/adr.py`. `ac44_passed` stays false. `phase_gate` stays `not_passed`.

Child AC44-C1 and AC44-C2 are contributions in this freeze. AC44 final stays `not_run` for HD-035.

## Evidence classes

| Class | Meaning | May mark G0/AC44 passed |
|---|---|---|
| `source_inspection_only` | Read source, register, or plan text | no |
| `synthetic` | L1 fixture or mapper specimen | no |
| `hosted_ci` | Offline `just ci` / Actions on a SHA | no |
| `blocked` | Authorized local work; live grant missing | no |
| `not_run` | Independent capture not started | no |
| `isolated_windows_runtime` | Authorized live Windows capture | not present |

A blocked or unknown gate with result `passed` / `verified` / `success` is a validator failure.

## ADR-001 Independent implementation; do not fork the macOS App

| Field | Value |
|---|---|
| Problem | A Windows client needs herdr protocol behaviour without copying herdrm assets or Swift sources. |
| Input evidence | `docs/licensing-register.md`, `docs/licensing/register.json`, `LICENSE-STATUS.md` (HD-002) |
| Decision | Keep the herdr protocol and product behaviour. Reimplement the Windows application. Do not copy herdrm icons, layout resources, or Swift files. |
| Evidence class | `source_inspection_only` |
| Allow callers | `herddesk_first_party_source`, `herdr_protocol_reference` |
| Deny callers | `herdrm_source`, `herdrm_icons_fonts_screenshots_layout`, `unapproved_third_party_copy` |
| Fail state | `keep_independent_substitute_or_drop_feature` |
| Rollback | Remove unapproved copies. Do not stop herdr or an agent. |
| Owner module | `docs/licensing`; future HD-007/034/035 |
| Verification task | HD-035 |
| Spec-update trigger | An approved unit enters package lock, WebView bundle, MSIX, or release manifest. |
| Adoption | adopted as policy. `runtime_verified=false` |

## ADR-002 Keep the native shell; terminal uses a replaceable renderer

| Field | Value |
|---|---|
| Problem | The terminal view needs a delivery adapter. Native raw-stream has not met the replacement bar. |
| Input evidence | `docs/spikes/renderer-decision.md`, `tests/fixtures/renderer-cases.json`, `evidence/runtime/windows-renderer-ime.blocked.json` (HD-005) |
| Decision | WebView2 plus xterm.js is the delivery baseline. EWTC/native stays an independent spike. Native is UNVERIFIED and is not promoted. |
| Evidence class | `synthetic` |
| Allow callers | `webview2_xterm_delivery_baseline`, `ITerminalRenderer_replacement_port` |
| Deny callers | `native_promotion_without_gate`, `cdn_renderer_assets`, `generic_host_exec` |
| Fail state | `stay_on_webview2_xterm_baseline` |
| Rollback | Keep `ITerminalRenderer`. Destroy only the failed view child processes. Do not stop herdr or an agent. |
| Owner module | `HerdDesk.Core` WebMessagePolicy L1; future `HerdDesk.Terminal.Web` |
| Verification task | HD-014 |
| Spec-update trigger | A WebView2 host or a native adapter is added. |
| Adoption | adopted as policy. `runtime_verified=false` |

## ADR-003 State plane uses RPC; terminal plane stays CLI

| Field | Value |
|---|---|
| Problem | `api schema`/`snapshot` is not a generic RPC proxy. Terminal frames must not share that socket. |
| Input evidence | `docs/plan/docs/03_架构与数据流.md`, `tests/fixtures/endpoint-cases.json`, `evidence/runtime/windows-endpoint-matrix.blocked.json` (HD-003, HD-004) |
| Decision | JSON RPC uses an API socket or a future `herddesk-bridge`. Terminal frames use `herdr terminal session` stdio. Do not send JSON RPC to the herdr binary client socket. |
| Evidence class | `source_inspection_only` |
| Allow callers | `future_herddesk_bridge_api_socket`, `herdr_terminal_session_stdio` |
| Deny callers | `json_rpc_on_terminal_stdio`, `json_rpc_on_herdr_binary_client_socket`, `appdata_pipe_name_guess` |
| Fail state | `explicit_configuration_required` |
| Rollback | Stay observe-only. Do not open a live API or terminal write path. |
| Owner module | `HerdDesk.Core` EndpointResolver; future Infrastructure and bridge |
| Verification task | HD-008 |
| Spec-update trigger | `herddesk-bridge` or a production RPC transport is added. |
| Adoption | adopted as policy. `runtime_verified=false` |

## ADR-004 SSH exec; do not copy the macOS Unix-to-Unix tunnel

| Field | Value |
|---|---|
| Problem | Windows OpenSSH Unix-socket forwarding is unverified. A local unauthenticated TCP API port is out of scope. |
| Input evidence | `docs/plan/docs/07_多设备SSH与文件.md`, `evidence/compatibility-baseline.json` (HD-001, HD-003) |
| Decision | Remote RPC runs the owned bridge. Remote terminal runs the remote herdr CLI. Both use `ssh -T`. Do not allocate a PTY. Do not open a local unauthenticated TCP API port. |
| Evidence class | `source_inspection_only` |
| Allow callers | `ssh_T_remote_bridge`, `ssh_T_remote_herdr_cli` |
| Deny callers | `ssh_tt_pty`, `local_unauthenticated_tcp_api`, `pane_output_in_remote_argv` |
| Fail state | `protocol_stream_polluted_or_auth_failed` |
| Rollback | Disable the SSH path. Keep local observe-only diagnostics. |
| Owner module | future `HerdDesk.Infrastructure` SSH; not in G0 Core |
| Verification task | HD-020 |
| Spec-update trigger | An SSH transport project is added. |
| Adoption | adopted as policy. `runtime_verified=false` |

## ADR-005 Do not own the agent; manage the daemon only by user intent

| Field | Value |
|---|---|
| Problem | Closing the GUI must not stop herdr or an existing agent. |
| Input evidence | `tests/fixtures/lease-cases.json`, `evidence/runtime/windows-terminal-lease.blocked.json`, `scripts/probe_herdr.py` (HD-004) |
| Decision | v0.1 requires the user to start herdr. After a successful connect, if the server disappears, do not auto-revive. Closing the GUI stops only this application's direct child processes. |
| Evidence class | `synthetic` |
| Allow callers | `stop_owned_bridge_child`, `stop_owned_ssh_child` |
| Deny callers | `server_stop`, `kill_daemon`, `kill_agent`, `job_object_including_daemon` |
| Fail state | `leave_daemon_and_agent_running` |
| Rollback | Disable auto-start. Return to manual daemon start. |
| Owner module | `probe_herdr` `stop_owned`; future Infrastructure process spec |
| Verification task | HD-013 |
| Spec-update trigger | A process host or Job Object policy is added. |
| Adoption | adopted as policy. `runtime_verified=false` |

## ADR-006 Least privilege and confirmed side effects

| Field | Value |
|---|---|
| Problem | Control, takeover, input, and upload can write to a live pane. Reconnect must not replay queued bytes. |
| Input evidence | `InputPolicy.cs`, `TerminalLeaseProbe.cs`, lease and renderer fixtures (HD-004, HD-005) |
| Decision | Default observe. Control does not imply takeover. Auto-reconnect returns to observe. Upload, close pane, delete workspace, and skip-agent-approval need an explicit grant. After disconnect, do not replay input. `ControlVerified` is true only after the adapter proves write ownership. |
| Evidence class | `synthetic` |
| Allow callers | `observe_without_grant`, `control_after_adapter_proved`, `takeover_after_confirmation` |
| Deny callers | `control_verified_from_first_frame`, `control_verified_from_process_alive`, `control_verified_from_window_focus`, `input_replay_after_disconnect`, `fictional_terminal_granted` |
| Fail state | `acquiring_or_unknown_no_write` |
| Rollback | Return to observe. Drop the queued input. Do not send release as a substitute for process kill. |
| Owner module | `InputPolicy`, `TerminalLeaseProbe`; future HD-016/018 |
| Verification task | HD-016 |
| Spec-update trigger | A compiled control state machine is added. |
| Adoption | adopted as policy. `runtime_verified=false` |

Identity and epoch for this decision: compare `seq` only inside the current `ConnectionEpoch`. An old-epoch input is `stale_epoch`.

## ADR-007 Modular monolith; no private agent backend

| Field | Value |
|---|---|
| Problem | A second source of truth or a private scheduler would split runtime state from herdr. |
| Input evidence | `src/HerdDesk.Contracts`, `src/HerdDesk.Core`, `docs/plan/docs/03_架构与数据流.md` |
| Decision | App Core plus sidecar. Do not rewrite agent detection. Do not store full terminal history for cloud sync. Do not build a private scheduler. Identity keys are `DeviceId`, `SessionKey`, `PaneKey`, and `ConnectionEpoch`. `seq` compares only inside the current epoch. `herddesk-filebridge` is a narrow on-demand helper. |
| Evidence class | `source_inspection_only` |
| Allow callers | `HerdDesk.Contracts_bcl_types`, `HerdDesk.Core_one_epoch_specimens`, `future_narrow_filebridge_job` |
| Deny callers | `private_agent_backend`, `cloud_terminal_history_sync`, `pane_id_as_global_key`, `seq_across_epochs` |
| Fail state | `reject_stale_epoch_or_unknown_identity` |
| Rollback | Keep Contracts and Core. Do not add App, Infrastructure, or filebridge in G0. |
| Owner module | Contracts and Core; future HD-009/023/027/028 |
| Verification task | HD-009 |
| Spec-update trigger | App, Infrastructure, bridge, or filebridge modules are added. |
| Adoption | adopted as policy. `runtime_verified=false` |

## Subcontracts

These topics sit under the original ADR numbers. They are not a second ADR series.

### Dependency admission

Parent: ADR-001. Child AC: AC02-C2.

Admission stays `approved` / `blocked` / `pending` and is distinct from technical and security. Only approved units may enter package lock, WebView bundle, MSIX, or release manifest. Public visibility is not a license grant. herdrm stays blocked. Fail state: keep the unit out of build inputs.

### Renderer IPC capability, origin, and schema

Parent: ADR-002. Child AC: AC44-C1.

Versioned allowlist: `frame.apply`, `parse.consumed`, `input.user_key`, `input.committed_text`, `input.explicit_paste`, `terminal.resize`. Unknown type, oversize, wrong epoch, wrong pane, `host.exec`, navigation, and downloads are rejects. `InputContext` is never taken from the renderer message. `ime.preedit` is never transport. `input.emulator_reply` is known and denied by `InputPolicy`. Local origin. No CDN. No host objects. No generic exec. Fail state: `reject_web_message`. Live WebView2 origin remains unknown.

### Endpoint discovery

Parent: ADR-003. Child AC: AC03-C1.

`EndpointResolver` maps `DeviceId`, `SessionKey`, and a trusted-host config object. Default does not guess `%APPDATA%` or conventional pipe names. Named does not fall back to default. Remote UNC is rejected. `PaneKey` is not an endpoint key. Unmapped default requires explicit configuration. Named-pipe ACL remains blocked.

### Logging redaction

Parent: ADR-006.

G0 has no log framework. Failures use a stable redacted code. Probe default summaries keep counts and lengths. Stderr text requires `--include-diagnostics`. Public issues use synthetic data or an isolated disposable session. Deny terminal payload, credentials, private paths, and default argv in probe summaries. Fail state: omit the field.

### filebridge capability

Parent: ADR-007. Child AC: AC44-C2.

`herddesk-filebridge` is a future on-demand helper. Narrow commands: list, stat, read, write, rename. No exec. No recursive delete. No arbitrary command. Not an SFTP claim. Separate from `herddesk-bridge`. Protocol locks in P4. G0 does not ship the module. Fail state: `job_refused`.

## HD-001 / HD-003 / HD-004 / HD-005 ledger

Executable gates. `claim_passed` is false on every row. Blocked and unknown rows stay observe or explicit configuration.

| id | task | capability | path | evidence class | result | degrade |
|---|---|---|---|---|---|---|
| hd001-source-protocol | HD-001 | herdr source tag/blob/protocol | confirmed | source_inspection_only | recorded | source inspection is not runtime proof |
| hd001-windows-runtime | HD-001 | windows local runtime | blocked | blocked | blocked | observe_and_unknown |
| hd001-named-pipe-acl | HD-001 | named-pipe ACL | blocked | blocked | blocked | explicit_configuration_required |
| hd001-remote-runtime | HD-001 | remote Linux runtime | unknown | not_run | not_run | observe_and_unknown |
| hd001-runtime-hashes | HD-001 | runtime and distribution hashes | unknown | not_run | unknown | keep_hash_fields_null |
| hd003-endpoint-l1 | HD-003 | endpoint resolver L1 | confirmed | synthetic | recorded | synthetic is not Windows runtime proof |
| hd003-windows-endpoint | HD-003 | Windows endpoint runtime | blocked | blocked | blocked | explicit_configuration_required |
| hd003-unc-and-env-guess | HD-003 | UNC and environment guess | degrade | synthetic | degrade | reject UNC; require explicit configuration |
| hd004-lease-l1 | HD-004 | terminal lease mapper L1 | confirmed | synthetic | recorded | synthetic is not observe/control runtime proof |
| hd004-windows-lease | HD-004 | Windows terminal lease | blocked | blocked | blocked | observe_and_unknown |
| hd004-control-inference | HD-004 | ControlVerified inference | degrade | synthetic | degrade | deny ControlVerified from frame, process, or focus |
| hd004-unknown-control-signal | HD-004 | unknown control signal | unknown | not_run | unknown | stay Unknown or Acquiring; no write |
| hd005-renderer-l1 | HD-005 | renderer L1 host specimens | confirmed | synthetic | recorded | synthetic is not WinUI/IME proof |
| hd005-ime-desktop | HD-005 | IME and interactive desktop | blocked | blocked | blocked | observe; preedit not sent |
| hd005-native-raw-stream | HD-005 | native raw-stream | unknown | not_run | unverified | do not promote native |
| hd005-webview-origin-runtime | HD-005 | WebView2 origin / CDN / host objects | unknown | not_run | unverified | keep L1 allowlist; no WebView host |

Blocked categories:

- HD-001 / HD-004: `no_authorized_isolated_pane_or_live_herdr_grant`
- HD-003: `no_authorized_isolated_windows_endpoint_or_live_herdr_grant`
- HD-005: `no_authorized_winui_interactive_desktop_or_ime_grant`

## AC44

| Child | This task | Final |
|---|---|---|
| AC44-C1 | Renderer IPC allowlist, origin, and reject behaviour recorded | HD-035 on the shipping host |
| AC44-C2 | filebridge has no arbitrary command, arbitrary file, or cross-target capability | HD-035 |
| AC44 | not claimed | HD-035; `not_run` |

Standard-user runtime, named-pipe ACL, and live WebView2 origin remain unverified. This document does not pass AC44.

## R5

R5 phase-rule sync is not executed. HD-001 through HD-005 G0 target evidence is not accepted. `AGENTS.md` G0 prohibitions stay in force. `implementation/status.json` `phase_gate` stays `not_passed`. `planning/acceptance.json` AC44 stays `not_run`.

## Residuals

- G0 is not passed.
- Windows runtime, named-pipe ACL, terminal lease, IME, and WinUI stay blocked.
- Remote runtime and runtime hashes stay unknown.
- Native renderer stays unverified and is not promoted.
- No product App, Infrastructure, bridge, or filebridge modules.
