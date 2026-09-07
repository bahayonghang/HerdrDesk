# P1 SDK 检查与安装授权边界

状态：planning；本轮只写计划，待用户确认父计划后实施。
父任务：../09-08-evergreen-harness-audit

## Goal

让工具链检查在无安装/全局修改授权时安全运行；SDK 版本只以 global.json 为准。

## Requirements

S1 默认 setup 只验证 Python、SDK 及实际命令解析，不安装或改 User 环境。
S2 保留显式 opt-in 安装/持久化入口，但必须先读 global.json 和校验所有参数。
S3 README/just 注释精确描述副作用和使用方式。

## Acceptance Criteria

S-AC1 → S1：正常、SDK 缺失、版本不匹配三种默认运行均不调用 winget、不写用户环境；失败返回非零。
S-AC2 → S2：测试使用 stub/fake host 证明只有显式安装/持久化分支可写；前置校验失败时无副作用。
S-AC3 → S2/S3：不再硬编码第二个 SDK 版本权威；README 与 just setup 行为一致。
S-AC4 → S1–S3：现有 just ci 通过，测试不在真实用户环境执行安装/持久化。

## Dependencies

无前置代码依赖，可与协议子任务并行。README 的实际编辑安排在规则子任务之后或交由单一文档负责人，避免覆盖。

## Out of scope

无产品功能扩展、无新依赖、无真实 herdr/SSH/GUI、无用户全局修改或发布。检查中禁止把合成证据当成产品验收。
