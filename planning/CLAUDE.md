# planning

[根索引](../CLAUDE.md) · 活动规划

生成日期：2026-09-08。机器可读进度。人类可读任务在 [../tasks](../tasks/CLAUDE.md)。`docs/plan/planning/` 与 `docs/plan/tasks/` 是档案副本。

## 职责

跟踪 36 项任务与 48 项验收。`scripts/validate_repository.py` 校验：ID 数量、依赖无环、`completed` 任务的依赖必须完成且对应 AC 为 `passed` 并带 evidence。

## Trellis 改造与 HD 产品

两套任务并存，状态不互相复制。

| 权威 | 路径 | 关系 |
|---|---|---|
| 产品 backlog / AC | 本目录与 [../tasks](../tasks/CLAUDE.md)、`acceptance.json` | HD-001–036 与 AC01–AC48 仍是产品进度。G0 `phase_gate=not_passed`。AC 全部 `not_run`。离线 `just ci` 不把产品 AC 标为 `passed`。 |
| 本轮工程改造 | `.trellis/tasks/`（父 `09-08-evergreen-harness-audit` 及子任务） | 协议失败锁存、SDK 默认只读、五工具入口、历史证据语义。HD-007 与 AC39/AC40/AC47 仍按产品 backlog 记录。 |
| 共享工程规则 | [../AGENTS.md](../AGENTS.md)、[../docs/harness-workflows.md](../docs/harness-workflows.md)、`.trellis/spec/` | 适用 Claude Code / Codex / Grok Build / Kimi Code / OMP。五 CLI 新会话加载仍为 UNVERIFIED。 |
| 历史导入证据 | [../docs/publication.md](../docs/publication.md)、`PUBLICATION_MANIFEST.json`、Actions run `34138627135` | 证明 SHA `629bb01` 的首次导入。日常门禁是 `just ci`。 |

`.trellis/tasks/00-bootstrap-guidelines` 仍为 `in_progress`；frontend 已按 WinUI 填写，未经开发者确认前不归档。

## 文件

| 文件 | 作用 |
|---|---|
| `backlog.json` | id、phase、depends_on、acceptance_ids、status、implementation_note |
| `acceptance.json` | AC01–AC48；`status_note=not_run`，`evidence=null` |
| `effort.json` | G0 7–13 … P5 7–12；合计 57–99；`contingency_ratio=0.25`；不含 native renderer 强制替换；`not_a_delivery_commitment=true` |
| `risks.json` | R01–R14，全部 `open_for_validation` |

## 任务状态（扫描日）

| 任务 | 状态 | 阻塞原因 / 下一步 |
|---|---|---|
| HD-001 | in_progress | Windows preview protocol 22 已记录但不兼容；ACL 未采集；远端 not_run |
| HD-002 | in_progress | 项目许可、商标/包身份、分发清点 |
| HD-003 | blocked | Windows pipe/ACL 与 endpoint 真机仍缺 |
| HD-004 | in_progress | 隔离 observe/control 已记录；control_verified 仍 false；AC05 未通过 |
| HD-005–036 | planned | 见 tasks 索引 |

阶段：G0 → P1 可观测 → P2 可写本地 MVP → P3 多设备 → P4 文件 → P5 发布。

## 验收分组（全部 `not_run`）

| 阶段 | ID | 主题 |
|---|---|---|
| G0 | AC01–03, AC05, AC44 | 版本、许可、端点、终端帧、特权边界 |
| P1 | AC04, AC11–12, AC17–18, AC39–40, AC47 | RPC、收敛、通知、CI、依赖边界 |
| P2 | AC06–10, AC13–16, AC20 | 控制、IME、TUI、所有权、无重放 |
| P3 | AC19, AC21–26 | 搜索、跨设备、SSH、helper |
| P4 | AC30–36 | 文件列举/传输/冲突/路径/附件/剪贴板 |
| P5 | AC27–29, AC37–38, AC41–43, AC45–46, AC48 | 背压、性能、安装、追溯 |

一项 AC 覆盖多平台时，未测平台不得借用其他平台结果标 `passed`。

## 风险（P1）

| ID | 风险 | 负责任务 |
|---|---|---|
| R01 | Windows 终端桥未运行验证 | HD-004 |
| R02 | IME/模式丢失 | HD-005 |
| R03 | 未授权素材进入分发 | HD-002 |
| R04 | 错误设备/旧 epoch 输入 | HD-016 |
| R05 | GUI 退出杀死 daemon | HD-013 |
| R06 | WebView 消息泛化执行 | HD-006 |
| R10 | 文件覆盖/路径攻击 | HD-032 |
| R13 | 更新/供应链 | HD-034 |

G0 桥或 IME 失败则停止扩大可写范围。文件损坏、错设备输入、GUI 误杀 agent 均可作为发布阻断。

## 入口

改状态时同时改 `backlog.json` 与 `tasks/HD-xxx.md`。通过 AC 必须写入证据路径。G0 未通过时不要把产品 AC 标为 `passed`。

当前快照：`implementation/status.json` 的 `verified_acceptance_ids=[]`，`windows_live_tests=isolated_capture_recorded`（非 AC 通过）。其中 `github_actions.verified_run_id=34138627135` 只覆盖 commit `629bb01`；计数可能滞后于后续协议/setup 回归。基线：`evidence/compatibility-baseline.json`。当前 HEAD 的 hosted Actions 为 UNVERIFIED。

## 约束

- `docs/implementation-g0.md` 是历史记录；当前托管状态以 `implementation/status.json` 与 Actions 为准。
- 回滚只断开本应用连接。
