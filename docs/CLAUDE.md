# docs

[根索引](../CLAUDE.md) · 文档

生成日期：2026-09-08。规划正文在 [plan](plan/CLAUDE.md)。此处为仓库级记录，不是产品用户手册。

## 文件

| 路径 | 角色 |
|---|---|
| [harness-workflows.md](harness-workflows.md) | 五套工具入口、派发、权限核对与 CLI 缺失回退。本文件为跟踪矩阵真源；父任务 research 路径归档后不再作为依赖。五 CLI 会话加载仍为 UNVERIFIED。 |
| [adr/0001-g0-bootstrap.md](adr/0001-g0-bootstrap.md) | 已采纳。建仓/CI/诊断代码 ≠ 跨越 G0。Python 不替换 .NET Core/Rust bridge。CI Windows job 不是 IME/herdr 验收。 |
| [implementation-g0.md](implementation-g0.md) | 建仓前第一笔实施的历史记录。其中“未推送”“C# 未编译”已被后续托管状态取代。 |
| [publication.md](publication.md) | 2026-09-07 首次导入 GitHub。可复查 CI run `34138627135`，commit `629bb01bd6bdb76e8576fd29668aa84a4894e59b`。 |
| [licensing-register.md](licensing-register.md) | HD-002 名称与单元台账。无 herdrm 源码/图标。项目许可仍待维护者决定。AC02 未完成。 |
| [licensing/](licensing/README.md) | 机器可读 `register.json` 与候选准入模板。`admission` 与 technical/security 分栏。模板不是 approved。 |
| [source-verification.md](source-verification.md) | 本轮重读上游 blob：`client/mod.rs`、`ipc.rs`、`render_stream.rs`、schema header。限额是 HerdDesk 客户端策略。 |
| [plan/](plan/CLAUDE.md) | 12 专题档案。 |

尚未存在、由后续任务拟建：`docs/spikes/renderer-decision.md`（HD-005）、`docs/adr/approved-baseline.md`（HD-006）。

## 约束

- 更新进度时改 `implementation/status.json` 与 `planning/`，不要改写 `implementation-g0.md` 冒充当前状态。
- 公开 issue 不附终端正文、凭据、私人路径；见根 `SECURITY.md`。
