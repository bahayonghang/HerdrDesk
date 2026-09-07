# 待批准实施计划

本文件描述批准后的动作，本轮不执行 task.py start、代码修改或正式回写。

| 顺序 | 优先级/子任务 | 要改的核心文件 | 必须通过的检查 |
|---|---|---|---|
| 1 | P0 protocol-failure-contract | TerminalFrameParser.cs、C# Program.cs、最小共同 fixture、test_protocol.py；确有共同语义需要才改 protocol.py | type/属性名/closed.reason 三种 surrogate 红→绿；精确异常码+同实例下一帧拒绝；合法 Unicode；C# build/smoke、Python protocol、just ci |
| 2 | P1 sdk-setup-boundary | Invoke-HerdDeskDotnetSetup.ps1、justfile、README、test_setup.py | stub 验证默认不安装/不写 User、失败无副作用、global.json 单一版本源；Windows 不全 skip；just ci |
| 3 | P1 harness-context-alignment | AGENTS、CLAUDE、docs/harness-workflows.md、.trellis/workflow.md、backend specs/frontend index、.gitignore 注释 | 路径/链接/tracked 边界；语义审查无冲突；context validate；五工具最小新会话题单及实际模型/权限证据 |
| 4 | P2 evergreen-evidence-closeout | docs/publication.md、README、publish_github.py 必要诊断、test_publish.py、planning/tasks 索引 | publish/repository 回归、结构检查、集成 just ci、历史/current SHA 区分、正式回写与隐私 diff 审查 |

上表核心文件的完整路径、具体断言、依赖和所有权见各子任务 design.md / implement.md。第1/2项可独立并行，第3项框架可并行但最终回写要等第1/2项，第4项最后集成。

## 本轮审查交付检查

- [x] 结构/关键代码、五工具规则与官方能力已审查。
- [x] 现有 just ci 已运行，全绿；历史失败与当前 publication 失败已记录。
- [x] 未覆盖协议异常有定向复现，未修代码。
- [x] 父子任务创建并保持 planning。
- [x] 真实 context JSONL、5个任务校验、父子关联与planning状态检查；独立计划复核修订后 PASS。
- [ ] 用户批准最新计划及其明确范围（本轮仅交付待批准计划）。

## 批准后闭环

每项：确定授权 → start 对应子任务 → 红回归/最小修改 → 必要检查 → 强模型审查 → 稳定结论回写。
规则子任务 start 前须先完成 bootstrap 的所有权交接。默认申请四项整体批准；若用户选择部分项，审批清单同时包含该项必要 H 回写/E 证据切片，保留其余子任务 planning，并不宣称父任务全部完成。
完成一次必要集成 just ci 后不机械扩大测试；新失败才追踪。
本轮未获远端写/安装/用户全局设置授权。对应 SHA 无 Actions run 就写未验证，不触发发布来补证据。
最终报告通过、失败、跳过和缺失证据；当前 G0 不因本计划完成而放行。
