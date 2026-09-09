# ADR-0008 · filebridge protocol v1.0

Status: **proposed**.

This ADR records the HD-027 L1 wire. It is not accepted. HD-028 must still
confirm in writing that `commit`, observation, and identity fields are enough
to implement no-follow exclusive temp create and atomic rename on the target
OS. Until that review, v1 is not locked.

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
  job's exclusive temp (codec state). Cancel after commit yields Complete.
  Disconnect before receipt is `outcome_unknown`; the client re-stats and must
  not auto-replay replace/rename.
- Rust codec and C# host codec each read the same golden vectors. Neither
  generates the other's expected results. Zero extra Cargo crates.

`herddesk-filebridge serve --stdio --protocol 1.0` is documented and **not
implemented**. This task ships no `main.rs` and no filesystem backend.

## Rejected

- Folding file operations into the RPC bridge, WinUI host, or terminal
  transport.
- TCP or named-pipe listener, generic exec, recursive delete, `ls`/`dir` text
  parse, credentials on the wire.
- JSON-only stdio without magic (stdout banners).
- SFTP as the 1.0 product claim.
- Windows WirePath or remote Windows on this version.
- Compatibility negotiation inside v1.0.

## Compatibility

v1.0 is strict. Unknown kind, version, flags, and fields fail closed. A later
approved protocol version needs new vectors. There is no in-band negotiation
layer.

## Rollback

While status is proposed, the wire may be rewritten. If HD-028 cannot implement
commit/identity semantics, withdraw this proposal and keep the file surface
disabled. Do not add generic operations, parse `ls`, or put paths on argv to
bypass the gap. After a future accept, changes require a new approved version.

## Residuals

- L2 filesystem, live SSH, and TOCTOU remain `UNVERIFIED`.
- Product AC30 and AC34 are not passed.
- No published binary, no helper install, no live file operations.
- HD-028 owner written review of commit and identity fields is outstanding.
