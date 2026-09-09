# herddesk-filebridge protocol v1.0

Normative wire, JSON, path, state, and limit rules for HD-027 L1. Rust and C#
codecs implement this text independently. They consume the golden vectors under
`spec/test-vectors/`. Neither codec may generate the other codec's expected
results.

`herddesk-filebridge serve --stdio --protocol 1.0` is implemented as an L1
stdio helper. The process working directory is the sandbox root. There is no
TCP/pipe listener, no published install, and no live SSH. L2 filesystem,
SSH, and TOCTOU remain UNVERIFIED.

## Process contract

- Separate process from `herddesk-bridge`, WinUI, and terminal transport.
- One job per process. First client frame must be `RequestJson`. No second
  request. After `CompleteJson` or `ErrorJson` the process would exit.
- Ops: `list`, `stat`, `read`, `write`, `rename` only. No generic exec, no
  recursive delete, no `ls`/`dir` text parse, no credentials on the wire.
- Paths travel only inside stdin frames. stdout is protocol bytes only. stderr
  is bounded diagnostics and is not logged raw.
- stdin close is a cancel signal. EOF without a terminal result is failure.
  Exit 0 does not invent `CompleteJson`.
- Helper inherits the SSH login user. Authentication is HD-020/021/022 identity,
  host-key, and absolute helper path. This protocol has no password or key
  fields.

## Binary header

Every frame starts with 16 bytes, integers in network byte order (big-endian):

| offset | size | field |
|---:|---:|---|
| 0 | 4 | ASCII magic `HDFB` (`48 44 46 42`) |
| 4 | 1 | major = `1` |
| 5 | 1 | minor = `0` |
| 6 | 1 | kind |
| 7 | 1 | flags; v1.0 must be `0` |
| 8 | 4 | payload length `u32` |
| 12 | 4 | unidirectional sequence `u32`, starting at `0` |

Fail closed on magic, version, flags, kind, length, or sequence mismatch. Do
not scan for resynchronization. Two directions count sequence independently.
Sequence must not wrap: after sequence `4294967295` is consumed, a further frame
on that direction is `sequence_wrap`. Encoders must refuse to emit a wrapping
sequence.

Check the header length against the kind limit **before** allocating a payload
buffer. Oversize frames are rejected from the 16-byte header alone.

stdout bytes that are not an `HDFB` header are `protocol_pollution`.

## Kinds

| kind | name | direction | payload |
|---:|---|---|---|
| `0x01` | RequestJson | client → helper | JSON |
| `0x02` | Data | write: client → helper; read: helper → client | raw file bytes |
| `0x03` | EndData | client → helper | empty |
| `0x04` | CancelJson | client → helper | JSON |
| `0x11` | AcceptedJson | helper → client | JSON |
| `0x12` | EntryJson | helper → client | JSON |
| `0x13` | ProgressJson | helper → client | JSON |
| `0x14` | CompleteJson | helper → client | JSON |
| `0x7f` | ErrorJson | helper → client | JSON |

Any other kind is `unknown_kind`. JSON kinds and Data are limited to 1048576
payload bytes. EndData payload length must be 0 (`invalid_payload_length`
otherwise). Helper kinds on the client stream and client kinds on the helper
stream are `wrong_direction`. Data on `list`/`stat`/`rename` is
`unexpected_kind`. Read Data on the client stream or write Data on the helper
stream is `wrong_direction`.

## Sequences

- `list`: Request → Accepted → Entry\* → Complete. No Data.
- `stat`: Request → Accepted → Complete. No Data. No Entry.
- `read`: Request → Accepted → Data\* → Complete. No EndData.
- `write`: Request → Accepted → Data\* → EndData → Complete or Error.
- `rename`: Request → Accepted → Complete. No Data.
- `ProgressJson` may appear after Accepted and before the terminal result.
- `ErrorJson` may replace Accepted or replace Complete. It is a terminal result.
- `CancelJson` is allowed after Request and before a terminal result.
- First client frame must be RequestJson (`unexpected_kind` otherwise).
- A second RequestJson is `second_request`.
- Data or EndData before Accepted is `data_before_accepted`.
- Complete before Accepted is `unexpected_kind`.
- After a terminal result, further frames are `process_would_exit`.
- Write Data byte count must equal the declared `length` at EndData.
- Read Data byte count must equal Accepted `size` and Complete `length`.

## JSON rules

UTF-8 strict. No BOM. Depth 32 (root object is depth 1). Duplicate keys are
`duplicate_json_key`. JSON numbers with `.`, `e`, or `E` are
`json_float_rejected`. JSON integers that do not fit `i64` are
`json_number_invalid`. `-0` and leading zeros are `json_number_invalid`.
Trailing junk is `invalid_json`. Unpaired UTF-16 surrogate escapes are
`invalid_json`. Unknown fields are `unknown_field`. Missing required fields are
`missing_field`. v1.0 rejects unknown kind, version, and flags.

`job` is a canonical UUID: lowercase `8-4-4-4-12` hex with hyphens, no braces
or URN prefix. Integers that may exceed 53 bits (`length`, `size`, `mtime`,
`mtime_precision`, Progress `bytes`) are canonical decimal strings: `0` or
`[1-9][0-9]*` fitting `u64`. Hashes are 64-character lowercase hex. Opaque
bytes, identity, token, and cursor are padded canonical Base64 (standard
alphabet `+/`, padding required, decode-then-reencode must match the wire
string). Cursor decoded length is 1–4096 bytes. Identity decoded length is
1–4096 bytes.

Control, entry, and error JSON: 1 MiB per frame. Data: 1 MiB per frame.
Cumulative control JSON payload bytes (Request, Cancel, Accepted, Entry,
Progress, Complete, Error) must not exceed 16777216 (`control_budget_exceeded`).
`list` `limit` is a JSON integer 1–1000, not a string.

## Operations

Every RequestJson has `protocol` (`"1.0"`), `job`, and `op`. No extra fields.

### list

Required: `path` (WirePath), `limit` (integer 1–1000). Optional: `cursor`.

CompleteJson for list also requires `has_more` (boolean). `commit` is
`not_applicable`.

### stat / read

Required: `path`. Optional: `observation` (64 lowercase hex).

### write

Required: `parent` (WirePath), `name` (one Base64 raw component), `mode`
(`create` or `replace`), `expected_parent_observation` (64 hex), `length`
(decimal `u64` string), `sha256` (64 hex).

`replace` requires `expected_target_observation` (64 hex). Missing target
observation is `replace_observation_required`. `create` must omit
`expected_target_observation` (KeepBoth is still exclusive create; HD-028
chooses the candidate name before the request).

### rename

Required: `source` (WirePath), `source_observation` (64 hex), `parent`, `name`,
`mode`, `expected_parent_observation`. `replace` requires
`expected_target_observation`. `create` must omit it.

### CancelJson

Required: `job`, `reason`. v1 `reason` is `user` only. stdin close is a codec
signal, not this frame.

### AcceptedJson

Required: `job`, `op`, `identity`, `size` (decimal string). `op` must match the
request.

### EntryJson

Required: `job`, `name` (one component), `display_name` (string, not a path, no
NUL), `type` (`file`/`directory`/`symlink`/`other`), `size`, `mtime`,
`mtime_precision` (decimal strings), `identity`, `symlink` (boolean),
`observation`. `type=symlink` iff `symlink=true`. Subsequent requests use raw
`name` and identity, never `display_name`.

### ProgressJson

Required: `job`, `bytes` (decimal string).

### CompleteJson

Required: `job`, `path`, `length`, `sha256`, `observation`, `commit`
(`committed` / `not_applicable`). `has_more` is required for `list` and
forbidden otherwise. CompleteJson does not carry a shell or display path.
`commit=committed` is only valid for successful `write`/`rename`.
`list`/`stat`/`read` use `commit=not_applicable`.

### ErrorJson

Required: `job`, `code`, `stage`, `retryable`. No details field. `stage` is
`request`, `transfer`, `commit`, `cancel`, or `unknown`. `code` is 1–64 chars
`[a-z][a-z0-9_]*`. Known codes are listed in `error-codes.md`. Unknown codes
stay opaque: accept the frame, map to a generic remote failure, do not log the
raw string. `cancelled` is used for cancel-before-commit.

## WirePath

v1 is a Unix absolute path: `/` plus zero or more padded canonical Base64
components. Root is `/`. Remote Windows is not promised. Local Windows mapping
is a C# adapter concern and is not a wire vector.

Because standard Base64 includes `/`, parsers must not naive-split on `/`.
After the leading `/`, take the **shortest** prefix that is padded canonical
Base64 and is followed by `/` or end-of-string. Then skip the separator and
repeat. Encode by joining canonical Base64 components with `/` after a leading
`/`.

Decoded component bytes must not be empty, must not be `.` (`2e`), must not be
`..` (`2e 2e`), and must not contain NUL (`00`) or `/` (`2f`). Non-UTF-8,
Unicode, space, and newline bytes are allowed.

`name` in write/rename/entry is one component: canonical Base64 of bytes that
pass the same component rules.

## Observation

An observation is a SHA-256 (64 lowercase hex) over helper-canonical bytes of
Unix wire path, existence, stable file identity, size, mtime/precision, and
type. It is a stale/conflict detector, not sandbox or authorization. HD-028
must still linearize no-follow operations on a verified parent directory
handle. A UI confirmation does not authorize a later different object.
Replace without the exact target observation is rejected at request parse.

## Cancel, commit, exit

The helper has one commit linearization point before the final
rename/replace. The L1 codec models that point via `mark_commit_linearized`
(HD-028 would call it). It does not create files.

- Cancel or stdin close **before** linearization: stop consuming, delete only
  that job's exclusive temp (codec flag), terminal result is
  `ErrorJson` `code=cancelled`. Outcome `cancelled`.
- Cancel **after** linearization: do not report cancelled. Terminal result is
  `CompleteJson`. Outcome follows Complete plus exit.
- Disconnect / EOF before a terminal result, including after linearization
  before Complete is received: `outcome_unknown`. The client must re-stat.
  Never auto-replay `replace` or `rename`.
- Complete + exit 0 = success. Error + nonzero exit = known failure.
- Complete + nonzero, Error + 0, missing terminal + any exit, or frame/exit
  mismatch = `terminal_result_mismatch` and outcome `unknown`.

Exclusive temp is codec state only. Cancel deletes that temp flag. It does not
wildcard-delete other files.

## Incremental parse

Callers may push any chunk size. Partial reads must not change the accept or
reject result. Incomplete header at EOF is `truncated_header`. Incomplete
payload at EOF is `truncated_payload`. After a protocol error the session
latches and further pushes return `protocol_not_active`.

## 1 MiB boundary

`payload length = 1048576` is allowed for JSON and Data after the other
checks. `payload length = 1048577` is `payload_too_large` from the header
before payload allocation. Tests may construct the exact-limit JSON in memory
using a valid EntryJson whose `display_name` is ASCII `x` repeated until the
JSON body is 1048576 bytes. That construction is specified here so each codec
builds it itself.

## Command

```text
herddesk-filebridge serve --stdio --protocol 1.0
```

One job per process. stdout is protocol bytes only. stderr is bounded
diagnostics. Non-HDFB stdout is `protocol_pollution`. The sandbox root is the
process working directory. Paths are not accepted on argv.
