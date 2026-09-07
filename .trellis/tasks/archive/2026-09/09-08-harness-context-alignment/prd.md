# P1 五套 harness 规则入口与规范对齐

状态：planning；本轮只写计划，待用户确认父计划后实施。
父任务：../09-08-evergreen-harness-audit

## Goal

五套工具获得同一项目事实与授权边界，并将工具特有能力写成可核验的薄适配说明。

## Requirements

H1 根 AGENTS.md 承载共享项目约束和权威入口；CLAUDE.md 引用共用源并保留必要平台加载信息。
H2 明确五工具实际子代理/skills/hooks/模型路由差异和手工上下文回退。
H3 将当前代码的真实规范写入 Trellis，前端未实现项明确 deferred/N/A。
H4 说明 fresh checkout 与本机 gitignored 工具资产的区别。

## Acceptance Criteria

H-AC1 → H1：从根和 src/HerdDesk.Core 启动路径都能找到 G0、离线 gate、禁止真实写入、批准后实施规则；没有两套冲突正文。
H-AC2 → H2：五工具文档各有入口、派发方式、实际模型与权限核对方法、无工具时回退，未验证项保留 UNVERIFIED。
H-AC3 → H3：真实 C#/Python 规范覆盖依赖方向、帧/epoch、错误脱敏和测试；前端不再呈现 React 模板为已实现规范。
H-AC4 → H4：仅 tracked 文件即可执行手工工作流；不依赖本机绝对路径或整包取消 gitignore。
H-AC5 → H1–H4：文档链接/语义审查、task context 校验通过；五 CLI 可用时记录最小新会话加载结果，不可用时不得声称完全运行对齐。

## Dependencies

激活 H 前先完成书面交接：H 是本轮 backend/frontend spec 的唯一实现者，00-bootstrap-guidelines 只保留已有验收清单并接收 H 证据，禁止另一个 bootstrap 实施会话同时编辑。交接记录写入旧任务 task.json.notes 和 prd.md；旧任务不提前改 completed。backend 规范/代码示例映射到 H-AC3，frontend 的未实现部分继续 pending/deferred，不能用 index 标记替代原本全文件验收。若旧任务仍有并发写入者，先协调停止该写入再激活 H。最终回写等待已批准协议/SDK 子任务结果。

## Out of scope

无产品功能扩展、无新依赖、无真实 herdr/SSH/GUI、无用户全局修改或发布。检查中禁止把合成证据当成产品验收。
