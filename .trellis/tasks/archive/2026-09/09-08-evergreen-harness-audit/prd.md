# 常青审查与五套 harness 能力对齐

状态：planning / 待用户批准实施。基线：2026-09-08，HEAD `8ae4797d0aa10559648d1ade5b7e017ff46c7a53`，起始工作区干净。

## Goal

建立可复核、可按项批准的改造计划；防止离线测试、工具目录存在或历史 CI 通过被误当成产品或五工具运行验收。

## Background

当前为 G0，已实现 BCL-only C# Contracts/Core 与 Python 诊断工具，未交付 WinUI 产品。
`planning/backlog.json` 与 `tasks/HD-*.md` 是产品任务，48 项产品 AC 未执行。
已有 `00-bootstrap-guidelines` 处于 in_progress；新任务不静默替代或归档它。

## Requirements

| ID | 需求 | 产物 |
|---|---|---|
| R1 | 读取结构和关键实现，区分已实现与规划 | research/audit.md |
| R2 | 跑现有测试，分别记录通过、失败、未运行及环境阻碍 | research/tests.md 与日志 |
| R3 | 失败要有触发条件、调用链、根因和复现边界 | research/audit.md |
| R4 | 对照 Claude Code、Codex、Grok Build、Kimi Code、OMP 能力与强/便宜模型分工 | research/harness-matrix.md |
| R5 | 优先级、最小改造文件、逐项检查和依赖 | design.md、implement.md、子任务 |
| R6 | 核查说明入口、冲突、缺失、追踪与加载差异 | research/audit.md |
| R7 | 批准后将稳定结论回写项目说明或 skill 库或团队知识库，注明适用工具 | 规则对齐与证据收口子任务 |
| R8 | 父子任务保留 planning；确认最新计划后逐项激活 | task.json 与子任务材料 |

## Scope and authorization

本轮授权读取源码/规则、离线测试、CI 只读调查、定向无副作用复现、创建父子任务及规划证据。
用户要求“先给出可批准的计划；我确认后再执行”。正式规则/知识回写在批准后实施。
不修改产品代码、安装依赖、改变用户环境、启动真实 herdr/SSH/GUI、提交或推送。

推荐审批范围为四项整体。若只批准某个代码项，该审批须同时列明该项必要的 H 文档回写及 E 证据收口作为随附范围，不包含 H/E 的其余改造；未批准工作继续 planning，不把部分闭环标为父任务或整个 H/E 完成。正式激活前按明确批准的闭包排定文件所有权。

## Acceptance Criteria

- AC1 → R1/R3：阻塞发现具备当前文件行号或原始日志；假设不标为已复现。
- AC2 → R2：命令、结果、计数、基线及运行边界齐全；历史 CI 失败与当前结果分开。
- AC3 → R4/R6：五工具分别说明入口、子代理/模型路由、权限局限和本仓库差距，附一手来源日期。
- AC4 → R5/R8：父任务与四个子任务各有 PRD、design、implement 和真实 JSONL，依赖可执行，状态保持 planning。
- AC5 → R7：批准后每项完成需有检查证据和项目内正式回写；禁止复制私人配置/凭据/原生会话记忆。
- AC6 → R1–R8：本轮只写任务规划与审查证据，无代码修复、安装、全局修改、提交或远端写入。

## Task map

| 优先级 | 子任务 | 交付 |
|---|---|---|
| P0 | 09-08-protocol-failure-contract | 协议失败边界与回归 |
| P1 | 09-08-harness-context-alignment | 五工具入口、规范和分工 |
| P1 | 09-08-sdk-setup-boundary | setup 检查/安装边界 |
| P2 | 09-08-evergreen-evidence-closeout | 历史清单、CI、HD/Trellis 映射和回写 |

## Out of scope

不提前实现 P1–P4 产品模块，不选许可证，不将合成检查升级为 AC01–AC48 通过证据，不创建五套重复规范或通用配置平台。
