# Implementation plan

禁止在本轮 task.py start；用户批准最新父计划后才逐项进入实施。

## Order and checks

1. 读取父 tests.md 中 publication dry-run 与当前/历史 CI 证据，复查日期、SHA、状态。
2. 修改历史用途说明与必要诊断，补精确输出/拒绝语义回归，禁止调用 --publish。
3. 运行 python -m unittest discover -s tests/python -p test_publish.py -v；python -m unittest discover -s tests/python -p test_repository.py -v；python scripts/validate_repository.py。
4. 在父任务中汇总改造结果和 HD 映射，交规则子任务写正式五工具适用说明。
5. 所有已批准代码变更集成后只跑一次必要 just ci；读取对应 SHA Actions 结果（无远端写授权则只读，未产生新CI不能冒认旧run）。
6. 审查 git diff --check 和变更范围，保留所有 missing acceptance；按用户当时授权完成提交/归档，不由本计划推断 push 授权。

## Dependencies

整体 E 依赖协议、SDK、规则三个子任务输出；部分批准只执行明确授权的相应证据切片，整个 E 不提前完成。README 由文档负责人串行集成，避免冲突。

## Acceptance trace

按本任务 prd.md 的 AC 逐项记录命令、退出码、关键输出及未验证项。子任务检查通过不代表父任务或 G0 产品门禁通过。
