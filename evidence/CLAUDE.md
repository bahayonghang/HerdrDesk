# evidence

[根索引](../CLAUDE.md) · 证据

生成日期：2026-09-08。可提交的基线、运行时采集模板与工具链记录。真实探针输出在 gitignored `probe-results/`。规划期来源索引在 `docs/plan/evidence/版本与证据索引.md`。

`python scripts/validate_repository.py` 与 `herddesk_g0.evidence` 只证明 JSON/链接结构。结构校验不是产品验收，不能把 G0、AC01 或 Windows runtime 标为通过。

## `compatibility-baseline.json`

| 字段 | 当前值 |
|---|---|
| 顶层 `evidence_level` | `source_inspection_only` |
| herdr tag / commit | v0.8.2 / `9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c` |
| `api_protocol` / `schema_version` | 20 / 1 |
| `src/client/mod.rs` blob | 见文件 `herdr.source_blobs`（git blob SHA-1） |
| `src/ipc.rs` blob | 见文件 `herdr.source_blobs` |
| `src/server/render_stream.rs` blob | 见文件 `herdr.source_blobs` |
| schema blob | 见文件 `herdr.source_blobs` |
| `distribution_binary_sha256` | null |
| `runtime_binary_sha256` | `d3e69a7810beb6077c47bd8d876f50929152828a6c210c5e6d001f9220137da6`（preview；非 git blob） |
| `daemon_version` | null（无独立于 CLI `--version` 的 daemon ping） |
| `runtime_schema_sha256` | `5fb46b13fdaf39c88cf699b9806685868c7ee6b0142523d84391b1606416dc0a` |
| `runtime_verification.windows_local` | `recorded`（preview CLI `0.9.0-preview.2026-09-08-62431dbd033b`，runtime `api_protocol=22`；与源码 protocol 20 不兼容） |
| `runtime_verification.remote_linux` | `not_run`（独立条目，不借用 Windows） |
| `runtime_verification.named_pipe_acl` | `blocked`（`named_pipe_acl_not_captured`） |
| `runtime_verification.windows_endpoint` | `blocked`（`no_authorized_isolated_windows_endpoint_or_live_herdr_grant`） |
| `runtime_verification.windows_terminal_lease` | `recorded`（隔离 pane observe/control；`control_verified` 仍为 false；非 AC05 pass） |
| `runtime_verification.ime` | `blocked`（`no_authorized_winui_interactive_desktop_or_ime_grant`；HD-005 L3 未授权） |
| `default_write_capability` | false |

每条 `records[]` 含 `subject`、`environment`、`observed_at`、`evidence_level`、`version`、`hashes`、`result`、`redaction`、`limitations`、`attachments`。`hashes` 中 git blob SHA、分发 binary SHA-256、runtime schema SHA-256 分栏，禁止互推。源码字段来源在 source 记录的 `field_sources`。

## `version-support-matrix.json`

冲突行键：`(os, arch, cli_binary_hash, daemon_version, protocol, schema_hash)`。列：source、Windows runtime、remote runtime。任一关键字段未知则该组合不得标 `compatible`。runtime protocol 22 不得标为与 source protocol 20 兼容。`compatible_by_default` 当前为空。preview 不进入默认兼容集。

## `runtime/`

UTF-8 JSON。采集记录字段：`capture_id`、`kind`、`captured_at_utc`、`operator_scope`、`host_fingerprint_redacted`、`command_redacted`、`exit_code`、`stdout_sha256`、`stderr_sha256`、`evidence_level`、`limitations`。

| 文件 | 含义 |
|---|---|
| `capture.schema.json` | 采集字段契约；不是一次运行 |
| `windows-runtime.template.json` | Windows 线模板；`template=true`，空 hash/退出码 |
| `remote-runtime.template.json` | remote 线模板；不得填成一次成功运行 |
| `windows-runtime.capture.json` | AC01-C2 隔离 Windows 预检：recorded，protocol 22，非兼容 |
| `remote-runtime.not-run.json` | AC01-C3 独立 `not_run` 记录 |
| `windows-endpoint-matrix.blocked.json` | AC03-C1 当前记录：blocked；合成矩阵不是 runtime pass |
| `windows-terminal-lease.capture.json` | AC05-C1 recorded；`control_verified` 仍 false；合成 lease 矩阵不是 AC05 pass；独立于 endpoint 记录 |
| `windows-renderer-ime.blocked.json` | HD-005 IME/desktop 当前记录：blocked；L1 合成标本不是 AC08/AC09 pass；独立于 lease/endpoint 记录 |

模板不是成功运行。runtime 成功结论必须指向非模板采集文件。

## `toolchain.json`

记录首次本地实施环境：选定 SDK `10.0.400`，当时容器无 dotnet、Python 3.13.5、Linux。其中 `github_actions_status=not_run`、`dotnet_installed_in_this_execution=false` 是历史快照。当前 CI/构建以 `implementation/status.json` 为准。

## 约束

- 更新 runtime 字段必须附命令、主机、时间。禁止把客户端 `api schema` 当作 daemon 证明。
- 禁止把 preview issue（如 #3701）写入本文件当作 stable 缺陷。
- 禁止把 `source_inspection_only`、synthetic fixture 或 hosted CI 提升为 runtime 证明。
- 禁止把 git blob SHA 写入 `runtime_binary_sha256` 或 `runtime_schema_sha256`。
- Windows 与 remote 的 runtime 结论必须来自独立记录。
- 源码 `api_protocol` 保持 20；`default_write_capability` 保持 false。runtime protocol 22 不得提升为兼容。
- 结构校验返回 `windows_verified: false`。AC01/AC05 保持 not passed。
