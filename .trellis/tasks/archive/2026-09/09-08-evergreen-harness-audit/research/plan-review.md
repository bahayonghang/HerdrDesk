# 独立计划复核与修订

复核角色：trellis_plan_auditor（强模型、只读），2026-09-08。初审结论 AMENDMENTS；修订后定向复核 PASS，四项均闭环，无残留计划阻断，可提交用户审批。此结论不代表实施获批或修复已通过测试。

| 发现 | 修订 |
|---|---|
| P0 漏 terminal.closed.reason.GetString | 主线程补充定向复现；P-AC1/P-AC5 纳入 type/name/reason 三案例和所有字符串 materialization 点，design/implement 同步 |
| H 与既有 bootstrap 同拥有 specs | H 激活前在旧任务 notes/PRD 明确 H 唯一实施、旧任务验收，核对并发写入；未完成 frontend 不归档 |
| PATH stub 不能拦截 User 静态 setter | 指定可 dot-source 主流程、Install-PinnedSdk 与 Set-UserDotnetEnvironment 两个 fake sink；默认/前置失败0调用，opt-in也只到fake |
| 按项批准缺正式回写依赖 | 明确必要 H 文档/E 证据切片必须随附批准，未批改造不执行，父和整个 H/E 不提前完成 |

独立审查认可 E1 的历史清单语义：保留当前树历史核验预期 exit 2，日常 just ci 绿，不刷新旧 hash。

## reason 路径补充证据

主线程在已编译 DLL 的独立 PowerShell 进程调用 Parse，输入：

    {"type":"terminal.closed","reason":"\uD800"}

捕获内层异常后，对同一实例立即输入证据文件中相同合法首帧，实际输出：

    reason=System.InvalidOperationException
    then_valid=accepted

此为新增定向复现，未改源码或重复运行整套测试。原 test-and-workflow-evidence.md 的 type/name 六行证据继续有效；本页补足 :48 的第三个调用点。
