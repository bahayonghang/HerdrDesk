# scripts

[根索引](../CLAUDE.md) · scripts

生成日期：2026-09-08。G0 诊断工具与 Python 协议库。生产 Windows 运行时不在本目录。

## 职责

- 有界 NDJSON / 终端帧 / 输入命令校验。
- 只读或显式授权的 herdr 探针。
- 仓库结构与发布清单核验。

Python 3.10+，仅标准库。将本目录加入 `sys.path` 后 `import herddesk_g0` 与 `import probe_herdr`。

## 包：`herddesk_g0`

| 符号 | 作用 |
|---|---|
| `Limits` | 行 16MiB、帧 8MiB、输入 64KiB |
| `ProtocolError` | 稳定错误类别；消息不含终端正文 |
| `strict_json_loads` | 拒重复键、非有限数字、非法 UTF-8、深度 >64 |
| `validate_frame` | `terminal.frame` / `terminal.closed`；返回 `(seq, summary)`，summary 含 `decoded_bytes` 与 payload `sha256` |
| `validate_input` | `terminal.input` / `resize` / `scroll` / `release` |
| `NdjsonDecoder` | 按 LF 切行；失败锁存；无 LF 的 EOF 失败，即使字节碰巧是合法 JSON |
| `TerminalCaptureValidator` | 单 epoch 有序帧；重连必须新实例 |
| `analyze_capture` | 总字节上限默认 256MiB；调用 `classify_stream_end`；`pane_exit_verified` 与 `daemon_or_pane_exit_verified` 在离线文件上为 false |
| `classify_stream_end` | stdout EOF / `terminal.closed` / 桥进程退出；EOF 不是 pane 死亡 |
| `evidence.EvidenceError` | 稳定证据规则码；消息仅为码 |
| `evidence.validate_evidence` / `check_evidence` | 基线/矩阵/采集结构；拒绝 hash 混同、非 runtime 提升、protocol 22 标成与 protocol 20 兼容、preview 进入默认兼容集、schema/snapshot/终端正文入库、ACL/IME/endpoint 非 blocked；`windows_verified` 恒为 false |
| `licensing.LicensingError` | 稳定许可规则码；消息仅为码 |
| `licensing.validate_licensing` / `check_licensing` | 加载 `docs/licensing` 台账与模板；拒绝 pending/blocked 当 approved、herdrm 拷贝、公开可见当授权；`ac02_passed` 恒为 false |
| `endpoint.EndpointError` | 稳定 endpoint 规则码；消息仅为码 |
| `endpoint.validate_endpoint_matrix` / `check_endpoint_matrix` / `resolve_endpoint` | 七行模拟矩阵与受控映射；拒绝 APPDATA 猜测、named 回退、UNC、fixture 当 runtime/AC03 通过；`windows_verified` 与 `ac03_passed` 恒为 false |
| `lease.LeaseError` | 稳定 lease 规则码；消息仅为码 |
| `lease.validate_terminal_lease_matrix` / `check_terminal_lease_matrix` / `map_lease` | 十四行模拟矩阵与受控映射；隔离 capture 保持 `control_verified` false；拒绝首帧/进程/焦点/stdin 写置 ControlVerified、observe 发送输入、EOF/桥退出当 pane 死亡、帧 `bytes` 入库、虚构 Granted、fixture 当 AC05 通过；`windows_verified` 与 `ac05_passed` 恒为 false |
| `renderer.RendererError` | 稳定 renderer 规则码；消息仅为码 |
| `renderer.validate_renderer_matrix` / `check_renderer_matrix` / `assemble_utf8` / `evaluate_web_message` | 十八行 L1 矩阵；拒绝逐块替换字符、旧 epoch、预编辑发送、observe 转发、无界队列、未知 web type；`windows_verified` 与 `ac08_passed`/`ac09_passed` 恒为 false |
| `adr.AdrError` | 稳定 ADR 规则码；消息仅为码 |
| `adr.validate_adr_baseline` / `check_adr_baseline` | 加载 `docs/adr/approved-baseline.json` 与 markdown；拒绝 blocked/unknown 当 passed、G0/AC44 宣称通过、R5 已执行、平行 ADR 编号、缺失证据路径、解除 AGENTS G0 禁令；`ac44_passed`、`g0_passed` 与 `windows_verified` 恒为 false |
| `project_graph.validate_project_graph` | HD-007 允许依赖图；禁止 Core→WinUI/WebView2/SSH、Contracts 第三方、生产→测试、非准入 PackageReference、umbrella WASDK 2.4.0；允许 App windows TFM 的 `Microsoft.WindowsAppSDK.WinUI` 2.3.6、`Directory.Packages.props` 与 `src/HerdDesk.App/packages.lock.json`；HD-014 允许唯一 npm lock `web/terminal/package-lock.json` 的 `@xterm/xterm` 6.0.0，拒绝 unscoped `xterm`、CDN，以及 `web/terminal/dist/*.js` 中的 TypeScript 语法 |

`validate_repository.py` 另读取 `implementation/hd-019-l2.json`、`implementation/hd-019-l3.json`、`implementation/hd-020-l2.json`、`implementation/hd-021-l2.json`、`implementation/hd-022-l2.json`、`implementation/hd-023-l2.json`、`implementation/hd-024-l2.json`、`implementation/hd-025-l2.json`、`implementation/hd-026-l2.json`、`implementation/hd-027-l2.json`、`implementation/hd-027-packages.json`、`implementation/hd-028-l2.json`、`implementation/hd-029-l2.json`、`implementation/hd-030-l2.json`、`implementation/hd-031-l2.json`、`implementation/hd-032-l2.json`、`implementation/hd-033-l2.json`、`implementation/hd-034-l2.json`、`implementation/hd-035-l2.json`、`implementation/hd-036-l2.json`、`evidence/local-mvp/catalog.json`、`evidence/multi-device-mvp/catalog.json`、`evidence/files/catalog.json`、`evidence/quality/catalog.json`、`evidence/packaging/catalog.json`、`evidence/security-release/catalog.json` 与 `evidence/releases/catalog.json`。L2/L3 live local MVP 保持 `UNVERIFIED`。L2 live helper deploy 保持 `UNVERIFIED`。L2 live SSH 保持 `UNVERIFIED`。L2 live auth 保持 `UNVERIFIED`。L2 live SSH/perf 与 `b_ssh_measured` 保持 `UNVERIFIED`/`false`。HD-026 收口目录的 AC13/14/15/19/21/22/23/24/26 与 `g0_passed` 保持 false。HD-032 收口目录的 AC31/32/33/34/35 与 `g0_passed` 保持 false。HD-033 收口目录的 AC27/28/29/37/38/46 与 `g0_passed` 保持 false；L3 IME/Narrator/DPI 与 L4 soak 保持 `UNVERIFIED`。HD-034 收口目录的 AC41/AC42 与 `g0_passed` 保持 false；L2/L3 live 安装/签名/更新/回滚保持 `UNVERIFIED`。HD-035 收口目录的 AC02/AC43/AC44 与 `g0_passed` 保持 false；L2/L3 live 扫描/renderer/canary/已签名包反向审计保持 `UNVERIFIED`。HD-036 收口目录的 AC39/AC40/AC45/AC47/AC48 与 `g0_passed` 保持 false；L2/L3/L4 live 走查/矩阵/SHA 绑定/干净还原/required-check/图重跑/发布/签名 hash 保持 `UNVERIFIED`。产品 AC06/AC07/AC10/AC15/AC25/AC24/AC26/AC27/AC28/AC29 与 `g0_passed` 保持 false。`tests/Integration.Windows` 与 `tests/Integration.Ssh` 不得存在。

`validate_input`：`text` 与 `bytes` 必须恰好一个；空载荷拒绝。Python `bool` 不得当作 JSON 整数。Base64 必须与 `b64encode` 回比一致。终端 payload 不做 UTF-8 解码。

`__init__.py` 仅模块说明，不导出符号。

## `probe_herdr.py`

子命令：`selftest`、`preflight`、`observe`、`control`。`shell=False`。`executable()` 经 `shutil.which` 后 `Path.resolve()`，不从 cwd 猜同名二进制。

### `capture`

启动本探针的直接子进程。stdout/stderr 分线程有界读取。超时或溢出时 `stop_owned`：`terminate`，1s 后 `kill` 同一 handle。返回码、是否超时、是否溢出、时长、原始字节。默认摘要只含长度，不含正文。`--include-diagnostics` 才附加 stderr 文本。

### `preflight`

只读命令：`--version`、`api schema --json`、`api snapshot`、`terminal session observe --help`、`control --help`。记录二进制 SHA-256。schema 校验 `protocol`/`schema_version` 为整数，并与参考头 `20`/`1` 比较。schema 导出到 `--output` 同名 `.schema.json`，默认不覆盖。snapshot 默认只记 `root_keys` 与 `has_error`；`--include-snapshot` 才会写入完整对象。范围声明：不测事件订阅、GUI、IME、control。

### `stream_probe`

argv：`herdr [--session S] terminal session {observe|control} TARGET --cols --rows`。target 拒绝空、前导 `-`、NUL、CR/LF。control 必须 `--disposable-target`。输入必须同时具备 control、`--allow-input`、`--disposable-target`，且文件 ≤64KiB UTF-8；发送 `terminal.input` 的 `text` 字段，不附加 Enter。stdout 按行、每行 `MAX_LINE+1` 读取。帧摘要最多保留 200 条。control 结束时尽力写 `terminal.release`，`release_acknowledged` 恒为 false。`takeover_used` 恒为 false。`control_verified` 恒为 false。`wire_shape_checks_passed` 要求 `frame_count>0` 且无 errors。EOF 与 `terminal.closed` 经 `classify_stream_end` 分类；`pane_exit_verified` 在无独立 pane 观测时为 false。

### `selftest`

读取 `tests/fixtures` 的 valid/invalid 案例，再对本机 `sys.executable` 做正常/超时/溢出三次 `capture`，并检查 `classify_stream_end`、`map_lease` 负例与 renderer L1 负例。`herdr_executed=false`。计数以本次运行为准。

### 其他 CLI

`--output` 默认 `xb` 独占创建。`--session` 作为单一 argv。`--seconds`/`--timeout` 区间 `(0,120]`。`--cols`/`--rows` 为正 u16。

## 其余入口

| 文件 | 行为 |
|---|---|
| `check_capture.py` | `analyze_capture`；`--output` 独占创建 |
| `validate_repository.py` | UTF-8 JSON；`herddesk_g0.project_graph` 校验允许/禁止依赖边；无 `PackageReference`；36 任务无环；48 AC；调用 evidence/endpoint/lease/renderer/licensing/adr；`implementation/hd-007-packages.json` 的 `github_required_check` 为 `UNVERIFIED`；baseline `api_protocol==20`；`windows_verified` 与各 AC/G0 恒为 false |
| `run_windows_desktop_gate.py` | WinUI 未准入时跳过 desktop restore；跳过不是全应用绿色 |
| `publish_github.py` | 历史 `PUBLICATION_MANIFEST.json` 审计（`kind=historical_bundle_audit`）。默认 dry-run，漂移退出码 2。日常门禁是 `just ci`。`--publish` 仅历史空仓建仓，已有仓库拒绝 |
| `Invoke-HerdDeskPreflight.ps1` | pwsh 7 包装 `preflight`；仓库内无 Windows 执行证据 |
| `package_release.ps1` | HD-034 L2 unsigned layout：`-Action Verify` 对 fixture 在 Ubuntu/Windows 运行；`-Action Build` 仅 Windows；无证书则 Sign 失败。未签名本地构建不是发行安装。AC41/AC42 仍为 false |

## 测试

[../tests/python](../tests/python/CLAUDE.md)。CI 另跑 `selftest` 与 `check_capture.py`。

## 约束

- 默认观察。不自动 takeover、不停止/升级 daemon、不重放输入、不绕过审批。
- 真实报告放 `probe-results/`（gitignore）。
- `safe_summary` 默认去掉 stdout/stderr 正文与 argv。
- 已有 GitHub 仓库禁止运行 `--publish`。
- `just ci` 在 HD-008 后包含 `cargo fmt/clippy/test --locked`（`bridge/` 与 `filebridge/`）。L2 named-pipe ACL 与 filebridge FS/SSH/TOCTOU 仍为 UNVERIFIED。
