# evidence

[根索引](../CLAUDE.md) · 证据

生成日期：2026-09-08。可提交的基线与工具链记录。真实探针输出在 gitignored `probe-results/`。规划期来源索引在 `docs/plan/evidence/版本与证据索引.md`。

## `compatibility-baseline.json`

| 字段 | 当前值 |
|---|---|
| `evidence_level` | `source_inspection_only` |
| herdr tag | v0.8.2 |
| commit | `9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c` |
| `api_protocol` / `schema_version` | 20 / 1 |
| `src/client/mod.rs` blob | `c33157fb0d9b7c1632562fced6bd9e439de80856` |
| `src/ipc.rs` blob | `36e69ea2a571de096e3150779eb9cae6e803d7a8` |
| `src/server/render_stream.rs` blob | `f14deb5e7e2f3183410020e5f36f7fb11dda6780` |
| schema blob | `f9642ffa0deb4dc87052a5247d700e1dcd50a753` |
| `runtime_binary_sha256` / `daemon_version` / `runtime_schema_sha256` | null |
| `runtime_verification.*` | `not_run` |
| `default_write_capability` | false |

Git blob SHA 不是二进制 SHA-256，也不是运行中 schema hash。`validate_repository.py` 断言 `api_protocol==20` 且默认不可写。

HD-001 完成条件：本地与远端各补齐 CLI version、daemon ping、schema protocol 与 hash。

## `toolchain.json`

记录首次本地实施环境：选定 SDK `10.0.400`，当时容器无 dotnet、Python 3.13.5、Linux。其中 `github_actions_status=not_run`、`dotnet_installed_in_this_execution=false` 是历史快照。当前 CI/构建以 `implementation/status.json` 为准。

## 约束

- 更新 runtime 字段必须附命令、主机、时间。禁止把客户端 `api schema` 当作 daemon 证明。
- 禁止把 preview issue（如 #3701）写入本文件当作 stable 缺陷。
