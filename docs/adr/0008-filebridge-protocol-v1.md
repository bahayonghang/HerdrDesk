# ADR-0008 · filebridge protocol v1.0

Status: **accepted** (wire only).

HD-028 owner review: v1 `commit`, observation, and identity fields are enough
to implement no-follow exclusive same-directory temp create and atomic
no-replace rename on a disposable local filesystem. KeepBoth remains exclusive
create of a caller-chosen candidate name. Replace stays capability-gated:
without `expected_target_observation` the codec rejects the request; a stale
target observation is conflict; the helper does not expose Replace when the OS
cannot prove a safe primitive. L2 live FS, live SSH, and TOCTOU remain
UNVERIFIED.

## Decision

- Independent `herddesk-filebridge` process contract, separate from
  `herddesk-bridge`, WinUI, and terminal stdio.
- 16-byte `HDFB` header, major 1, minor 0, flags 0, network-order length and
  unidirectional sequence from 0. Fail closed. No resync.
- JSON control/metadata; raw Data frames for file bytes. One job per process.
  Ops: list, stat, read, write, rename.
- Unix WirePath only. Observation tokens are stale/conflict detectors, not
  auth. Replace requires the target observation. KeepBoth remains exclusive
  create.
- Cancel before commit linearization yields `cancelled` and deletes only that
  job's exclusive temp. Cancel after commit yields Complete.
  Disconnect before receipt is `outcome_unknown`; the client re-stats and must
  not auto-replay replace/rename.
- Rust codec and C# host codec each read the same golden vectors. Neither
  generates the other's expected results. The serve binary pins `sha2` 0.10.8
  for SHA-256; C# uses BCL `SHA256`.
- Observation bytes are SHA-256 over helper-canonical `HD028OBS1` fields:
  path components, existence, type, identity, size, mtime, precision.
  Identity is volume+index on Windows local, dev+ino on Unix, portable
  fallback otherwise.

`herddesk-filebridge serve --stdio --protocol 1.0` is implemented as an L1
stdio helper whose working directory is the sandbox root. It is not a
published install and not live SSH.

## Rejected

- Folding file operations into the RPC bridge, WinUI host, or terminal
  transport.
- TCP or named-pipe listener, generic exec, recursive delete, `ls`/`dir` text
  parse, credentials on the wire.
- JSON-only stdio without magic (stdout banners).
- SFTP as the 1.0 product claim.
- Windows WirePath or remote Windows on this version.
- Compatibility negotiation inside v1.0.
- Unconditional overwrite when no-replace or exchange primitives are missing.

## Compatibility

v1.0 is strict. Unknown kind, version, flags, and fields fail closed. A later
approved protocol version needs new vectors. There is no in-band negotiation
layer.

## Rollback

Wire changes require a new approved version. If a later OS review finds the
identity fields insufficient for a live TOCTOU gate, disable the write
capability and keep the file surface read-only or off. Do not add generic
operations, parse `ls`, or put paths on argv to bypass the gap.

## Residuals

- L2 filesystem, live SSH, and TOCTOU remain `UNVERIFIED`.
- Product AC30, AC31, AC32, and AC34 are not passed.
- No published helper install. No live remote file operations.
- Helper Replace remains unsupported on L1.
