# evidence

[根索引](../CLAUDE.md) · 证据

生成日期：2026-09-08。可提交的基线、运行时采集模板与工具链记录。真实探针输出在 gitignored `probe-results/`。规划期来源索引在 `docs/plan/evidence/版本与证据索引.md`。

`python scripts/validate_repository.py` 与 `herddesk_g0.evidence` 只证明 JSON/链接结构。结构校验不是产品验收，不能把 G0、AC01 或 Windows runtime 标为通过。

## `compatibility-baseline.json`

| 字段 | 当前值 |
|---|---|
| 顶层 `evidence_level` | `source_inspection_only` |
| 规划钉 | GitHub **v0.9.0** / protocol 22 / `b99002a`（父任务 `research/herdr-0.9.0.md`） |
| 源码对照行 herdr tag / commit | v0.8.2 / `9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c`（`compatibility-baseline.json` 源码检查行，测试钉死） |
| `api_protocol` / `schema_version` | 源码检查行 20 / 1；运行时 preview 22；规划钉 22 |
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

## `multi-device-mvp/`

HD-026 L1 P3 收口目录。`catalog.json` 列出 AC21/22/23/24/13/14/15/26/19 与 budget 执行卡，指向已有 L1 产物。每个 live 行 `status=UNVERIFIED`、`result=not_run`。`support-matrix.json` 承诺 Windows 11 x64 客户端 + Linux x64 远端，两行均为 `not_run`。macOS/ARM64 为 `unsupported` 或 `experimental`。模板 `template=true`，空 hash/退出码，不是成功运行。无第三 DeviceId，AC19 保持 `not_run`。产品 AC 仍未通过。

## `files/`

HD-032 L1 P4 文件故障与安全收口目录。`catalog.json` 列出 payload / permission-ENOSPC / SSH link interrupt / ssh-kill / helper-kill / symlink-junction-reparse / 32-way KeepBoth / Fail-Replace / dual-pane UI / attach-no-Enter 执行卡，指向已有 HD-028/029/030/031 L1 产物。三种中断各有独立 `missing_grant`，不可并成一条断网结论。每个 live 行 `status=UNVERIFIED`、`result=not_run`。`support-matrix.json` 承诺 Windows 11 x64 客户端 + Linux x64 远端，两行均为 `not_run`。macOS/ARM64 为 `unsupported` 或 `experimental`。模板 `template=true`，空 hash/退出码，不是成功运行。假 FS / mock process 不能通过真实 TOCTOU。产品 AC31–AC35 仍未通过。

## `quality/`

HD-033 L1 P5 性能/可访问性/soak 收口目录，外加 L2 采集器指针。`catalog.json` 列出 cold start / input-to-visible-pixel / search p95 / working set 1/4 pane / 100 hide-show / Narrator / DPI 100-150-200 / 8h soak 执行卡。`environment-pointer.json` 指向 gitignored `probe-results/` 原始环境清单，live 仍 `not_run`。parser consumed 不是 GPU 或可见像素呈现。不得从 `Q_p` 推导进程工作集。MiB = 1,048,576 bytes。每个 live 行 `status=UNVERIFIED`、`result=not_run`。`github_required_check` 为 `UNVERIFIED`。`support-matrix.json` 承诺 Windows 11 x64 客户端 + Linux x64 远端，两行均为 `not_run`。macOS/ARM64 为 `unsupported` 或 `experimental`。模板 `template=true`，空 hash/退出码，不是成功运行。不得发明 timings。hosted CI 不是交互桌面。产品 AC27/AC28/AC29/AC37/AC38/AC46 仍未通过。

## `packaging/`

HD-034 L2 P5 签名/安装/更新回滚收口目录，外加未签名 lab layout 叠加。`catalog.json` 列出 clean install / runtime missing / signed update / bad publisher-or-tamper / signed rollback / config backup-restore / file-job defer / unsigned local build 执行卡。`App.xaml` 已存在。`packaging/` 是 lab identity 未签名 layout，不是 WAP 工程，不是发行 Publisher。假 Publisher 不能让 AC41 通过。未签名本地构建不能当发行安装。复制旧 EXE 不能当回滚。每个 live 行 `status=UNVERIFIED`、`result=not_run`。`support-matrix.json` 承诺 Windows 11 x64 客户端，行为 `not_run`。Linux x64 是远端 OS，不是 MSIX 客户端。macOS/ARM64 为 `unsupported` 或 `experimental`。模板 `template=true`，空 hash/退出码/Publisher，不是成功运行。产品 AC41/AC42 仍未通过。

## `security-release/`

HD-035 L1 P5 安全与许可收口目录加 L2 工作树 admitted-input auditor。`catalog.json` 列出 license inventory / herdrm-not-copied / nuget-scan / cargo-scan / npm-scan / renderer-boundary / diagnostic-canary / signed-package-reverse-audit 执行卡，指向已有 HD-002 许可台账、HD-006 ADR 基线、HD-014 renderer allowlist、HD-020/024 SSH fail-closed、HD-031 clipboard/OSC52/cache、HD-032 文件故障目录、HD-034 包装目录。L2 auditor overlay 不是 live 扫描；AC02/AC43/AC44 仍未通过。`inventory.json` 指向 `docs/licensing/register.json`；所列单元保持 pending/blocked。缺扫描不是零漏洞。公开可见不是许可授予。每个 live 行 `status=UNVERIFIED`、`result=not_run`。`support-matrix.json` 承诺 Windows 11 x64 客户端，行为 `not_run`。Linux x64 是远端 OS，不能替代 Windows renderer/进程观察。macOS/ARM64 为 `unsupported` 或 `experimental`。模板 `template=true`，空 hash/扫描日期/工具版本，不是成功运行。产品 AC02/AC43/AC44 仍未通过。

## `releases/`

HD-036 L1 P5 发布文档与追踪归档收口目录，外加 L2 hosted-workflow pointer overlay。`catalog.json` 列出 user-guide / support-matrix / ac48-trace / ac39-clean-restore / ac40-hosted-required-check / ac47-dep-graph / unpublished-candidate / signed-hash-sbom 执行卡，指向已有 HD-001–035 L1 产物与 `docs/user-guide/`、`docs/release/`。`hosted-workflow-pointer.json` 绑定 observed SHA `602c252` 与 hosted run `34438599236`；hosted workflow 不是 GitHub required-check ruleset。`ac-index.json` 追溯 AC01–AC48，状态保持 `not_run`。每个 live 行 `status=UNVERIFIED`、`result=not_run`。`candidate_sha` 与 `hosted_check_run_id` 保持 null。`support-matrix.json` 承诺 Windows 11 x64 客户端 + Linux x64 远端，两行均为 `not_run`。macOS/ARM64 为 `unsupported` 或 `experimental`。core 1.0 不冒充 EP-01..EP-07。模板 `template=true`，空 hash，不是成功运行。未发布。不是完整 1.0。产品 AC39/AC40/AC45/AC47/AC48 仍未通过。

## `local-mvp/`

HD-019 场景目录。`catalog.json` 列出 observe/control/IME/input/resize/scroll/release/GUI close/recovery/agent TUI。每个 live 行 `required_evidence` 为 L2 或 L3，`status` 为 `UNVERIFIED`。L1 composition 不是本地 E2E pass。缺 disposable pane、WebView2、IME desktop、agent TUI versions。产品 AC06/AC07/AC10/AC15 仍未通过。

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
