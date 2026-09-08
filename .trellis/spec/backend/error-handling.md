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

`InputPolicy.Evaluate` returns `InputDecision` with `Allowed` and `Code`. It does not throw for a denied grant. Deny codes: `invalid_identity`, `wrong_pane`, `stale_epoch`, `control_not_verified`, `input_origin_denied`, `input_bytes_limit`. Allow code: `allowed`. `CompositionPolicy` adds `preedit_not_sent`, `ime_owns_shortcut`, `commit_origin_required`. `WebMessagePolicy` adds `unknown_web_message_type`, `unsupported_web_message_version`, `web_message_bytes_limit`. `RendererEpochGate` throws `stale_epoch` or `terminal_stream_not_active`. `RendererByteWindow` uses `queue_bytes_limit`, `queue_ack_mismatch`, and `delta_drop_forbidden`; parse-consumed is never presentation. Ack must match the oldest queued frame size. `Reset` rejects a lower epoch.

`EndpointResolver.Resolve` returns `EndpointResolutionResult`. It does not throw for a mapping failure and does not connect to a pipe. Failure codes: `invalid_identity`, `invalid_preference`, `explicit_configuration_required`, `named_session_unmapped`, `endpoint_not_found`, `permission_denied`, `cross_user_denied`, `remote_unc_rejected`, `unicode_encoding_error`, `ambiguous_mapping`. Codes and `DiagnosticId` omit paths, `%APPDATA%`, and unpaired surrogates.

`TerminalLeaseProbe.Map` returns `TerminalLeaseResult`. It does not throw for unconfirmed control and does not invent a Granted wire message. Codes include `observing`, `observe_input_denied`, `acquiring`, `control_unconfirmed`, `control_verified`, `busy`, `rejected`, `takeover_not_confirmed`, `takeover_required`, `control_not_verified`, `resize_unacknowledged`, `resized`, `released`, `release_unacknowledged`, `input_result_unknown`, `fictional_granted_rejected`, `unknown_control_signal`, `disconnected`, `invalid_observation`. `ControlVerified` is true only when `Access` is `Controlling` and the observation includes adapter write-ownership proof.

### Python `ProtocolError`

`strict_json_loads` maps duplicate keys, non-finite numbers, illegal UTF-8, unpaired UTF-16 surrogates, and depth > 64 to `ProtocolError` (`duplicate_json_key`, `nonfinite_json_number`, `json_depth_limit`, or `invalid_json`). Frame and input validators use their own codes (`object_required`, `noncanonical_base64`, `input_bytes_limit`, …). `NdjsonDecoder` uses `decoder_not_active`, `line_bytes_limit`, `truncated_ndjson_record`. `TerminalCaptureValidator` uses `terminal_stream_not_active` after close or failure.

Python `herddesk_g0.endpoint.EndpointError` uses stable codes (`evidence_level_promotion`, `ac03_claimed_passed`, `missing_matrix_row`, …). `resolve_endpoint` returns a result dict and does not connect to a pipe.

Python `herddesk_g0.lease.LeaseError` uses stable codes (`evidence_level_promotion`, `ac05_claimed_passed`, `fictional_granted_required`, `control_verified_from_frame_process_focus`, …). `map_lease` returns a result dict and does not call herdr.

`CompositionPolicy.Evaluate` and `WebMessagePolicy.Evaluate` return `InputDecision`. Deny codes include `preedit_not_sent`, `ime_owns_shortcut`, `commit_origin_required`, `unknown_web_message_type`, `unsupported_web_message_version`, `web_message_bytes_limit`, plus `InputPolicy` codes. `RendererByteWindow` returns `RendererQueueDecision` with `ParseConsumedIsPresented` always false. Overflow codes include `queue_bytes_limit` and `delta_drop_forbidden`. Ack mismatch is `queue_ack_mismatch`. A lower-epoch `reset` is `stale_epoch`.

Python `herddesk_g0.renderer.RendererError` uses stable codes (`evidence_level_promotion`, `ac08_ac09_claimed_passed`, `missing_matrix_row`, …). L1 helpers do not launch WinUI or WebView2.

C# and Python need not share every internal code string. They must share reject/accept intent on `tests/fixtures/protocol-edge-cases.json`, `tests/fixtures/endpoint-cases.json`, `tests/fixtures/lease-cases.json`, and `tests/fixtures/renderer-cases.json`.

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
