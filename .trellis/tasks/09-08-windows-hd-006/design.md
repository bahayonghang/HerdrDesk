# HD-006 设计

## ADR 决策表

每项包含问题、输入证据、决定、允许/禁止调用者、失败态、撤销策略、责任模块、
验证任务和 spec 更新触发条件。最低包含：通信平面、ID/epoch、control authority、
renderer IPC、process ownership、日志脱敏、endpoint discovery、依赖准入和
filebridge capability。

## 规则与 spec 边界

AGENTS 是项目事实，backend spec 只描述当前 G0 C#/Python；frontend index 明确是
deferred，不能作为 WinUI 合同：`.trellis/spec/frontend/index.md:1-31`。HD-006 负责
后续获批实施时的项目规则同步，先核实证据再更新对应文件；本轮仅记录范围。

## 撤销

任何未验证 control 信号、endpoint ACL 或 renderer IME 结论都使关联能力降级为
observe/explicit configuration；ADR 不把 unknown 改写为 success。

## 必须定稿的 ADR 清单

| ADR | 决策 | 输入任务 | 后续 owner |
|---|---|---|---|
| 原 ADR-001 独立实现 | 保持独立代码/资产来源与许可准入 | HD-002 | HD-007/034/035 |
| 原 ADR-002 renderer | 固定 origin、消息边界、IME 与 raw-stream 路线 | HD-005 | HD-014/015 |
| 原 ADR-003 RPC/terminal | API plane 与 terminal 分离，显式 endpoint/ACL | HD-003/004 | HD-008/013 |
| 原 ADR-004 SSH exec | argv、远端引用、透明 stdio 与认证边界 | HD-001/003 | HD-020/022 |
| 原 ADR-005 进程所有权 | 自有连接退出，daemon/agent 存活，不自动复活 | HD-004 | HD-013/019 |
| 原 ADR-006 主动授权 | 默认 observe、控制证明、takeover/release、无重放 | HD-004/005 | HD-016/018 |
| 原 ADR-007 模块化单体 | identity/epoch、依赖与窄 filebridge 职责 | 现有 Contracts/plan | HD-009/023/027/028 |

保留 `docs/plan/docs/04_技术选型与ADR.md` 原编号与语义。拟建
`docs/adr/approved-baseline.md` 记录每条决定的实际证据、采用状态与子契约；
不把原编号重用为不同决定，不创建平行ADR体系。

## 从 G0 到 P1 的合法转换

当前 AGENTS G0 hard prohibition 是本轮离线 gate 的安全范围，不是永久产品禁令。
本轮不解除它。用户批准本任务实施、HD-001 至 005 的 G0 目标证据达到、
ADR 记录剩余风险和撤销路径后，由 HD-006 同一任务内完成阶段规则同步。该变更必须
同时更新 `AGENTS.md`、`.trellis/spec/backend/index.md`、相关 nested `CLAUDE.md`、
`implementation/status.json` 和 `planning/acceptance.json`，并以实际执行证据而非
文档存在作为状态依据。同步 `.trellis/spec/frontend/index.md` 的后续适用边界，
详细 WinUI 规范由 HD-007/011 根据实际骨架填充，不把旧模板整体标为完成。
G0 门检查基线/spike/准入证据；AC02/44 最终产品/分发复核仍归 HD-035，
不能用未来 P5 结果构成 G0→P1 的循环依赖。任一 G0 门未闭合则保持原阶段。
