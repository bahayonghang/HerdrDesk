# tests/python

[根索引](../../CLAUDE.md) · [tests](../CLAUDE.md) · python tests

生成日期：2026-09-08。标准库 `unittest`。计数以本次 `python -m unittest discover -s tests/python -v` 为准。`implementation/status.json` 的 73 是 SHA `629bb01` 托管记录，可能滞后。

## 职责

回归 Python 协议、探针安全门、发布清单、仓库可移植性、许可台账、endpoint 矩阵、terminal lease 映射、renderer L1 矩阵、ADR 冻结台账。不执行 herdr，不启动 GUI。

## 入口

```powershell
python -m unittest discover -s tests/python -v
```

每个文件将 `ROOT/scripts` 插入 `sys.path`。无 `pyproject.toml`、无 pytest。

## 测试文件

`def test_` 数量以本次 discover 输出为准，不与 `status.json` 的历史 73 强行对齐。

| 文件 | 类 | 覆盖 |
|---|---|---|
| `test_protocol.py` | `StrictJsonTests`, `FramerTests`, `FrameTests`, `InputTests`, `CaptureTests` | 重复键、非有限数字、深度脱敏、分块 NDJSON、无分隔符 EOF 失败、字段名 `bytes` 而非 `data`、非 canonical Base64、跨帧 UTF-8 不解码、关闭后锁存、新实例重置 seq、input text/bytes 互斥、surrogate、scroll/resize 整数类型、100 轮随机分块、`check_capture.py` 拒绝覆盖且路径脱敏 |
| `test_probe_safety.py` | `ProbeSafetyTests` | 无 disposable 时 control 不 `Popen`；observe 不能带 input；input 需单独 `--allow-input`；非法 target/session；`stop_owned` 不杀进程树；terminate 超时只 kill 自有 handle；默认报告去掉输出与 argv；probe 不宣称 ControlVerified 或 pane 死亡 |
| `test_publish.py` | `PublisherTests` | 清单 hash、路径穿越、未列出的 `.env` 不入库、错误 GitHub 身份、create 为 public 且非 force、dry-run 输出 `kind=historical_bundle_audit`、漂移退出码 2、已有 checkout/已有仓库拒绝 |
| `test_hd020.py` | `Hd020ResidualTests` | `implementation/hd-020-l2.json` 保持 L2 OpenSSH UNVERIFIED；AC22/AC23/G0 恒为 false；无 `tests/Integration.Ssh` |
| `test_hd021.py` | `Hd021ResidualTests` | `implementation/hd-021-l2.json` 保持 L2 live helper deploy UNVERIFIED；AC25/G0 恒为 false；无 `tests/Integration.Ssh` |
| `test_hd022.py` | `Hd022ResidualTests` | `implementation/hd-022-l2.json` 保持 L2 live SSH UNVERIFIED；AC24/AC26/G0 恒为 false；无 `tests/Integration.Ssh` |
| `test_hd023.py` | `Hd023ResidualTests` | `implementation/hd-023-l2.json` 保持 L2 live 3-device p95 UNVERIFIED；AC19/AC21/G0 恒为 false；无 `tests/Integration.Windows` |
| `test_hd024.py` | `Hd024ResidualTests` | `implementation/hd-024-l2.json` 保持 L2 live auth UNVERIFIED；AC22/AC26/G0 恒为 false；无 `tests/Integration.Ssh` |
| `test_hd025.py` | `Hd025ResidualTests` | `implementation/hd-025-l2.json` 保持 L2 live SSH/perf UNVERIFIED；AC27/G0 恒为 false；`b_ssh_measured` false；无 `tests/Integration.Ssh` |
| `test_hd026.py` | `Hd026ResidualTests` | `implementation/hd-026-l2.json` 与 `evidence/multi-device-mvp/` 保持 L2 live SSH UNVERIFIED；AC13/14/15/19/21/22/23/24/26/G0 恒为 false；模板不是成功运行；无 `tests/Integration.Ssh` / `Integration.Windows` |
| `test_hd027.py` | `Hd027ResidualTests` | `implementation/hd-027-l2.json` 保持 L2 FS/SSH/TOCTOU UNVERIFIED；AC30/AC34/G0 恒为 false；ADR-0008 accepted（wire only）；golden vector hash 与 manifest 一致 |
| `test_hd028.py` | `Hd028ResidualTests` | `implementation/hd-028-l2.json` 保持 L2 FS/SSH/TOCTOU UNVERIFIED；AC30/AC31/AC32/G0 恒为 false；无 Integration.Ssh/Windows；`sha2` 0.10.8 钉死 |
| `test_hd029.py` | `Hd029ResidualTests` | `implementation/hd-029-l2.json` 保持 L2 live UI/SSH UNVERIFIED；AC31/AC33/G0 恒为 false；WinUI 未准入；无 Integration.Windows |
| `test_hd030.py` | `Hd030ResidualTests` | `implementation/hd-030-l2.json` 保持 L2 live agent/IME/SSH UNVERIFIED；AC35/AC31/AC32/AC36/G0 恒为 false；WinUI 未准入；无 auto-submit；无 Integration.Windows |
| `test_hd031.py` | `Hd031ResidualTests` | `implementation/hd-031-l2.json` 保持 L2 live clipboard/IME UNVERIFIED；AC36/G0 恒为 false；WinUI 未准入；无 clipboard watcher；OSC 52 默认 deny；无 Integration.Windows |
| `test_repository.py` | `RepositoryPortabilityTests` | `validate()` 通过；元数据 `read_text` 必须 `encoding='utf-8'`；`terminal-valid.ndjson` 无 CRLF，SHA-256 `d206d2ad30aac1814193b2f0423bbf30405d113e6d172adb2a9f5bed34f6f599`；`ac44_passed` 与 `g0_passed` 恒为 false；`project_graph` passed；`github_required_check` UNVERIFIED |
| `test_project_graph.py` | `ProjectGraphTests` | 允许边通过；Core→WinUI/WebView2/SSH、Contracts 第三方、生产→测试、未知 Integration.Windows、`Directory.Packages.props`、`packages.lock.json`、csproj `2.4.0` 钉均失败。`bridge/` 不在 C# 项目图内。`Cargo.lock` 的 `interprocess` 版本必须与 `implementation/hd-008-packages.json` 一致；L2 保持 UNVERIFIED |
| `test_evidence.py` | `EvidenceBaselineTests` | 驱动 `herddesk_g0.evidence` 与仓库内真实 evidence 文件；拒绝 hash 混同、无采集文件的 runtime 成功、source/synthetic/hosted CI 提升、protocol 22 标成与 protocol 20 兼容、preview 进入默认兼容集、schema/snapshot 正文入库；Windows recorded 与 remote `not_run` 必须独立；ACL/IME 保持 blocked；结构校验 `windows_verified` 恒为 false |
| `test_licensing.py` | `LicensingRegisterTests` | 驱动 `herddesk_g0.licensing` 与 `docs/licensing` 真实台账/模板；拒绝 pending/blocked 当 approved、herdrm 拷贝宣称、公开可见当授权；`ac02_passed` 与 `windows_verified` 恒为 false |
| `test_endpoint.py` | `EndpointMatrixTests` | 驱动 `herddesk_g0.endpoint` 与 `tests/fixtures/endpoint-cases.json`；七行模拟矩阵；拒绝 APPDATA 猜测、常规 pipe 名猜测、named 回退、UNC、PaneKey 当身份、跨 DeviceId 映射、fixture 当 runtime/AC03 通过；`windows_verified` 与 `ac03_passed` 恒为 false |
| `test_lease.py` | `TerminalLeaseTests` | 驱动 `herddesk_g0.lease` 与 `tests/fixtures/lease-cases.json`；十四行模拟矩阵；隔离 pane capture 保持 `control_verified` false；拒绝首帧/进程/焦点/stdin 写置 ControlVerified、observe 发送输入、EOF/桥退出当 pane 死亡、帧 `bytes` 入库、虚构 Granted、fixture 当 runtime/AC05 通过；`windows_verified` 与 `ac05_passed` 恒为 false |
| `test_renderer.py` | `RendererMatrixTests` | 驱动 `herddesk_g0.renderer` 与 `tests/fixtures/renderer-cases.json`；十八行 L1 矩阵；拒绝 AC08/AC09 宣称、IME 已执行宣称、L3 假通过；`windows_verified` 与 `ac08_passed`/`ac09_passed` 恒为 false |
| `test_adr.py` | `ApprovedBaselineTests` | 驱动 `herddesk_g0.adr` 与 `docs/adr/approved-baseline.json` / markdown；拒绝 blocked/unknown 当 passed、G0/AC44 宣称通过、R5 已执行、平行编号、缺失证据路径、解除 AGENTS G0 禁令；`ac44_passed`、`g0_passed` 与 `windows_verified` 恒为 false |
| `test_local_mvp.py` | `LocalMvpCatalogTests` | 驱动 `evidence/local-mvp/catalog.json` 与 `implementation/hd-019-l2.json` / `hd-019-l3.json`；live 行 L2/L3 且 `UNVERIFIED`；Claude/Codex/OpenCode + PowerShell；Muse 不在 targets；缺 disposable pane / WebView2 / IME desktop / agent TUI versions；`ac06/07/10/15` 与 `g0_passed` 恒为 false |

## 依赖

- 代码：`scripts/herddesk_g0`（含 `evidence.py`、`licensing.py`、`endpoint.py`、`lease.py`、`renderer.py`、`adr.py`、`project_graph.py`）、`probe_herdr.py`、`publish_github.py`、`validate_repository.py`。
- 数据：[../fixtures](../fixtures/CLAUDE.md)。

与 C# smoke、probe selftest 分开报告，不合并为覆盖率。hosted SHA `629bb01` 为 Python 73 / C# smoke 22 / probe 23。

## 约束

- 断言错误消息时核对脱敏：载荷中的合成私钥/终端文本不得出现在 `ProtocolError` 字符串里。
- 新增回归优先使用合成 fixture。需要 herdr 的检查放探针 live 路径，默认 gitignore。
