# herddesk-filebridge v1 error codes

Public exception / result messages are the code only. Do not put paths, JSON,
file bytes, display names, stderr, or credentials in the message.

## Protocol latch

| code | when |
|---|---|
| `protocol_not_active` | push after a prior protocol failure |
| `protocol_pollution` | magic is not `HDFB`; stdout is not a frame |
| `unsupported_version` | major/minor is not 1.0 |
| `unknown_kind` | kind not in the v1 table |
| `unknown_flags` | flags is not 0 |
| `payload_too_large` | JSON or Data length &gt; 1048576; checked before allocate |
| `invalid_payload_length` | EndData length is not 0 |
| `truncated_header` | EOF with 1–15 header bytes |
| `truncated_payload` | EOF before payload filled |
| `sequence_gap` | seq &gt; expected |
| `sequence_replay` | seq &lt; expected |
| `sequence_wrap` | a frame after seq 4294967295 on that direction |
| `wrong_direction` | kind/dir mismatch, including Data on the wrong stream |
| `unexpected_kind` | kind illegal in the current state |
| `second_request` | a second RequestJson |
| `data_before_accepted` | Data or EndData before AcceptedJson |
| `process_would_exit` | frame after CompleteJson or ErrorJson |
| `control_budget_exceeded` | cumulative JSON payload &gt; 16777216 |
| `terminal_result_mismatch` | Complete/Error vs process exit disagree |

EOF at a frame boundary with no Complete/Error is not a parse latch. The session
returns outcome `unknown`, sets `auto_replay_forbidden` for write/rename, and
rejects later frames with `protocol_not_active`. Truncated header/payload at EOF
still latch as `truncated_header` / `truncated_payload`.

## JSON and fields

| code | when |
|---|---|
| `invalid_json` | UTF-8, syntax, trailing junk, unpaired surrogate, BOM |
| `duplicate_json_key` | duplicate object key |
| `json_float_rejected` | JSON number with `.` / `e` / `E` |
| `json_number_invalid` | `-0`, leading zeros, integer overflow of i64 |
| `json_depth_limit` | nesting deeper than 32 |
| `unknown_field` | field not in the v1 table for that message |
| `missing_field` | required field absent |
| `invalid_field_type` | wrong JSON type |
| `invalid_job` | not a canonical lowercase UUID |
| `job_mismatch` | later frame job ≠ request job |
| `invalid_op` | op not list/stat/read/write/rename |
| `op_mismatch` | Accepted op ≠ request op |
| `invalid_wire_path` | WirePath syntax failed |
| `invalid_component` | decoded `.` / `..` / empty / NUL / `/` |
| `invalid_cursor` | cursor not canonical Base64 or decoded length not 1–4096 |
| `invalid_identity` | identity not canonical Base64 or decoded length not 1–4096 |
| `invalid_hash` | not 64 lowercase hex |
| `invalid_length` | decimal string invalid, or Data sum ≠ declared length |
| `invalid_observation` | not 64 lowercase hex |
| `invalid_limit` | list limit not integer 1–1000 |
| `replace_observation_required` | replace without target observation |
| `invalid_mode` | mode not create/replace |
| `invalid_reason` | CancelJson reason not `user` |
| `invalid_commit` | Complete commit value illegal for the op |

## Helper ErrorJson codes (payload `code`)

These appear inside ErrorJson. Unknown values stay opaque.

| code | meaning |
|---|---|
| `cancelled` | cancel/EOF before commit linearization |
| `conflict` | identity/observation conflict |
| `stale_target` | replace/rename target observation does not match |
| `not_found` | path missing |
| `permission_denied` | OS denied |
| `name_exists` | exclusive create collided |
| `parent_missing` | parent directory missing |
| `is_directory` | file op on a directory |
| `not_directory` | directory op on a file |
| `hash_mismatch` | content hash failed |
| `length_mismatch` | content length failed |
| `unsupported` | helper cannot perform the op |
| `outcome_unknown` | helper itself does not know |

Unknown ErrorJson `code` values are accepted on the wire and treated as a
generic remote failure. Do not log them.
