# Common semantics: unpaired UTF-16 surrogates in terminal JSON

Date: 2026-09-08. Inputs are the three unfixed C# red records plus one valid-pair contrast. C# results are the captured unfixed parser evidence. Python results are from `strict_json_loads` / `validate_frame` / `TerminalCaptureValidator` on the same bytes before any `protocol.py` change.

## Wire records

| name | bytes (ASCII JSON, no LF) |
|---|---|
| unpaired_surrogate_type_value | `{"type":"\uD800"}` |
| unpaired_surrogate_property_name | `{"\uD800":1,"type":"terminal.frame","seq":1,"encoding":"ansi","width":80,"height":24,"full":true,"bytes":"aGVsbG8="}` |
| unpaired_surrogate_closed_reason | `{"type":"terminal.closed","reason":"\uD800"}` |
| valid_surrogate_pair_closed_reason | `{"type":"terminal.closed","reason":"\uD800\uDC00"}` |

Next-frame probe after each first parse: a valid `terminal.frame` seq=1 full=true `bytes=aGVsbG8=`.

## Unfixed C# (`TerminalFrameParser.Parse`)

From parent audit / plan-review. Inner exception is `System.InvalidOperationException` (`Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.`). `failed` is not set.

| record | first | then valid |
|---|---|---|
| type value | InvalidOperationException | accepted |
| property name | InvalidOperationException | accepted |
| closed reason | InvalidOperationException | accepted |

Sites: `GetString` helper (`type` / `encoding` / `bytes`), `JsonProperty.Name`, `reason.GetString()`.

## Unfixed Python (this session)

| record | `strict_json_loads` | `validate_frame` | capture first | capture then valid |
|---|---|---|---|---|
| type value | accept | `unknown_terminal_type` | `unknown_terminal_type`, `failed=True` | `terminal_stream_not_active` |
| property name | accept (`'\ud800'` key) | accept | accept seq=1 | `sequence_gap_or_replay` |
| closed reason | accept | accept `reason_present=True` | accept, `closed=True` | `terminal_stream_not_active` (closed, not failed) |
| valid pair | accept | accept `reason_present=True` | accept, `closed=True` | `terminal_stream_not_active` (closed) |

Python `json.loads` materializes unpaired surrogates as `str`. Extra object keys are ignored by `validate_frame`. `bool('\ud800')` is true, so a closed reason of U+D800 counts as present.

## Ruling

Shared protocol semantic: a terminal JSON record is malformed when a JSON string that the implementation materializes contains an unpaired UTF-16 surrogate. That is a string-materialization failure, not a field-schema failure.

- C# must convert `InvalidOperationException` at `GetString` / `Name` / `reason.GetString()` to `TerminalProtocolException("malformed_terminal_record")` and set the existing `failed` latch. Do not catch every `InvalidOperationException` from `Parse`. Do not add a pre-parser or a new error-code system.
- Python must reject the same three wire records on the shipped load path. Minimum change: after `json.loads` in `strict_json_loads`, refuse any dict key or string value that cannot UTF-8-encode. Reuse `invalid_json`. Do not require the C# code name.
- `unknown_terminal_type` is the wrong category for `{"type":"\uD800"}`. After the Python change, that record fails at load time.
- Valid pair `\uD800\uDC00` remains accepted as a closed envelope with `reason_present=true`.
- After the first malformed record, the same parser / `TerminalCaptureValidator` instance must reject the next valid frame with `terminal_stream_not_active` and `failed=True` (not because `closed` was set).

## Allowed remaining differences

- Error token: C# `malformed_terminal_record`; Python `invalid_json`.
- Extra unused string values: C# `JsonElement` is lazy, so an unread extra field value with `\uD800` is still accepted. Python `json.loads` materializes every string, so `strict_json_loads` rejects that extra value. Do not add a C# scan of unused values.
- `validate_frame` still ignores unknown field names that are valid Unicode scalars.
- Input `text` unpaired surrogates stay on `validate_input` → `invalid_input_unicode`. That path is not this JSON-record contract.

## Fixture

`tests/fixtures/protocol-edge-cases.json` stores each `raw` as a JSON string containing `\u` escapes, so the fixture file itself is valid UTF-8 and does not go into `invalid-cases.json` (probe selftest loads that file as one JSON document).
