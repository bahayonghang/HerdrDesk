# docs

[根索引](../CLAUDE.md) · 文档

生成日期：2026-09-08。规划正文在 [plan](plan/CLAUDE.md)。此处为仓库级记录，不是产品用户手册。

## 文件

| 路径 | 角色 |
|---|---|
| [harness-workflows.md](harness-workflows.md) | 五套工具入口、派发、权限核对与 CLI 缺失回退。本文件为跟踪矩阵真源；父任务 research 路径归档后不再作为依赖。五 CLI 会话加载仍为 UNVERIFIED。 |
| [adr/0001-g0-bootstrap.md](adr/0001-g0-bootstrap.md) | 已采纳。建仓/CI/诊断代码 ≠ 跨越 G0。Python 不替换 .NET Core/Rust bridge。CI Windows job 不是 IME/herdr 验收。仓库组织 ADR，不是规划 ADR-001。 |
| [adr/0008-filebridge-protocol-v1.md](adr/0008-filebridge-protocol-v1.md) | HD-028 **accepted**（wire only）。L2 FS/SSH/TOCTOU UNVERIFIED。不是 AC30/AC31/AC32/AC34 通过。 |
| [adr/approved-baseline.md](adr/approved-baseline.md) | HD-006 冻结规划 ADR-001 至 ADR-007 与 HD-001/003/004/005 门。机器台账 [adr/approved-baseline.json](adr/approved-baseline.json)。不是 G0/AC44 通过。R5 未执行。 |
| [implementation-g0.md](implementation-g0.md) | 建仓前第一笔实施的历史记录。其中“未推送”“C# 未编译”已被后续托管状态取代。 |
| [publication.md](publication.md) | 2026-09-07 首次导入 GitHub。可复查 CI run `34138627135`，commit `629bb01bd6bdb76e8576fd29668aa84a4894e59b`。 |
| [licensing-register.md](licensing-register.md) | HD-002 名称与单元台账。HD-007 另记 pending NuGet 探针。无 herdrm 源码/图标。项目许可仍待维护者决定。AC02 未完成。 |
| [licensing/](licensing/README.md) | 机器可读 `register.json` 与候选准入模板。`admission` 与 technical/security 分栏。模板不是 approved。 |
| [source-verification.md](source-verification.md) | 本轮重读上游 blob：`client/mod.rs`、`ipc.rs`、`render_stream.rs`、schema header。限额是 HerdDesk 客户端策略。 |
| [plan/](plan/CLAUDE.md) | 12 专题档案。 |
| [testing/multi-device-mvp.md](testing/multi-device-mvp.md) | HD-026 L1 收口说明。各 AC 为 `not_run`。不是 live SSH/WinUI 通过。 |
| [testing/file-fault-security.md](testing/file-fault-security.md) | HD-032 L1 收口说明。AC31–AC35 为 `not_run`。不是 live FS/SSH/TOCTOU/attack/UI 通过。 |
| [testing/performance-soak.md](testing/performance-soak.md) | HD-033 L1 收口说明加 L2 Narrator overlay。AC27/28/29/37/38/46 为 `not_run`。overlay 不是 AC37。不是 live soak / 输入到像素 / DPI / Narrator 通过。 |
| [testing/packaging-manual.md](testing/packaging-manual.md) | HD-034 L2 未签名 lab layout 与收口说明。AC41/AC42 为 `not_run`。不是 live 安装/签名/更新/回滚 通过。 |
| [testing/security-release.md](testing/security-release.md) | HD-035 L1 收口说明加 L2 auditor overlay。AC02/AC43/AC44 为 `not_run`。不是 live 扫描/renderer 进程/canary/已签名包反向审计 通过。 |
| [testing/release-checklist.md](testing/release-checklist.md) | HD-036 后续独立用户步骤。L2 hosted-workflow pointer 不是 required-check。本 L1 目录不是 AC45。不是完整 1.0。 |
| [release/notes.md](release/notes.md) | HD-036 候选说明。未发布。不是完整 1.0。 |
| [release/support-matrix.md](release/support-matrix.md) | HD-036 支持矩阵投影。承诺组合保持 `not_run`。 |
| [user-guide/](user-guide/index.md) | HD-036 L1 用户说明。WinUI 外壳未准入。不是独立用户走查。 |
| [spikes/](spikes/CLAUDE.md) | HD-005 renderer 决策。WebView2/xterm 基线；native 不晋级。不是 AC08/AC09 通过。 |

## 约束

- 更新进度时改 `implementation/status.json` 与 `planning/`，不要改写 `implementation-g0.md` 冒充当前状态。
- 公开 issue 不附终端正文、凭据、私人路径；见根 `SECURITY.md`。
