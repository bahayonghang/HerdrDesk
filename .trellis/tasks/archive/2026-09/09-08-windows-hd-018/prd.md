# HD-018 · 断连和崩溃恢复

状态：`planning`。保持原依赖 HD-010、HD-013、HD-016；恢复逻辑扩展这些既有 owner，不创建第二套 Store、lease 或进程状态机。

## Goal

在 RPC、terminal、renderer 或应用生命周期故障后立即把状态标为 stale/readonly，隔离所有旧 epoch completion 和输入，再按“RPC订阅与权威 snapshot 收敛→确认目标仍存在→新 terminal epoch observe→full baseline”的顺序恢复。恢复永不自动启动/升级 herdr daemon，也不恢复旧控制权。

## Confirmed facts

- GUI 只拥有自身 bridge/SSH client，关闭不执行 server stop、不枚举 agent；断线后不重放输入且先只读校准：`docs/plan/docs/03_架构与数据流.md:56-62`。
- ConnectionEpoch 每次重建连接递增，旧 pending response 不应用：`docs/plan/docs/03_架构与数据流.md:80-82`。
- 网络失联必须显示“过期”；错过的瞬态事件不能凭空恢复：`docs/plan/docs/03_架构与数据流.md:84-106`。
- v0.1 假设 daemon 由用户启动；曾连接后 server 消失也不自动复活：`docs/plan/docs/04_技术选型与ADR.md`（第39-45行）。
- 原 AC13/14/15 当前均 `not_run`：`planning/acceptance.json:101-122`；HD-018 交付 P2 本地/通用恢复机制与证据，跨阶段的网络/SSH/产品最终验收由 HD-026 统一汇总。

## Requirements

- R1：HD-010 `DeviceSession` 继续唯一拥有 RPC phase/epoch/Store；HD-016 `ControlLeaseCoordinator` 继续唯一拥有 terminal access/control/input。HD-018 只增加纯 `RecoveryPolicy` 与明确的跨 owner 信号。
- R2：failure taxonomy 至少区分 manual disconnect、app stopping、request EOF、subscription EOF、RPC bridge exit、daemon unreachable、schema incompatible、protocol error、terminal.closed、terminal stdout EOF、terminal client exit、renderer failure 和 cancellation。
- R3：异常 RPC loss 的第一可观察动作是在 actor 中发布 Stale、清 mutation/control capability 并取消当前 epoch effects；retry timer 或新 process 不能先于该 publication。
- R4：epoch rollover 使旧 RPC response/event/snapshot/timer、terminal frame/receipt/ownership proof、renderer ack 和 challenge 全部 no-op；旧对象只能完成清理，不能改变新状态。
- R5：异常 terminal/renderer loss 立即由 HD-016 设置 readonly、`ControlVerified=false`、清 challenge和未开始输入；HD-013 在途 receipt 映射为 NotSent 或 ResultUnknown，bytes 不保留。
- R6：可重试 client connection 使用每 SessionKey 独立的单 timer和指数退避，默认 1、2、4、8、16、30 秒并有可注入 jitter；delay封顶30秒，manual Retry 可提前触发且合并重复点击。
- R7：authentication/host-key（未来 SSH）、schema incompatible、protocol incompatible、explicit disconnect、app stopping 不无限后台 retry；转 AwaitingUser/Offline并显示具体动作。策略不运行 server start、service restart、download 或 upgrade。
- R8：RPC recovery 必须重新创建 request/subscription connection、新 epoch，并完整执行 HD-010 的 subscribe ack→snapshot→dirty reconcile；只有 Store Ready/fresh 和 capability 重新验证后才算 RPC recovered。
- R9：RPC Ready 前不得打开 terminal candidate。Ready 后只对仍可见/选中且在新 Store 中存在的 PaneKey 建一个新 observe transport；不存在的 pane 显示 closed/stale selection，不按旧标题寻找替代。
- R10：terminal recovery 总是新 epoch、Observe、`takeover=false`、`ControlVerified=false`，并等待有效 full frame与 renderer parser-consumed ack；旧 seq、delta、scroll位置和 control lease不能沿用。
- R11：同一 SessionKey 任意时刻最多一个 reconnect effect和一个 retry timer；重复 request/sub EOF、process exit和manual Retry合并，不产生重连风暴或并行 Store writer。
- R12：一设备/session 的 retry不能阻塞其他 actor或 UI dispatcher。本任务只实现本地/P2通用策略；P3 SSH认证矩阵与多设备AC26由 HD-024/026补充。
- R13：首次 snapshot 是新通知基线；recovery 不补发断线期间的历史 done，不把本地旧 projection 当完整事件回放。HD-012 只从新基线后的事件建立通知。
- R14：app stopping latch 一经设置便禁止新 retry/process/renderer创建；先 revoke write和取消 timers，再 dispose 本应用 direct child。晚到 process creation必须立即关闭该 direct child。
- R15：应用 crash 无机会运行清理时，架构仍只能让 OS 关闭本应用 pipe/direct clients；daemon/agent/pane不在应用 job object或 kill allowlist。跨网络/SSH的真实产品存活由 HD-026 最终验收。
- R16：UI 保留最后已知名称作为灰态 context，并显示 Stale/attempt/next retry/AwaitingUser/Retry/Diagnostics；任何可能产生副作用的命令在 Store重新收敛前 disabled。
- R17：recovery state 不持久化 pending input、ControlVerified、takeover challenge、seq、RPC pending request或terminal content；进程重启只从配置和权威 server重建。

## Acceptance criteria

- AC1（R2/R3；原 AC13）：request EOF、subscription EOF、bridge exit、terminal EOF/closed/exit、renderer failure分别得到稳定 cause；每种异常在任何 retry前可观察为 Stale/readonly，而非绿色 Ready。
- AC2（R4/R8）：100次 barrier-controlled reconnect中，epoch N 的每类 completion在 N+1 后均不改变 Store、lease、renderer binding、notification baseline或command outcome。
- AC3（R6/R7/R11）：fake time证明退避为1..30秒+jitter且单 timer；manual Retry合并并提前；incompatible/auth/user disconnect/app stop零后台重试，所有路径零 daemon-start调用。
- AC4（R8-R10；原 AC13 的 P2 贡献）：恢复顺序日志严格为 new RPC→subscribe ack→authoritative convergence→target revalidate→new observe→full frame/renderer ack；从未出现 RecoverControl/takeover/旧seq。
- AC5（R5/R10/R17；原 AC14 的 P2 贡献）：在 enqueue前、writing、flush后及 epoch切换点断开，恢复后的writer capture零旧payload；UI逐项显示 NotSent或ResultUnknown，process/config/state均无input bytes。
- AC6（R9/R13/R16）：pane在断线期间关闭时不按同名重连；首次恢复snapshot不发历史done；Ready前所有control/resource副作用按钮disabled且 stale context明确。
- AC7（R14/R15；原 AC15 的 P2 贡献）：app stop与late-start race只终止owned direct child；fake daemon/agent/pane pid永不进入kill ledger。跨阶段真实GUI/网络/SSH存活由 HD-026最终汇总。
- AC8（evidence boundary）：本任务以 L1 deterministic faults 和本地离线 Windows fake-process integration 完成 P2 交付；可选真实本地 daemon/GUI fault 需获批 disposable environment。未执行的 P3 网络/SSH场景不阻塞本任务完成，作为 HD-026 的 `UNVERIFIED` 输入。

## Out of scope

- 不实现 daemon start/stop/upgrade、Windows service管理、SSH host-key UI、多设备AC26、持久event cursor、terminal history replay或resource mutation retry。
- 不把 process exit 0等同 pane退出，不把网络恢复等同 control恢复，不运行 live herdr/WinUI。

## Blocking gate

HD-010 若没有 epoch-latched full resync，或 HD-013/016 若不能清队列并回 Observe，本任务不得以外围 retry loop绕过。真实故障注入只能在明确授权的隔离资源执行。
