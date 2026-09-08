# HD-024 · 认证异常与重连退避

## 目标与用户价值

让每个远端 SessionKey 在瞬态网络故障后有界、错峰地恢复，同时在认证失败、unknown/changed host key 或未支持认证方式下立即停止后台尝试并给出可操作入口。
恢复不能阻塞同设备其他 session 或其他设备、重放输入或用反复提示消耗凭据/账号锁定次数。

## 已确认事实

- 原任务处于 P3，依赖 HD-022，源范围是 host-key 阻断和密码/MFA 明确不支持或独立 provider：`tasks/HD-024.md:3-17`。
- 原 AC22 包含 unknown host 人工核验、changed 阻断和密码隐私；原 AC26 包含设备隔离、指数退避+jitter、认证无无限后台重试：`tasks/HD-024.md:19-22`。
- 既有 plan 明确每设备退避 1–30 秒加 jitter，认证失败/host-key 变化不无限重试，恢复先 RPC、terminal 回 observe、旧输入丢弃：`docs/plan/docs/07_多设备SSH与文件.md:21-25`。
- Core 当前仅有输入策略和单 epoch parser；DeviceSession/reconnect actor 尚未实现：`src/HerdDesk.Core/CLAUDE.md:5-14`。Core 仍不得引用 SSH 实现。
- HD-018 已规划每个完整 SessionKey 的 DeviceSession 为唯一 timer/attempt owner，并明确不建立第二个 RecoveryManager；本任务扩展该 policy/actor，不另建 coordinator：`.trellis/tasks/09-08-windows-hd-018/design.md:3-17`。

## 需求

- **R1 失败分类边界**：HD-020/022 将已知 host-key、认证、协议和 transport 结果映射为 HD-018 的 `RecoveryFailure`/retry class；不得另建平行失败分类，也不得仅凭本地化 stderr 猜成可重试认证/网络错误，unknown 默认人工阻断。
- **R2 每 session 独立状态**：每个完整 `DeviceId+SessionKey` 的既有 DeviceSession 独占 attempt、generation、next due、in-flight、last failure、timer 与 cancellation source；同设备其他 session 和其他设备均不共享这些运行状态。
- **R3 有界 jitter 公式**：连续瞬态失败使用注入 clock/RNG 的指数 equal-jitter，所有 delay 落在 `[1s,30s]`；成功达到完整 RPC Ready 后计数归零，不以 TCP connect/child alive 提前归零。
- **R4 可重试范围**：仅明确的 transient network/transport failure 自动调度；daemon 不存在、schema incompatible、stdout pollution、配置错误和 unknown failure 默认 `ManualBlocked`，不靠循环掩盖根因。
- **R5 认证阻断**：`AuthenticationBlocked` 与 `UnsupportedAuthentication` 在 `DeviceId+ProfileRevision` 层记录阻断并适用于该 profile 的所有 sessions，但不拥有或创建 timer。只有凭据/agent/profile 的相关 revision 变化且用户对一个完整 SessionKey 明确点“重试”时，该 DeviceSession 才允许一次立即尝试；不 fan-out 到同设备其他 session，无变化、无点击或仅改 label 均保持阻断。
- **R6 host-key 阻断**：`HostKeyUnknown/Changed` 在 `DeviceId+ProfileRevision` 层记录阻断，不创建 timer。用户必须回到 HD-020 的 fingerprint/可信侧信道流程；只有新 trust revision 且当前 assessment 为 Trusted，再对完整 SessionKey 明确点“重试”才允许该 DeviceSession 一次尝试，其他 session 不被连带启动。
- **R7 重连语义**：一次有效 retry 先重建 request/event RPC 并完成 snapshot/dirty 收敛，再按可见 pane 重建 observe；生成新 epoch、丢弃旧 pending/帧/输入，绝不自动恢复 control/takeover。
- **R8 UI 可解释性**：每个完整 SessionKey 的状态显示“将在 N 秒重试”“认证已阻断”“主机密钥需核验”“配置/协议需处理”，提供 keyed Cancel、Retry now 或对应 Edit/Review 操作；颜色不是唯一编码，后台无不可见 prompt。
- **R9 持久与取消**：每 SessionKey 的瞬态 timer/attempt 为进程内 actor 状态；认证/host-key block 的 DeviceId、blocked ProfileRevision、kind 与相关 evidence revision 持久化，App 重启后仍阻断。停用/删除 session 或设备、成功 Ready 或 block 转换必须由对应 DeviceSession 取消自己的旧 generation timer。
- **R10 隐私**：状态和 telemetry 只含脱敏 DeviceId、attempt/delay/kind/revision、duration；不含 credential、host/path、raw stderr、terminal/input payload。

## 验收标准

- [ ] **AC1（R2–R3、贡献原 AC26）**：每个 SessionKey 在固定 RNG 序列下第 0..N 次 delay 精确可复现，范围依次为 `[1,2)`、`[2,4)`、`[4,8)`、`[8,16)`、之后 `[15,30)`；任何值 clamp 到 1–30 秒。
- [ ] **AC2（R2–R4、贡献原 AC26）**：设备 A 的 session A1 连续瞬态失败时 A2 与设备 B 仍请求/观察；A1 同时最多一个 timer/attempt，Cancel/success/新 generation 后旧 callback 不启动连接。
- [ ] **AC3（R5、贡献原 AC22/26）**：认证失败和 unsupported auth 后推进 fake clock 24 小时仍零 retry；相同 revision 的 keyed 点击仍阻断，相关 revision 更新+对 A1 明确点击只触发 A1 一次，A2 不被连带启动。
- [ ] **AC4（R6、贡献原 AC22/26）**：unknown/changed host key 后零 timer；普通 Retry、改 label、仅网络恢复均无连接；可信 key 写入产生新 revision、assessment=Trusted 且对完整 SessionKey 点击后才有该 session 一次尝试。
- [ ] **AC5（R7）**：恢复过程先 RPC Ready，terminal 仅 Observe；旧 epoch response/event/frame/input 全丢弃，ControlVerified 保持 false，未发送输入显示“未重放”。
- [ ] **AC6（R8–R9）**：重启 App 后 auth/host-key block 在该 DeviceProfile 的各 SessionKey 上仍可见；keyed Cancel/disable/delete 只清对应 actor timer；状态文本、AutomationProperties 和键盘操作可区分等待与人工阻断。
- [ ] **AC7（R10）**：秘密哨兵不出现在持久 block、日志、UI telemetry、错误或 timer state；unknown stderr 不被保存为公共消息。
- [ ] **AC8（证据）**：L1 deterministic policy、L2 隔离 SSH 故障注入、L3 UI/revision 恢复分开归档；本任务只交付候选贡献，不改原 AC22/26 为 passed。

## 原 AC 所有权

- HD-024 对 **AC22** 交付认证/host-key block、revision 与 UI 恢复的实现/L1/L2候选；HD-020 交付身份核验，**HD-026 最终汇总验收**。
- HD-024 对 **AC26** 交付 per-SessionKey jitter/backoff 与 profile-level 无认证重试；HD-022 交付通道隔离，**HD-026 最终汇总验收**。

## 范围外

- 不新增密码、keyboard-interactive、MFA、浏览器或 Tailscale provider；不改变 HD-020 trust store；不实现 HD-022 transport、HD-025 pooling/bandwidth 或 HD-026 最终矩阵。
- 不新增 coordinator、第二个 recovery 状态源或设备级 timer；不持久化待发送输入，不在后台自动 takeover/control，不因网络恢复自动解除 auth/host-key block，不停止/启动 daemon。

## 阻塞与回退

分类无法稳定验证时采用 `ManualBlocked` 并显示诊断入口；若自动重连存在串设备、重复 timer 或输入恢复风险，则关闭自动重连，只保留按设备明确 Retry。
