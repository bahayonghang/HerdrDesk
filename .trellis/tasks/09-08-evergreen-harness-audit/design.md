# 改造设计与边界

目标为修补当前 G0 的已证实缺陷和协作上下文，使用现有 C#/Python/PowerShell 与 Markdown 结构，不新增依赖或平台层。

## 单一权威

- 编译实现：src，而非 docs/plan/contracts 草案。
- 产品状态：planning/backlog.json、tasks/HD-*.md、planning/acceptance.json。
- 改造执行：本 Trellis 父子任务，状态不自动映射成产品 AC passed。
- 共享工程/授权规则：AGENTS.md 托管块外；CLAUDE.md 引用并保留模块索引。
- 工具差异：docs/harness-workflows.md，官方能力、静态本机配置和真实加载三列分开。
- SDK：global.json；历史 publication 清单保持历史语义，不跟当前工作区自动同步。

## 子任务机制与验收映射

| 父需求 | 子任务 | 机制 | 核心验收 |
|---|---|---|---|
| R1/R3/R5 | protocol-failure-contract | JSON string materialization 异常收口，failed latch，最小共同语料 | P-AC1–6 |
| R3/R5 | sdk-setup-boundary | 默认只读、显式安装/持久化、前置校验 | S-AC1–4 |
| R4/R6/R7 | harness-context-alignment | 共享入口、真实 spec、五工具薄适配/手工回退 | H-AC1–5 |
| R2/R5/R7 | evergreen-evidence-closeout | 历史证据范围、HD/Trellis 映射、最终回写 | E-AC1–4 |
| R8 | 全部 | planning 等待批准，按项 start 与检查 | 父 AC4/AC6 |

## 依赖与所有权

协议与 SDK 核心修改可并行，分别独占 C#/protocol fixture 与 PowerShell/stub tests。
规则子任务独占 AGENTS/CLAUDE/spec/workflow/harness 文档；其内容框架可先做，最终回写等两项修复结果。
README 由文档负责人串行集成 SDK 与证据文案；不让两个 worker 并发改同一文件。
证据收口最后执行，汇总已批准项检查和未验证项。
H 激活前在旧 00-bootstrap-guidelines 的 task.json.notes/prd.md 写所有权交接：H 唯一编辑 specs，旧任务只验收；确认没有并发 bootstrap 写入者。未实现 frontend 清单保留 pending/deferred，不因 H 的 index 更新无条件归档旧任务。

## 模型与权限

本轮主线程负责规划，独立 quality_release_auditor 和 agent_skill_architect 使用预设强模型审查，非便宜模型规划。
批准后强模型继续拥有协议/控制权/权限/验收裁决；便宜模型只接明确文件与检查。每次派发必须显式 task 路径、文件所有权、预期输出、禁用动作和完成条件。
不将 Grok/Kimi 无 shell 的 plan 子代理派去跑测试；构建写 bin/obj 的限制与只读源码审查分开。
不因换 harness 放宽授权，不因角色名称推断实际模型价格。

## 回写与可移植性

优先选择用户允许的“项目说明”回写途径，无需本轮修改全局 skill 或团队记忆。
各子任务输出稳定结论交规则负责人统一写回 AGENTS/spec/docs，标明适用五工具及特例。按项批准必须同时明确该项 H 回写和 E 证据切片的随附授权；不借此执行未批准的其他改造，也不提前完成整个 H/E 或父任务。
本机生成适配器保持忽略，团队有 tracked 手工入口。Kimi/Trellis 源模板改造输出明确 handoff，源仓库修改不包含在本次审批范围。
五 CLI 新会话验收无证据就保留 UNVERIFIED；若缺客户端/账号，完成静态改造也不能宣布五工具运行对齐完成。

## 风险和回滚

不吞掉不相关程序异常，不验证原始终端 bytes 为 Unicode，不改变控制权策略。
setup 默认改为只读是有意行为变化，不需兼容旧的静默安装行为。
各子任务按自身 diff 独立回退，研究记录保留。无安装、无远端/真实会话变动，不需要环境迁移或数据回填。
