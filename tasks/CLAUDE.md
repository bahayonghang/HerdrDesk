# tasks

[根索引](../CLAUDE.md) · 活动任务正文

生成日期：2026-09-08。每项 `HD-xxx.md` 与 `planning/backlog.json` 对应。档案副本在 `docs/plan/tasks/`。模板：`TASK_TEMPLATE.md`。

本目录是产品 backlog 正文。本轮工程改造在 `.trellis/tasks/`（父 `09-08-evergreen-harness-audit`）。完成 Trellis 子任务不改下表 HD 状态，也不把 AC01–AC48 标为 `passed`。共享入口：[AGENTS.md](../AGENTS.md)、[docs/harness-workflows.md](../docs/harness-workflows.md)。对照见 [planning/CLAUDE.md](../planning/CLAUDE.md)。

状态字段：`planned` / `in_progress` / `blocked` / `accepted`。正文里的“拟建产物”是目标路径，不表示文件已存在。完成证据必须区分 synthetic 与真机；仅 mock 不能把 Windows 行为标 passed。回滚不得停止用户 herdr。

## G0

| ID | 标题 | 状态 | 依赖 | AC | 拟建产物 |
|---|---|---|---|---|---|
| HD-001 | 锁定上游事实与版本 | in_progress | — | AC01 | `evidence/compatibility-baseline.json` |
| HD-002 | 名称和授权清点 | in_progress | — | AC02 | `docs/licensing-register.md` |
| HD-003 | API endpoint 与 pipe 映射 | blocked | HD-001 | AC03 | `tests/fixtures/endpoint-cases.json` |
| HD-004 | 终端桥协议探针 | in_progress | HD-001 | AC05 | `tests/fixtures/real-terminal-v082/` |
| HD-005 | WinUI renderer 与中文输入 spike | planned | HD-004 | AC08, AC09 | `docs/spikes/renderer-decision.md` |
| HD-006 | 安全边界与 ADR 定稿 | planned | HD-002, 003, 005 | AC44 | `docs/adr/approved-baseline.md` |

HD-005：小型 WinUI 宿主对比 WebView2/xterm 与 native raw-stream，不写产品业务。HD-007 虽已有目录/CI 准备，ADR-0001 规定提前准备不等于 P1 完成。

## P1

| ID | 标题 | 依赖 | AC | 拟建产物 |
|---|---|---|---|---|
| HD-007 | 仓库骨架与 CI | HD-006 | AC39, AC40, AC47 | `src/` 与 workflows |
| HD-008 | RPC stdio bridge 和请求层 | HD-003, 007 | AC03, AC04 | `bridge/` 与 `HerdDesk.Infrastructure/Rpc` |
| HD-009 | 类型契约与状态投影 | HD-008 | AC04, AC11 | Core Store |
| HD-010 | 订阅与收敛策略 | HD-009 | AC12 | dirty + 再读 |
| HD-011 | 本地导航与搜索 | HD-009 | AC19 | App 导航 |
| HD-012 | 通知与未读 | HD-010, 011 | AC17, AC18 | 通知 |

HD-008 风险：stderr 污染或读写互锁。回滚：回到 snapshot 诊断，不冒充实时连接。

## P2

| ID | 标题 | 依赖 | AC |
|---|---|---|---|
| HD-013 | TerminalCliTransport 与生命周期 | HD-008, 010 | AC05, AC06, AC15 |
| HD-014 | 可交付 WebView2 终端 adapter | HD-005, 007, 013 | AC08, AC27 |
| HD-015 | 输入法、键盘与选择 | HD-014 | AC09, AC10 |
| HD-016 | 控制权状态机 | HD-013, 014 | AC07, AC14, AC16 |
| HD-017 | workspace 与 agent 基本操作 | HD-009, 016 | AC20 |
| HD-018 | 断连和崩溃恢复 | HD-010, 013, 016 | AC13–15 |
| HD-019 | 本地 MVP 综合验收 | HD-012, 015, 017, 018 | AC06, AC07, AC10, AC15 |

## P3–P5

| ID | 标题 | 阶段 |
|---|---|---|
| HD-020–026 | SSH 身份、helper 部署、远端通道、多设备聚合/退避/预算与验收 | P3 |
| HD-027–032 | filebridge 协议、传输、双栏 UI、投入 agent、剪贴板、安全测试 | P4 |
| HD-033–036 | soak/可访问性、签名安装、最终许可、发布文档 | P5 |

完整依赖与人日见 `planning/backlog.json` 与 `docs/plan/docs/09_里程碑与执行Backlog.md`。

## 执行顺序（G0 未通过时）

1. Python 回归与 .NET build/smoke（已有 CI）。
2. 隔离 Windows herdr session 记录 HD-001 runtime。
3. 显式 endpoint 跑 HD-003，不猜 `%APPDATA%`。
4. 只读 probe 收集 HD-004；control/resize/release 需维护者授权 disposable pane。
5. 真实报告进 `probe-results/`。之后才进入 HD-005 spike。
