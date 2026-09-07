# P2 常青证据与任务状态回写

状态：planning；本轮只写计划，待用户确认父计划后实施。
父任务：../09-08-evergreen-harness-audit

## Goal

常青维护时保留证据的提交与适用范围，避免历史发布清单和本机工具资产阻碍日常检查。

## Requirements

E1 区分初始 publication bundle 清单与当前 working-tree/CI 状态。
E2 明确 Trellis 改造任务与 HD 产品 backlog/AC 权威关系。
E3 批准项完成后稳定规则进入项目说明，注明适用五工具，运行日志留任务证据。

## Acceptance Criteria

E-AC1 → E1：默认离线发布脚本的用途、失败语义与历史基线清晰；不靠重算旧 SHA 抹掉漂移，不触发 --publish。
E-AC2 → E2：README/项目导航能找到 Trellis 与 HD 对照，产品 AC 不因本计划的离线通过变 passed。
E-AC3 → E3：每项批准改动有结果、适用工具、来源/日期及实际通过/未验证边界；当前 CI 与历史 run 分开。
E-AC4 → E1–E3：python test_publish/test_repository、validate_repository、just ci 通过；审查最终 diff 无私人配置/真实会话数据。

## Dependencies

整体交付依赖协议、SDK、规则三个子任务的验证输出；最后完成。若只批准某个代码项，按父 PRD 明确批准的随附范围仅做该项证据切片，整个 E 保持未完成。README 编辑按文档负责人顺序执行，避免冲突。

## Out of scope

无产品功能扩展、无新依赖、无真实 herdr/SSH/GUI、无用户全局修改或发布。检查中禁止把合成证据当成产品验收。
