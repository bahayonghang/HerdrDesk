# HD-016 · 控制权状态机

状态：`planning`。保持原依赖 HD-013、HD-014；本任务只负责 terminal lease、takeover 授权与输入 gate，resource CRUD 仍归 HD-017。

## Goal

实现以 PaneKey、current projection epoch、terminal binding epoch 和 control attempt 为边界的 `ControlLeaseCoordinator`。默认观察；普通 control request 永不携 takeover；仅在可信 busy 证据后接受一次性目标确认。只有适配器证明当前 attempt 拥有输入权且 renderer 完成该 binding 的 full baseline 后，才允许输入。

## Confirmed facts

- 现有稳定状态只有 `Disconnected / Observing / Acquiring / Controlling / Unknown`，`ControlVerified` 默认 false：`src/HerdDesk.Contracts/TerminalModels.cs:9-23`。
- 当前 `InputPolicy` 已拒绝 wrong pane、stale epoch、非 Controlling、未验证控制和 EmulatorReply：`src/HerdDesk.Core/InputPolicy.cs:13-31`。
- 上游 control stdio 无逐输入 ACK，不能用首帧或进程存活推断授权；不清楚时必须停在 Acquiring/Unknown：`docs/plan/docs/03_架构与数据流.md:108-123`。
- 默认观察、主动 control 不带 takeover、自动重连回观察是既定 ADR：`docs/plan/docs/04_技术选型与ADR.md`（第41-46行）。
- 原 AC07/14/16 均为 `not_run`：`planning/acceptance.json:53-58`、`planning/acceptance.json:109-129`。

## Requirements

- R1：coordinator 是每个可见 pane terminal lease 的唯一状态写入者；所有 command/event 带 PaneKey、projection epoch、terminal epoch、attempt id/lease generation，迟到项 mutation 前重验。
- R2：`busy`、`rejected` 和 `cancelled` 是 control attempt outcome，不新增 `TerminalAccess` 枚举值。busy 后保持当前 observer 与 Observing，只通过独立 outcome 呈现。
- R3：打开/选择 pane 只建立 observe transport，renderer 强制只读。focus、first frame、valid process、input write receipt 均不能自动触发 control request 或 verified。
- R4：用户点击 RequestControl 时重新读取 HD-010 Store，校验完整 target 存在、fresh、capability 与当前 observe binding；状态转 Acquiring，candidate control transport 固定 `takeover=false`。
- R5：Acquiring 最多并存当前 observe 与一个 control candidate。当前 observe 继续渲染；candidate frame 有界暂存，只有 ownership proof 通过才可 promote，不能把两条流混入同一 renderer epoch。
- R6：ownership proof 必须来自 HD-013 的 trusted classifier，且绑定实际 binary profile、PaneKey、candidate epoch、attempt id 和 no-takeover/takeover mode。proof 缺失、版本不符或竞态时为 Unknown。
- R7：promotion 需要 ownership proof、candidate 首个 full baseline、renderer 对该 binding 的 parser-consumed ack、Store target 仍 fresh 四者同时成立；随后才设 Controlling + `ControlVerified=true` 和 renderer writable。
- R8：busy evidence 只有在当前 no-takeover attempt 明确分类时生成一次性 `TakeoverChallenge`；token 绑定 target、projection epoch、observe binding epoch、busy evidence id 和 displayed breadcrumb，不持久化、不记录。
- R9：只有明确用户确认该 challenge 才启动 `takeover=true` candidate。token 使用一次即销毁；target/selection/epoch/freshness/busy evidence/attempt 任一变化自动失效，不提供 always-takeover 或自动确认。
- R10：Unknown/rejected/cancelled candidate 立即停止可写可能性并回到既有 observe；candidate 已可能取得但无法验证时先尽力 release 该连接，再关闭 direct client，绝不启用输入。
- R11：`TerminalInputCoordinator` 对每条 renderer input 在 trusted host 侧重取 active binding、Store target、lease generation、TerminalAccess 和 ControlVerified，再调用 `InputPolicy`；JS 自报 identity 不能作为授权。
- R12：input、server resize、server scroll 仅在 current verified control lease 下发送；observe 状态的 local renderer resize/selection 不转发上游。`EmulatorReply` 保持独立未审计通道并默认拒绝。
- R13：input enqueue 与 lease transition 在 coordinator actor 内排序。失权/EOF/release/epoch change 先把 renderer 设只读并递增 lease generation，再关闭 transport，使 HD-013 将未开始项标 NotSent、在途项标 Unknown。
- R14：任何 NotSent 不重试；`WrittenUnacknowledged` 或 `UnknownAfterDisconnect` 在断连上下文呈现 ResultUnknown，也不自动 replay。input bytes 不写 Store、配置、诊断或 recovery state。
- R15：Release 命令幂等：先撤销 write gate，再调用当前 control transport release 一次、关闭 candidate/current transport，并建立 fresh observe binding；不能在原 control epoch 上重新启用。
- R16：异常 transport loss 立即进入 Disconnected/Unknown 且只读；HD-018 只可请求 observe recovery，不能恢复 Controlling、ControlVerified、challenge 或 queued input。
- R17：UI 显示完整 device/session/workspace/pane breadcrumb、access、busy/rejected/unknown 和 pending input outcome；takeover 对话框没有全局跳过选项，并可由键盘/screen reader操作。
- R18：本任务不开放 generic terminal command、shell executor、server stop、agent approval/bypass 或 workspace/agent create/rename/close；HD-017 的 capability+confirmation 不能借 terminal lease 代替。

## Acceptance criteria

- AC1（R1-R3）：transition table 覆盖所有 state/event；打开、切 pane、focus、首帧和正常 process 都只到 Observing，`ControlVerified=false`。
- AC2（R4-R7）：no-takeover fake attempt 只有绑定的 trusted proof+full baseline+renderer ack+fresh target 才到 Controlling；每个缺失/迟到组合保持 read-only。
- AC3（R2/R8/R9；原 AC07 贡献）：第二 controller 收到 Busy 时不 promote；没有 token 或 token stale/reused/wrong-target 时零 takeover transport。有效确认恰好创建一次 takeover candidate。
- AC4（R5/R7）：acquire 期间 observe frame 继续单流渲染；candidate buffer 有界且 promote 时以自己的 full frame 重绑，旧 observe/candidate late frame 不进入新 epoch。
- AC5（R11-R14；原 AC14 贡献）：input 与 release/EOF/epoch rollover 的 barrier tests 中每个 payload为 NotSent 或 ResultUnknown，断线恢复后 fake writer capture 无重复。
- AC6（R13/R15/R16）：release/失权先 readonly 后清 queue，并只建立新 observe epoch；fresh observe full baseline 前不显示 Controlling、不接受输入。
- AC7（R17/R18；原 AC16 最终 owner）：默认路径无 takeover/approval bypass；busy 对话框显示固定 target，选择切换使确认失效；public API 无 raw argv/RPC/resource command，并以L1+获批本地L3汇总完整主动授权证据。
- AC8（最终证据边界）：L1 fake classifier/transport/renderer 证明状态机；原 AC07 的 observer/controller互斥与旧controller停写交HD-019汇总，AC14无输入重放交HD-026汇总。缺少各自真实证据时对应原AC保持 `not_run`。

## Out of scope

- 不解析 raw stderr/stdout，不拥有 Process、不渲染 ANSI、不保存 terminal content；分别由 HD-013/014 提供受限 port。
- 不关闭 pane、workspace、agent 或 daemon；不把 release receipt 当上游已释放的 ACK。

## Blocking gate

HD-004/013 若不能给出可信 ownership/busy classifier，则 control/takeover capability 保持 disabled 或 Unknown/read-only；不得用“功能必须完成”为由放松 `ControlVerified`。HD-014 未提供 read-only/bind/ack contract 时不得启用输入。
