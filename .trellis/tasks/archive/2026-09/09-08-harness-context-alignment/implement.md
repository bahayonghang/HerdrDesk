# Implementation plan

禁止在本轮 task.py start；用户批准最新父计划后才逐项进入实施。

## Order and checks

1. 在 task.py start H 前按 prd.md 完成 bootstrap 文件所有权书面交接并确认无并发 spec 写入，再依据父 research/audit.md 和 harness-matrix.md 逐条消除规则缺失；先更新 AGENTS/CLAUDE 共用入口。
2. 使用当前源码与测试作为规范证据，清除当前实际范围的占位模板，不为未实现产品编造规则。
3. 将各工具派发、权限和低成本模型前提写入 docs/harness-workflows.md；不硬编码自动验证器代替语义审查。
4. 在不启动模型的静态检查中核对链接、tracked 依赖、工具名/命令差异和 JSONL 路径。
5. 准备同一新会话题单：列当前 G0/非目标/just ci/smoke方式/禁用真实herdr写入/计划批准/子任务路径/模型和工具权限。对每工具记录 CLI 版本、实际加载文件、上下文和结果；执行需要可用客户端与账号，缺失保持 UNVERIFIED。
6. 强模型逐条审查题单与官方文档，不因目录存在宣布通过；运行 task.py validate；最终代码门禁沿用其他子任务 just ci 结果。

## Dependencies

H 唯一实施 spec；bootstrap 仅接收验收证据且未满足的 frontend 清单继续 pending。框架可先完成，最终回写等待已批准协议/SDK 子任务结果；交接必须先于 H 激活。

## Acceptance trace

按本任务 prd.md 的 AC 逐项记录命令、退出码、关键输出及未验证项。子任务检查通过不代表父任务或 G0 产品门禁通过。
