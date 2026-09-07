# Design

共享项目约束放在 AGENTS.md 的 Trellis 托管块外，使用一份简短架构、命令、授权和状态权威导航；CLAUDE.md 留索引并引用，现有各模块 CLAUDE.md 暂保留且由共用导航明确要求读取，避免复制十余套 AGENTS。

新增 docs/harness-workflows.md：五工具矩阵、native injection 与 child-side pull、plan/no-shell 和 check 可写权限差异、模型职责、运行证据。只保留一份团队说明；不复制私人 agent settings。

修改 .trellis/spec/backend/{index,directory-structure,error-handling,quality-guidelines,logging-guidelines,database-guidelines}.md：根据 G0 C#/Python 写真实约束，无数据库则明确 N/A。修改 .trellis/spec/frontend/index.md 将实际未实现和相关模板标为 deferred，禁止据模板假设 React。既有 00-bootstrap-guidelines 关联本子任务产物，最终由其负责人确认后收口；本轮不改状态。

本地五目录当前忽略，先使用 tracked 手工工作流保证可移植性。只有证明存在工具专属阻断的文件才加入精确 allowlist，不 blanket unignore .claude/.grok 等目录。若需更新生成 hooks，从对应 Trellis 源/模板修复并重生成；本计划不授权编辑用户全局 skill 源。

所有权交接另涉及 .trellis/tasks/00-bootstrap-guidelines/task.json 的 notes 与 prd.md 的交接段落：批准后、H 激活前记录 H 唯一实施 specs，bootstrap 仅消费验收证据。保留旧 frontend 未完成清单，不因 H 的 index/deferred 文案提前归档旧任务。此交接不在本轮审查中执行。

修正 .trellis/workflow.md:104 的全平台 hook 概括、:223/:226 的 Kimi 派发与“没有同名 Skill”冲突，以及 :483 附近把自动注入写成无条件能力的措辞。此为本仓库的受控文档变更；同时在 docs/harness-workflows.md 记录 Trellis 生成来源/版本与待上游同步条目，避免下次 update 覆盖。源模板仓库不在本次实施范围，必须给出明确 handoff，不能声称已经同步上游。

.gitignore 只补充本机生成适配器的范围说明；不取消整个目录的忽略。Kimi 本地 skill 的“无 project-level custom agents”已被当前官方文档否定，项目 tracked 说明明确纠正并提供现有 built-in coder + 显式上下文的可用回退，不擅自修改全局技能库。

文件所有权：本子任务唯一拥有 AGENTS.md、CLAUDE.md、docs/harness-workflows.md、.trellis/workflow.md、.gitignore 注释和 specs；其他子任务通过产物交付待回写内容。

## Ownership and model

强模型负责契约、根因和最终审查；更便宜模型只执行明确文件与检查清单。适用 Claude Code / Codex / Grok Build / Kimi Code / OMP；具体边界见父 research/harness-matrix.md。
