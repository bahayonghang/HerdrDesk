# 总体实施与验收顺序

## 2026-09-09 执行快照

HD-001–036 已完成 L1 产品提交、规划提交与独立归档。父任务仍 `planning`，不归档。AC01–AC48 保持 `not_run`。`phase_gate=not_passed`。完整 1.0 未宣称。

规划钉 GitHub v0.9.0 / protocol 22。2026-09-09 将 last-interactor 尺寸、client-local chrome、#3519 detach、dim 缓存、graphics omitted、禁止 `--no-session`、Herdr Cloud 排除写入父 design/ui-blueprint。不新建子任务。不改 `src/`。不 push。live herdr/SSH/WinUI/IME/soak/Publisher 仍缺授权。

需要真机/远端/签名的任务须绑定明确目标与授权。已有 Goal 本地 L1 授权无需重复询问。父任务负责跨任务契约与最终集成审查；不得启动父任务代替子任务。

## Phase 1 规划检查

- [x] 原HD-001–036一一建子任务并填写三件套、JSONL，源AC01–48完整保留且映射owner。
- [x] 逐项检查所引用事实、拟建路径、依赖、错误状态、测试与rollback；不存在seed占位。
- [x] task.py validate全部成员，plan_precheck递归检查；结构通过不替代语义审查。
- [x] 独立trellis_plan_auditor对整个树审查，主线程修订可确认的问题并复查。
- [x] 交付规划与阻塞条件；实施批准另行发生，本轮不更改源产品状态。

实际检查和执行条件见 [research/validation.md](research/validation.md)。以上勾选仅为
本轮规划交付，父PRD产品AC与全部task.json仍保持未通过/规划状态。

## 分波执行（后续批准后）

| 波次 | 子任务 | 可并行范围 | 出口 |
|---|---|---|---|
| G0-A | HD-001、HD-002 | runtime证据与资产/发行决定独立进行 | 基线来源可信、授权决定、明确实验目标 |
| G0-B | HD-003、HD-004 | endpoint/ACL和terminal信号在隔离资源上分工 | Windows/远端基线、可观察的控制/释放语义 |
| G0-C | HD-005→HD-006 | renderer/IME spike后汇总架构与规则适用范围 | 不确定控制/IME问题已解；G0证据被接受后才扩产品范围 |
| P1-A | HD-007→HD-008→HD-009 | 工具链/骨架先建，schema/端口先审再各消费者实现 | 可编译产品边界、真实RPC、Store投影 |
| P1-B | HD-010→HD-011→HD-012 | UI布局研究可先行；完整本地连接/设置依赖已就绪的收敛层 | 可用只读工作台，过期/未知能力准确 |
| P2-A | HD-013→HD-014 | transport先建；renderer按已通过spike产品化 | 长期帧/背压/生命周期，不误判控制 |
| P2-B | HD-015与HD-016 | IME/输入adapter和control policy独立文件，公共契约顺序合入 | 真实IME、控制/释放/接管、无重放 |
| P2-C | HD-017、HD-018→HD-019 | 资源命令和恢复分工，最后整体本地验收 | 可写本地MVP及真实Agent TUI/进程所有权证据 |
| P3-A | HD-020→HD-021→HD-022 | 设备身份→helper部署→远端通道 | 真实受信SSH与透明流 |
| P3-B | HD-023、HD-024→HD-025→HD-026 | 聚合UI与退避独立，预算和多设备验收随后 | 同名pane零串写、设备故障隔离 |
| P4-A | HD-027→HD-028→HD-029 | wire/security先审，服务后UI | 真实列举/传输/冲突结果 |
| P4-B | HD-030→HD-031→HD-032 | 附件意图和缓存界限合入后故障验收 | hash/原子rename、无误删、无自动提交 |
| P5-A | HD-033 与 HD-034 | 性能/可访问性和离线打包准备可独立；最终包验收需同一候选 | L3/L4和安装/更新/回退证据 |
| P5-B | HD-035→HD-036 | 独立安全审查后文档/证据归档 | 完整1.0候选可审核；明确授权后才外部发布 |

原依赖与细化条件见task-map.md及各子任务implement。测试工程统一使用research/test-layout.md的入口，首次建立时必须注册solution、just与CI并验证测试发现。G0不得靠放宽安全/验收退化“通过”；若Windows控制或IME无法成立，停止该完整目标晋级并给出可复现上游问题/范围决策，不能悄悄交只读版本称完整。

## 单个子任务执行协议

1. 读取当前仓库状态和源task/AC；源状态可能变化，不能只读本次snapshot。
2. 确认该任务方案获批、前置产物/真实证据存在，技术选择与父契约一致；需要改变公共契约时先让消费者审查。
3. `python .trellis/scripts/task.py start <实际子任务目录>` 仅在批准后调用。本行是未来指引，本轮未执行。
4. 按implement.jsonl加载真实spec/research；原G0 frontend模板不是WinUI契约，HD-006/007/011按批准范围更新对应规则和spec。
5. 实现最小完整增量、focused regressions和错误/取消路径；独占文件分派给实现代理，独立check代理核查。
6. 先跑最小相关门，再跑该任务要求的完整门和真实环境矩阵；已通过且无新问题不无谓重复。
7. 写清代码SHA、测试命令与版本、平台、实际结果、证据路径和未测项。原AC全条款未满足时子任务仅报告贡献。
8. 按已有授权完成本地交付/文档/spec收口；归档前检查Trellis脚本是否会auto-commit。本轮没有commit或归档授权。
9. 外部发布、远端配置、helper部署、输入/接管与签名服务依照明确目标范围操作；不把“代码获批”扩大为真实用户会话写入。

## 验证命令与适用范围

本轮现有命令：
- `python .trellis/scripts/task.py validate .trellis/tasks/09-08-windows-desktop-full`，并逐个验证children。
- `python C:/Users/lyh/.agents/skills/trellis-plan-review/scripts/plan_precheck.py .trellis/tasks/09-08-windows-desktop-full --include-descendants`。
- `python scripts/validate_repository.py` 只检查原仓库结构；不会证明任务设计正确。
- 一次性检查task-map/acceptance-map与源JSON匹配、依赖DAG、链接/引用、全部planning及没有产品diff；不把判断固化成新规则引擎。

后续现有 `just ci` 必须按HD-007扩展到引入的语言/平台，并明确Windows专属项目的真实门。未来的Integration/E2E/packaging脚本只有所属任务创建并验证后才能调用；计划中的命令不得记成执行结果。

## 父任务完成条件

所有36子任务达到各自完成标准，原48项在承诺平台范围内有同一release候选的完整证据，跨任务公共契约一致、源码与文档/支持矩阵一致、安装/更新/安全/soak无发布阻断，完成用户授权范围内的交付。未获外部发布授权时只可报告“发布候选准备完成”，不可报告已经发布；完整产品目标是否包含外部发布由该次授权决定。

EP扩展若后续被用户升为必做，则先创建对应真实Trellis子任务并增补映射/预算/验收，重新审阅扩大的计划；不得隐式按1.0完成标准把新增必做项排除。
