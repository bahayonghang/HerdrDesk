# HD-024 技术设计

## 边界与所有权

Infrastructure 将 HD-020/022 的可信阶段结果映射为通用 failure kind；Core 只计算 retry/block state，不引用 `ssh.exe`、host、known_hosts 或 credential 实现。
HD-010/018 已规定每个完整 SessionKey 一个 DeviceSession actor；它继续独占该 session 的 timer、attempt、generation、epoch 与 Store mutation。HD-024 只扩展其 recovery policy 和 SSH failure classifier，不建立 reconnect coordinator；timer callback 仍必须回到 owner actor。现有输入策略已要求 pane+epoch+ControlVerified：`src/HerdDesk.Core/InputPolicy.cs:13-31`。
App 呈现 DeviceSession 的只读状态并发送带完整 SessionKey 的类型化 Cancel/Retry/Edit/Review intent，不能修改 attempt/revision、fan-out 到其他 session 或直接启动进程。

## 精确拟建/修改文件

- `src/HerdDesk.Core/Recovery/RecoveryFailure.cs`、`RecoveryDecision.cs` 与 `RecoveryPolicy.cs`：扩展 HD-018 的既有分类/决策以表达远端 transient、auth、host-key 与 manual block；不复制 taxonomy。
- `src/HerdDesk.Core/Recovery/RecoveryBackoff.cs`：为远端 SessionKey 扩展纯 equal-jitter 计算；依赖注入 RNG，不含 SSH 或 timer ownership。
- `src/HerdDesk.Core/DeviceSessions/DeviceSession.cs`：消费扩展 policy，并继续以完整 SessionKey 唯一拥有 timer/generation/single in-flight；不新增 coordinator。
- `src/HerdDesk.Infrastructure/Ssh/SshFailureClassifier.cs`：把已知 preflight/channel outcome 映射为通用 kind；unknown fail-closed。
- `src/HerdDesk.Infrastructure/Ssh/SshRecoveryBlockStore.cs`：只持久 `DeviceId+blocked ProfileRevision` 层 auth/host-key/manual block 的 kind 与相关 evidence revision，版本化 JSON、文件锁与 atomic replace；不存 timer/attempt。
- `src/HerdDesk.App/Devices/DeviceConnectionStatusView.xaml` 与 `DeviceConnectionStatusView.xaml.cs`：倒计时、阻断原因和可访问动作。
- `src/HerdDesk.App/Devices/DeviceConnectionStatusViewModel.cs`：Cancel/Retry/Edit/Review intent 与状态格式化。
- `tests/Unit/HerdDesk.Core.Tests/Connections/`、`tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/Recovery/`、`tests/Unit/HerdDesk.App.Tests/Devices/`、`tests/Integration.Ssh/Auth/`：下述验证。

以上均为未来路径，不附行号。Core 修改直接并入 HD-010/018 的单一 DeviceSession/recovery owner；禁止平行 actor、第二 timer 或第二份 recovery state。

## 类型与分类

HD-018 的 `RecoveryFailure` cause/retry class 扩展为至少可表达 `TransientNetwork`、`TransientTransport`、`AuthenticationBlocked`、`UnsupportedAuthentication`、`HostKeyUnknown`、`HostKeyChanged`、`DaemonUnavailable`、`ProtocolPollution`、`Incompatible`、`Cancelled`、`UnknownBlocked`。
只有前两种返回 `AutoRetry`。Cancelled 返回 `Stopped`；auth/host-key/unsupported 返回对应 revision gate；daemon/protocol/incompatible/unknown 返回 `ManualBlocked`。
classifier 优先使用 HD-020 host assessment 和认证阶段、HD-022 typed outcome/exit stage。原始或本地化 stderr 只能作为有界诊断，不可单独把 Unknown 变成 AutoRetry。
profile-level block record 含 `DeviceId`、被阻断的 `ProfileRevision`、kind，以及相关 `CredentialRevision` 或 `KnownHostRevision`；revision 是单调 token/内容 hash，不含秘密。label/theme/session 排序等非连接字段不得增加相关 revision。
`CredentialRevision` 只在 identity/auth 引用变化，或用户显式重扫 agent 后观察到不同的公钥 fingerprint 集合时变化；集合仅存排序后 hash，不存 key material。

## 精确退避公式

`n` 是自上次完整 RPC Ready 后的零基连续瞬态失败序号。`cap(n) = min(30s, 1s × 2^min(n+1, 5))`。
注入 `IRetryRandom.NextUnitInterval()` 返回 `u ∈ [0,1)`；`delay(n,u) = clamp(1s, 30s, cap(n)/2 + u × cap(n)/2)`。
因此 n=0 为 `[1,2)`，n=1 `[2,4)`，n=2 `[4,8)`，n=3 `[8,16)`，n≥4 `[15,30)`；实现用整数 tick 与 checked/saturating 算术，边界最终 clamp。
生产 RNG 每次调度独立取值；测试注入固定序列。clock 使用 `TimeProvider`，禁止直接散布 `Task.Delay`/`DateTime.UtcNow`。

## 每 SessionKey 状态机与并发

DeviceSession 状态扩展为 `Stopped`、`Connecting`、`Ready`、`WaitingRetry`、`BlockedCredential`、`BlockedHostKey`、`ManualBlocked`。每个 actor 已由完整 SessionKey 唯一标识，并在自身状态内保存 attempt、generation、dueAt、timer、inFlight CTS 和当前 block snapshot；不存在按 DeviceId 聚合这些字段的 map。
瞬态失败在 owner actor 内递增 generation、取消自己的旧 timer、计算一次 delay 并注册 callback。callback 携带完整 SessionKey+generation，回到同一 actor 后二次比较；stale、disabled、blocked、Ready 或 in-flight 时不启动。
同一 SessionKey 同时至多一次 connect attempt；同设备其他 session 和其他设备无共享 await/lock。keyed Cancel、session/device disable/delete、block 转换、成功 Ready 都先在目标 actor 递增 generation，再 dispose 自己的 timer/cancel attempt。
只有 HD-022 request+event 完成同步并报告 Ready 才 reset `n=0`。短暂 child alive、TCP/auth 成功或 terminal 首帧都不 reset。

## 阻断、revision 与显式恢复

AuthenticationBlocked 按 DeviceId 持久 `(kind, blockedProfileRevision, credentialRevision)`，HostKey block 持久 `(kind, blockedProfileRevision, knownHostRevision)`；它们是该 DeviceProfile 各 sessions 共同查询的 gate，不是 timer/attempt owner，store 不持久 dueAt、stderr 或输入。
App 启动恢复 block。仅 revision 变化不会自动连接：用户对完整 SessionKey 点击 Retry 后，该 DeviceSession 重读 profile/trust assessment；相关 revision 必须不同且认证模式受支持/host assessment 为 Trusted，才发起该 session 一次立即尝试。动作不 fan-out，其他 session 保持各自 actor 状态。
该尝试失败时产生新的 block snapshot；成功进入 Ready 清持久 block。相同 revision、仅网络 change、仅 label 修改或普通 Retry 均保持 blocked。
瞬态 WaitingRetry 可由带完整 SessionKey 的 Retry now 或可信 network-change signal 提前一次；两者只让目标 actor 取消自己的 timer/换 generation，若已有 in-flight 则幂等忽略。

## 重连数据流

有效 attempt 调 HD-022 为同一 SessionKey 创建全新 `RemoteSessionTransportSet`/epoch；先 request+event subscribe/snapshot/dirty 收敛，后按当前可见 pane observe。控制状态回 Observing，`ControlVerified=false`。
旧 epoch pending/event/frame 由各 owner 丢弃；断线时未发送队列先清空并发布 UI-safe “input_not_replayed”。本任务不保存或恢复 input bytes。
失败只更新目标 SessionKey 的 actor 状态；profile-level auth/host block 可使同 profile 的其他 actors 显示 AwaitingUser，但不取消或启动它们的 timer/attempt。其他 session/device 的 connection 不受影响；App 聚合只接收状态快照。

## 错误、UI 与恢复

公共 code 至少有 `reconnect_waiting`、`authentication_action_required`、`host_key_review_required`、`authentication_unsupported`、`connection_manual_retry_required`、`reconnect_cancelled`；显示参数只有秒数和脱敏 ID。
UI countdown 来自各 DeviceSession dueAt 与注入 clock 的只读投影，不通过每秒持久写；按钮在目标 SessionKey in-flight 时禁用。blocked host 显示 Review Host Key，blocked auth 显示 Edit Credentials，ManualBlocked 显示诊断/Retry。
block store 写失败时采用更保守的内存 block 并显示持久化失败；不得因无法保存而自动 retry。回滚关闭 auto-retry，所有远端设备保留明确手动入口。

## 验证设计

L1 用 fake TimeProvider/RNG/connector/store 精确推进时间，覆盖每 SessionKey generation/并发、profile-level revision 与 crash recovery；敏感字段用哨兵查泄漏。
L2 用隔离 SSH 账号注入网络拒绝、auth deny、host-key rotation、banner/protocol，以及同设备 A1/A2 和设备 B 并发。L3 人工检查 keyed 倒计时、block 页面、重启恢复、键盘/读屏；由 HD-026 引用形成最终 AC22/26。
