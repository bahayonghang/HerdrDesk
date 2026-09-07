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

`InputPolicy.Evaluate` returns `InputDecision` with `Allowed` and `Code`. It does not throw for a denied grant. Deny codes: `invalid_identity`, `wrong_pane`, `stale_epoch`, `control_not_verified`, `input_origin_denied`, `input_bytes_limit`. Allow code: `allowed`.

### Python `ProtocolError`

`strict_json_loads` maps duplicate keys, non-finite numbers, illegal UTF-8, unpaired UTF-16 surrogates, and depth > 64 to `ProtocolError` (`duplicate_json_key`, `nonfinite_json_number`, `json_depth_limit`, or `invalid_json`). Frame and input validators use their own codes (`object_required`, `noncanonical_base64`, `input_bytes_limit`, …). `NdjsonDecoder` uses `decoder_not_active`, `line_bytes_limit`, `truncated_ndjson_record`. `TerminalCaptureValidator` uses `terminal_stream_not_active` after close or failure.

C# and Python need not share every internal code string. They must share reject/accept intent on `tests/fixtures/protocol-edge-cases.json`.

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
