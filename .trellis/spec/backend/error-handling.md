# Error Handling

Fail-closed protocol errors with stable, redacted codes. No HTTP problem-json layer exists.

---

## Overview

C# public protocol failures are `HerdDesk.Core.TerminalProtocolException`. The exception message is the code. Python uses `herddesk_g0.protocol.ProtocolError` (a `ValueError`). Neither type may echo terminal text, JSON payloads, credentials, or private paths.

---

## Error Types

### C# `TerminalProtocolException`

Defined in `src/HerdDesk.Core/TerminalFrameParser.cs`. Codes include:

`terminal_stream_not_active`, `line_bytes_limit`, `object_required`, `duplicate_json_key`, `unknown_terminal_type`, `unsupported_encoding`, `invalid_sequence`, `sequence_gap_or_replay`, `invalid_frame_dimensions`, `boolean_full_required`, `initial_full_frame_required`, `decoded_bytes_limit`, `noncanonical_base64`, `invalid_closed_reason`, `string_field_required`, `malformed_terminal_record`.

`InputPolicy.Evaluate` returns `InputDecision` with `Allowed` and `Code`. It does not throw for a denied grant. Deny codes: `invalid_identity`, `wrong_pane`, `stale_epoch`, `control_not_verified`, `input_origin_denied`, `input_bytes_limit`. Allow code: `allowed`. `CompositionPolicy` adds `preedit_not_sent`, `ime_owns_shortcut`, `commit_origin_required`. `WebMessagePolicy` adds `unknown_web_message_type`, `unsupported_web_message_version`, `web_message_bytes_limit`. `WebMessageValidator` adds `unknown_web_message_field`, `malformed_web_message`, `duplicate_json_key`, `noncanonical_base64`. `WebViewSecurityPolicy` adds `navigation_denied`, `new_window_denied`, `download_denied`, `permission_denied`, `host_object_denied`, `dialog_denied`, `devtools_denied`, `link_scheme_denied`, `link_gesture_required`. Observe resize is `observe_no_resize`. HD-015 host coordinators add `renderer_not_ready`, `composition_active`, `unsupported_key_profile`, `observe_scroll_denied`, `lease_not_granted`, `commit_already_accepted`, and `input_paused`. `RequestControl` does not set `ControlVerified`. HD-016 `ControlLeaseCoordinator` adds `target_stale`, `control_busy`, `control_rejected`, `ownership_unverified`, `takeover_confirmation_stale`, `candidate_backpressure`, `terminal_disconnected`, `input_not_sent`, and `input_outcome_unknown`. `busy`/`rejected`/`cancelled` are attempt outcomes, not `TerminalAccess` values. First frame, process alive, focus, and write receipt never set `ControlVerified`. `RendererEpochGate` throws `stale_epoch` or `terminal_stream_not_active`. `RendererByteWindow` uses `queue_bytes_limit`, `queue_ack_mismatch`, and `delta_drop_forbidden`; parse-consumed is never presentation. Ack must match the oldest queued frame size. `Reset` rejects a lower epoch. `RenderFlowController` adds `sequence_gap_or_replay` and `initial_full_frame_required` on apply, and invalidates `RenderToken` generation on cancel/reset.

`EndpointResolver.Resolve` returns `EndpointResolutionResult`. It does not throw for a mapping failure and does not connect to a pipe. Failure codes: `invalid_identity`, `invalid_preference`, `explicit_configuration_required`, `named_session_unmapped`, `endpoint_not_found`, `permission_denied`, `cross_user_denied`, `remote_unc_rejected`, `unicode_encoding_error`, `ambiguous_mapping`. Codes and `DiagnosticId` omit paths, `%APPDATA%`, and unpaired surrogates.

`TerminalLeaseProbe.Map` returns `TerminalLeaseResult`. It does not throw for unconfirmed control and does not invent a Granted wire message. Codes include `observing`, `observe_input_denied`, `acquiring`, `control_unconfirmed`, `control_verified`, `busy`, `rejected`, `takeover_not_confirmed`, `takeover_required`, `control_not_verified`, `resize_unacknowledged`, `resized`, `released`, `release_unacknowledged`, `input_result_unknown`, `fictional_granted_rejected`, `unknown_control_signal`, `disconnected`, `invalid_observation`. `ControlVerified` is true only when `Access` is `Controlling` and the observation includes adapter write-ownership proof.

### Python `ProtocolError`

`strict_json_loads` maps duplicate keys, non-finite numbers, illegal UTF-8, unpaired UTF-16 surrogates, and depth > 64 to `ProtocolError` (`duplicate_json_key`, `nonfinite_json_number`, `json_depth_limit`, or `invalid_json`). Frame and input validators use their own codes (`object_required`, `noncanonical_base64`, `input_bytes_limit`, …). `NdjsonDecoder` uses `decoder_not_active`, `line_bytes_limit`, `truncated_ndjson_record`. `TerminalCaptureValidator` uses `terminal_stream_not_active` after close or failure.

Python `herddesk_g0.endpoint.EndpointError` uses stable codes (`evidence_level_promotion`, `ac03_claimed_passed`, `missing_matrix_row`, …). `resolve_endpoint` returns a result dict and does not connect to a pipe.

Python `herddesk_g0.lease.LeaseError` uses stable codes (`evidence_level_promotion`, `ac05_claimed_passed`, `fictional_granted_required`, `control_verified_from_frame_process_focus`, …). `map_lease` returns a result dict and does not call herdr.

`CompositionPolicy.Evaluate` and `WebMessagePolicy.Evaluate` return `InputDecision`. Deny codes include `preedit_not_sent`, `ime_owns_shortcut`, `commit_origin_required`, `unknown_web_message_type`, `unsupported_web_message_version`, `web_message_bytes_limit`, plus `InputPolicy` codes. `RendererByteWindow` returns `RendererQueueDecision` with `ParseConsumedIsPresented` always false. Overflow codes include `queue_bytes_limit` and `delta_drop_forbidden`. Ack mismatch is `queue_ack_mismatch`. A lower-epoch `reset` is `stale_epoch`.

Python `herddesk_g0.renderer.RendererError` uses stable codes (`evidence_level_promotion`, `ac08_ac09_claimed_passed`, `missing_matrix_row`, …). L1 helpers do not launch WinUI or WebView2.

`AtomicConfigurationStore` returns `ConfigurationLoadResult` / `ConfigurationWriteResult` with a stable `Code`. It does not throw for malformed, forbidden, conflict, or replace failures. Codes: `configuration_missing`, `configuration_malformed`, `configuration_forbidden_field`, `configuration_version_unsupported`, `configuration_write_conflict`, `configuration_serialize_failed`, `configuration_replace_failed`, `configuration_backup_missing`, `invalid_identity`, `invalid_profile`, `duplicate_session`, `ssh_profile_invalid`. A failed write leaves the original file bytes and does not promote a temp file. Restore reads only `device-profiles.json.bak`. HD-020 SSH test codes include `ssh_executable_unavailable`, `ssh_config_invalid`, `host_key_unknown`, `host_key_changed`, `auth_unsupported`, `auth_failed`, `ssh_test_timeout`, `ssh_test_cancelled`, `ssh_test_failed`, and `persistence_failed`. Default results omit raw stderr, argv, host, user, and key paths. L2 isolated OpenSSH stays UNVERIFIED. Product AC22/AC23 stay not passed.

HD-021 helper codes include `helper_manifest_untrusted`, `helper_manifest_invalid`, `helper_platform_unsupported`, `helper_consent_required`, `helper_consent_stale`, `helper_upload_failed`, `helper_hash_mismatch`, `helper_version_collision`, `helper_selftest_failed`, `helper_activation_failed`, and `helper_cancelled`. Default results omit raw stderr, argv, host, user, and private paths. L2 live helper deploy stays UNVERIFIED. Product AC25 stays not passed.

HD-022 remote transport codes include `remote_stdout_protocol_pollution`, `remote_record_too_large`, `remote_stream_truncated`, `remote_bridge_incompatible`, `remote_daemon_unavailable`, `remote_child_exited`, `remote_transport_cancelled`, `remote_terminal_closed`, `remote_schema_incompatible`, `remote_trust_required`, and `remote_unclassified_exit`. Banner, handshake, and non-JSON stdout prefixes fail closed; stderr is never parsed as NDJSON. An unclassified non-zero SSH exit is `UnknownBlocked` and is not a transient retry. Retry disposition is reported without a transport timer. L2 live SSH stays UNVERIFIED. Product AC24/AC26 stay not passed.

`JsonlDiagnosticSink.TryWrite` returns false and increments `DroppedCount` for invalid events, a full queue, or I/O failure. Writer-thread exceptions are dropped; they must not crash the host or grant control. Log files are UTF-8 without BOM. `DiagnosticEvent` has no message, exception, or payload field.

`AttentionReducer` / `NotificationPolicy` return `NotificationDecision` with `NotificationAction` and a stable reason: `baseline`, `reconnect`, `duplicate`, `stale`, `offline`, `muted`, `rate_limited`, `old_epoch`, `not_notifiable`, `deliver`. They do not throw for a suppressed toast. Unknown `AgentStatusKind` stays `BusinessStateKind.Unknown` with the original raw value. `done` is not mapped to success. Windows toast L1 is `windows_toast_unverified` and does not block Store updates.

C# and Python need not share every internal code string. They must share reject/accept intent on `tests/fixtures/protocol-edge-cases.json`, `tests/fixtures/endpoint-cases.json`, `tests/fixtures/lease-cases.json`, and `tests/fixtures/renderer-cases.json`.

HD-027 `FileBridgeProtocolException` / Rust `herddesk_filebridge::Error` messages are the code only (`payload_too_large`, `protocol_pollution`, `replace_observation_required`, `sequence_wrap`, …). Unknown ErrorJson codes stay opaque. HD-028 helper/local codes include `cancelled`, `stale_target`, `name_exists`, `mapping_required`, `outcome_unknown`. L2 FS/SSH/TOCTOU remain UNVERIFIED. Product AC30/AC31/AC32/AC34 stay not passed.

HD-030 `AttachmentCoordinator` codes include `capability_unknown`, `direct_clipboard_denied`, `target_stale`, `control_revoked`, `upload_integrity`, `input_rejected`, and `result_unknown`. Missing evidence is Unknown and does not advertise attachment support. Path insert never claims agent receipt. Diagnostics omit file body, full path, and credentials. L2 live agent/IME/SSH remain UNVERIFIED. Product AC35 stays not passed.

`herddesk-bridge` stderr codes: `bridge_usage`, `bridge_endpoint_invalid`, `bridge_connect_denied`, `bridge_connect_failed`, `bridge_relay_failed`. C# RPC codes include `rpc_protocol_pollution`, `rpc_envelope_invalid`, `rpc_connection_lost`, `rpc_unknown_response_id`, `rpc_subscribe_ack_failed`, `rpc_event_queue_overflow`, `rpc_not_sent`, `rpc_cancelled_after_write`, `rpc_binary_client_socket_rejected`, `rpc_subscription_lost`, `rpc_request_lost`, `rpc_reconcile_failed`. Projection/decoder codes: `rpc_required_field_missing`, `rpc_field_type_invalid`, `rpc_duplicate_identity`, `rpc_parent_missing`, `rpc_schema_incompatible`, `rpc_error_envelope`, `stale_epoch`, `full_snapshot_required`. Request EOF, subscription EOF, protocol error, schema incompatible, and caller disconnect stay distinct. Messages omit raw JSON, titles, cwd, endpoint paths, terminal text, and credentials.

`TerminalCliTransport` write receipts use `NotSent`, `WrittenUnacknowledged`, and `UnknownAfterDisconnect`. They do not mean upstream execution. Fail-closed stdout codes include `line_bytes_limit`, `truncated_ndjson_record`, `malformed_terminal_record`, `sequence_gap_or_replay`, and `terminal_consumer_backpressure`. Observe input is `observe_input_denied`. First frame, process alive, and focus never set `ControlVerified`. L2 live herdr stays UNVERIFIED.

HD-017 `ResourceCommandCoordinator` uses `ResourceGateDecision` / `ResourceOperation.Code`. Codes include `rpc_schema_incompatible`, `capability_unknown`, `stale_target`, `stale_confirmation`, `mutation_already_sent`, `workspace_group_close_required`, `close_group_unconfirmed`, `agent_kind_unverified`, `argv_rejected`, `rpc_not_sent`, `rpc_cancelled_after_write`, `timeout`, and `unknown_outcome`. Timeout after write becomes UnknownOutcome then a read-only query. Mutations are not retried. `close_group` is omitted unless the user confirms group close. L2 live mutation stays UNVERIFIED. Product AC20 stays not passed.

HD-018 `RecoveryPolicy` classifies `RecoveryFailure` and returns `RecoveryDecision`. Cause codes: `request_eof`, `subscription_eof`, `rpc_bridge_exit`, `daemon_unreachable`, `schema_incompatible`, `protocol_incompatible`, `terminal_closed`, `terminal_stdout_eof`, `terminal_client_exit`, `renderer_failure`, `cancellation`, `manual_disconnect`, `app_stopping`, `authentication`, `host_key_changed`. Decision codes: `retry_after`, `retry_now`, `await_user`, `stop`, `retry_exhausted`. `StartDaemon` stays false. Abnormal RPC loss publishes Stale before any retry timer. Recovery does not call RecoverControl or replay input. L2 live disconnect stays UNVERIFIED. Product AC13/AC14/AC15 stay not passed.

HD-024 extends that taxonomy with `transient_network`, `transient_transport`, `authentication_blocked`, `authentication_unsupported`, `host_key_unknown`, `daemon_unavailable`, `protocol_pollution`, `incompatible`, `cancelled`, and `unknown_blocked`. Public UI codes: `reconnect_waiting`, `authentication_action_required`, `host_key_review_required`, `authentication_unsupported`, `connection_manual_retry_required`, `reconnect_cancelled`, `input_not_replayed`. Only transient network/transport auto-retry with equal-jitter delays in `[1s,30s]`. Auth and host-key failures persist a `DeviceId+ProfileRevision` block and schedule zero timers. Unknown HD-020/022 outcomes default to manual block. L2 live auth stays UNVERIFIED. Product AC22/AC26 stay not passed.

HD-025 admission and queue codes: `connection_budget_exhausted`, `terminal_queue_limit`, `stale_render_ack`, `transport_cancel_timeout`, `process_start_failed`. Unknown stay fail-closed. Messages omit credentials, host/path, terminal body, and raw stderr. `WaitingForCapacity` / `PausedForCapacity` are not Ready. L2 live SSH/perf stays UNVERIFIED. `b_ssh_measured` stays false. Product AC27 stays not passed.

---

## Error Handling Patterns

### Fail latch (C#)

`TerminalFrameParser` keeps `failed` and `closed`. After either flag is set, `Parse` throws `terminal_stream_not_active`. Reconnect constructs a new parser. `seq` is not compared across epochs.

`JsonException`, `DecoderFallbackException`, and `FormatException` become `malformed_terminal_record` and set `failed`. `TerminalProtocolException` also sets `failed` before rethrow.

### Unpaired UTF-16 surrogates (required)

`System.Text.Json` `JsonElement.GetString()` and `JsonProperty.Name` throw `InvalidOperationException` for unpaired surrogates. That exception is not `JsonException`. Every current string or name materialization must catch it and throw `TerminalProtocolException("malformed_terminal_record")` so the existing `catch (TerminalProtocolException)` path latches `failed`:

- `GetString(JsonElement)` for field values (`type`, `encoding`, `bytes`, `terminal.closed.reason`)
- `GetName(JsonProperty)` during duplicate-key walk

A later valid first frame on the same instance must throw `terminal_stream_not_active`. Do not let `InvalidOperationException` escape. Do not put the surrogate or payload in the exception message.

Python: `strict_json_loads` calls `_require_unicode_scalars` after `json.loads`. A string that cannot `encode('utf-8')` becomes `ProtocolError('invalid_json')`. `TerminalCaptureValidator.accept` latches on `ValueError` (including `ProtocolError`).

Shared cases: `tests/fixtures/protocol-edge-cases.json` (`unpaired_surrogate_type_value`, `unpaired_surrogate_property_name`, `unpaired_surrogate_closed_reason`, `valid_surrogate_pair_closed_reason`). C# smoke: `RejectMalformedThenLatch`. Python: `reject_and_latch`.

### State write order

Validate every field before `lastSequence` / `closed` assignment. Do not decode terminal payload bytes as UTF-8. A UTF-8 character may split across frames.

### Input policy

`InputContext` is created by a trusted host. Do not deserialize `InputContext` from a renderer message. `ControlVerified` stays false until an adapter proves write ownership. First frame, process alive, and window focus do not set that flag.

---

## API Error Responses

There is no public HTTP API. Probe JSON reports use `scripts/probe_herdr.py` `safe_summary`: lengths and codes, not stdout/stderr text, unless `--include-diagnostics`.

---

## Common Mistakes

- Catching only `JsonException` and letting unpaired-surrogate `InvalidOperationException` skip `failed = true`.
- Truncating an oversize line or dropping a delta instead of stopping the connection.
- Putting attacker-controlled JSON or terminal bytes into logs or exception messages.
- Treating Python `bool` as a wire integer.
- Comparing `seq` across connection epochs or reusing a parser after reconnect.
