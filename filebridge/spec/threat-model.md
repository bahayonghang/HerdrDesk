# herddesk-filebridge v1 threat model

L1 protocol review. OS atomicity, symlink follow, and TOCTOU remain HD-028 /
HD-032 work. This document does not pass AC30 or AC34.

## In scope (protocol must prevent)

| threat | mitigation |
|---|---|
| Fold file ops into RPC bridge, WinUI, or terminal stdio | Separate crate and process contract. No file kinds on `herddesk-bridge`. |
| TCP or named-pipe listener | Codec only; no bind/listen. Serve command not implemented. |
| Generic exec / recursive delete | Ops are list/stat/read/write/rename only. |
| Path on argv or shell | Paths exist only inside framed JSON. |
| `ls` / `dir` text parse | Structured EntryJson with raw Base64 names. |
| Credentials on the wire | No password, key, token, or cookie fields. |
| stdout banner / SSH greeting | Non-`HDFB` bytes are `protocol_pollution`. No resync. |
| Oversize allocation | Header length checked before payload allocate. 1 MiB and 16 MiB caps. |
| JSON smuggling | Strict UTF-8, depth 32, duplicate keys rejected, JSON floats rejected, unknown fields rejected. |
| Path traversal via `.` / `..` / NUL / `/` | Rejected in decoded components. Protocol hiding of `..` is not a sandbox. |
| Display name reused as path | `display_name` is display-only. Later ops use raw component + identity. |
| Windows reserved names silently rewritten | No Windows WirePath. C# mapping returns mapping-required. No auto suffix. |
| Second job / mux | One request. `second_request` fails closed. |
| Replay / gap / wrap | Unidirectional seq from 0; wrap refused. |
| Auto-replay after unknown commit | `outcome_unknown` forbids replace/rename replay. Client must re-stat. |
| Cancel deleting unrelated files | Codec tracks only this job's exclusive temp flag. |
| Observation treated as auth | Spec states observation is stale/conflict detection only. |
| KeepBoth as check-then-overwrite | KeepBoth still sends `mode=create` exclusive create. |
| Replace without target identity | `replace_observation_required`. |
| Unbounded stderr logging | stderr is not parsed as protocol and is not logged raw. |
| Unknown version downgrade | v1.0 only; no negotiation layer. |

## Out of scope (protocol cannot finish)

| residual | owner |
|---|---|
| no-follow open of parent directory | HD-028 |
| same-directory exclusive temp create and atomic rename | HD-028 |
| stable file identity across filesystems | HD-028; ADR stays proposed until written review |
| symlink / TOCTOU on a live Unix host | HD-032 |
| live SSH helper launch | HD-021 / HD-028 |
| Windows local filesystem adapter | HD-028 C# native path, not this wire |
| OS permission as sandbox | login user remains authority |
| helper binary presence / hash | HD-021 |

## Rejected designs

- JSON-only stdio without magic (banner collision).
- SFTP as the 1.0 claim.
- Multiplexed jobs on one process.
- Windows path components on this wire version.
- Folding file bytes into RPC snapshot events.
- Compatibility negotiation inside v1.0.
