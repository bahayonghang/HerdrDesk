# HD-010 · 订阅与收敛策略

状态：`planning`。保持原依赖 HD-009；本任务不实现通知、搜索或 terminal transport。

## Goal

实现每个 SessionKey 一个串行 `DeviceSession` actor，以“订阅确认→snapshot→dirty 权威重读”的流程维护 HD-009 Store；在 snapshot/event 交错、连接失败、事件风暴和取消下最终收敛，且任何旧 epoch completion 都不能回写当前状态。

## Confirmed facts

- herdr 是运行状态唯一权威，本地 Store 可丢弃；网络/解码不在 UI 线程，Store mutation 由单 actor 串行：`docs/plan/docs/03_架构与数据流.md:56-82`。
- 不能假设 event 有 replay sequence 或 snapshot revision；v0.9.0 lifecycle 订阅从请求被接受时开始、不回放保留历史（herdr socket-api / CHANGELOG #1270）。事件只作为 invalidation，初始策略是 250 ms 合并、5 s 校准、断线全量同步：`docs/plan/docs/03_架构与数据流.md:84-106`。
- 首次 snapshot 是基线，不补发历史 done notification；网络失联必须显示 stale：`docs/plan/docs/03_架构与数据流.md:104-106`。
- 原 AC12 要求 snapshot/订阅交错后最终收敛且旧 epoch 不回写：`planning/acceptance.json:93-98`；风险 R08 的既定 mitigation 是 invalidation+权威重读：`planning/risks.json:61-66`。

## Requirements

- R1：actor 的唯一 identity 是完整 SessionKey；一个 actor 独占该 session 的 ConnectionPhase、ConnectionEpoch、dirty set、request/subscription handles 与 Store 写权。
- R2：每次 connect/reconnect 生成严格递增的新 epoch 和 epoch cancellation source；所有 effect completion、timer、event、snapshot 都携带 epoch/operation id，actor 在 mutation 前校验。
- R3：同步顺序固定为建立独立 subscription、收到成功 ack、开始接收 invalidation、请求 snapshot、原子安装、对期间 dirty 再读；确认前不得宣告 Ready。
- R4：event 不直接 patch Store。mapper 能确定实体且 schema 有 verified getter 时做定向权威 read；未知 event/实体/能力或 graph invariant 风险改做完整 snapshot。
- R5：dirty 合并与周期校准使用可注入 `TimeProvider`/options；事件持续到来时保持 Synchronizing/Ready-with-refresh，不丢 invalidation、不忙循环、不阻塞 actor。
- R6：mailbox、effect 和 UI state publication 都有界。协议事件不能静默丢弃；若 downstream 无法跟上则失败当前 subscription、标 stale 并要求全量 resync。UI 可合并中间 projection，只保证最新 immutable snapshot。
- R7：request EOF、subscription EOF、protocol error、schema incompatible、caller disconnect 分开；异常断开立即使 Store stale、移除 mutation capability 并取消当前 effects。
- R8：本任务不重试任何 mutation。snapshot/read 是只读，可按明确策略重取；重连/退避及 terminal re-observe 由 HD-018 补齐。
- R9：App/ViewModel 只订阅 typed `DeviceSessionState`，不访问 raw JSON、pending map、Process 或 actor mailbox；首次基线不产生 HD-012 通知。

## Acceptance criteria

- AC1（R3/R4；原 AC12）：对 create/close/status event 分别安排在 subscribe ack 前后、snapshot 请求前后和安装期间，最终 graph 与权威 fake server snapshot 等价。
- AC2（R2；原 AC12）：epoch N 的 snapshot/event/timer/error 在 N+1 建立后全部被忽略，Store revision/graph/phase 不被旧 completion 改变。
- AC3（R4）：未知 event 和无 getter 的 dirty entity 触发一次 full snapshot；已验证 getter 只读取目标并保持 parent invariants，失败自动升级为 full snapshot而非局部 patch。
- AC4（R5/R6）：10,000-event burst 合并后内存受 mailbox/transport budget 限制；无 invalidation 被当作成功处理后丢弃，quiet period 后收敛且无 timer/effect 泄漏。
- AC5（R7）：request 与 subscription 任一意外 EOF 立即发布 Stale 和只读 capability；不保持绿色 Ready、不把 EOF 统一成 named-pipe timeout。
- AC6（R8/R9）：actor 使用 HD-008/009 fake ports 完成 L1 单测，无 WinUI/SSH/live herdr；baseline install 不调用 notification sink。
- AC7（原 AC12 最终 owner）：本任务同时完成 L1 race model 与基于 HD-008 真实 bridge/HD-009 actual schema 的隔离 L2 交错，证明 create/close/status 后最终投影等于权威 snapshot且旧epoch不回写。HD-019只在跨UI候选上复跑，不是AC12首次闭合前提。

## Out of scope

- 不提供 event replay/exactly-once 承诺，不建持久 event cursor/SQLite，不实现通知 dedupe、UI search、resource mutation、control、terminal 或 SSH reconnect。
- 本任务不运行 Windows UI；实际 method/filter/getter 必须来自 HD-009 schema profile，不按名称猜。L2若需产生create/close/status，实施前必须明确批准可丢弃session、允许的mutation和清理步骤。

## Blocking gate

HD-009 未产出 typed decoder/immutable Store/capability profile，或 HD-008 未证明 subscribe ack 形态时不得实施。无 getter 的情况已由 full snapshot 降级解决，不是新增用户决策。
