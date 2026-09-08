# HD-024 实施计划

## 启动门

- [ ] `implementation/status.json.phase_gate=passed`、待实施 HEAD 的 `just ci` 通过、HD-022 完成，并由用户明确批准最新父/子计划后，才可 `task.py start`；当前 G0 `not_passed` 明确阻止启动。
- [ ] 启动前核对 HD-010/018 的每 SessionKey DeviceSession/recovery owner、HD-020 `DeviceId+ProfileRevision` contract 与 HD-022 `RemoteSessionTransportSet` outcomes；只扩展既有 owner，不新建平行 actor/coordinator/timer。
- [ ] 规划阶段不运行 live SSH；实施的 auth/host-key/断网只在授权隔离目标，绝不使用生产/训练设备或发送 terminal 输入。

## 有序步骤

1. [ ] 扩展 HD-018 的 `RecoveryFailure`、`RecoveryDecision` 与 `RecoveryPolicy`，加入远端 failure/disposition/revision；保持 UI-safe，未知 kind fail-closed，不复制 taxonomy。
2. [ ] 扩展既有 `RecoveryBackoff.cs` 的远端 exact equal-jitter formula、checked/clamp 与 retry matrix；注入 RNG，先完成 deterministic table/property tests。
3. [ ] 扩展既有 DeviceSession 消费新 policy：每个完整 SessionKey 继续唯一持有 TimeProvider timer、generation、single in-flight、Cancel/Ready/reset；无 coordinator、设备级 map 或全局锁/await。
4. [ ] 实现 `SshFailureClassifier.cs`，优先消费可信阶段结果；覆盖 localized/malicious stderr 为 UnknownBlocked，禁止错误 auto-retry。
5. [ ] 实现 `SshRecoveryBlockStore.cs` 的 `DeviceId+blocked ProfileRevision` auth/host-key snapshot、文件锁、atomic replace、重启恢复和旧版本回退；不存 timer/attempt/input/secret。
6. [ ] 接入 HD-022：目标 SessionKey 的有效 attempt 创建新 `RemoteSessionTransportSet`/epoch，RPC sync 后才 Ready，terminal observe；stale callback/result/input 丢弃并产生安全状态。
7. [ ] 完成 DeviceConnectionStatusView/ViewModel 的 keyed 倒计时、Cancel/Retry/Edit/Review、无变化阻断、AutomationProperties 与键盘流；一次 Retry 不 fan-out 到同设备其他 session。
8. [ ] 跑完整 L1，再跑隔离 L2/L3；AC22/26 只登记候选贡献并交 HD-026，不能局部写 passed。

## 精确测试路径与用例

- `tests/Unit/HerdDesk.Core.Tests/Recovery/RecoveryBackoffTests.cs`：远端 policy 的固定 u=0/中间/趋近1、n=0..100、exact range、clamp/overflow、非 retry kind 无 delay。
- `tests/Unit/HerdDesk.Core.Tests/Recovery/DeviceSessionRecoveryTests.cs`：同设备 A1/A2 独立、每 SessionKey 单 timer/in-flight、stale generation、keyed Cancel/delete/Ready reset、network signal/manual retry。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/Recovery/SshFailureClassifierTests.cs`：known stage、localized/恶意 stderr、unknown blocked、无秘密输出。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Ssh/Recovery/SshRecoveryBlockStoreTests.cs`：DeviceId+profile auth/host revision、label/session 排序无关、atomic failure、文件锁、App 重启、无 timer/秘密持久化。
- `tests/Unit/HerdDesk.App.Tests/Devices/DeviceConnectionStatusViewModelTests.cs`：完整 SessionKey 倒计时、按钮 enable、无 revision Retry、Edit/Review intent、不 fan-out、可访问文本。
- `tests/Integration.Ssh/Auth/SshAuthenticationRecoveryTests.cs`：auth deny 24h 零 retry、credential revision+click 一次、无 hidden prompt。
- `tests/Integration.Ssh/Auth/SshHostKeyRecoveryTests.cs`：unknown/rotation 零 retry、trust revision+Trusted+click 一次、设备 B 不受影响。
- `tests/Integration.Ssh/Auth/SshTransientReconnectTests.cs`：1–30s jitter、network recovery、RPC-first/observe、old epoch/input drop、A/B 隔离。

## 命令与证据等级

- [ ] **现有命令**：`just ci`，启动门与合并前运行；当前只证明离线 G0/既有回归。
- [ ] **拟建 L1 Core**：`dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj -c Release --filter FullyQualifiedName~Recovery`。
- [ ] **拟建 L1 Infrastructure/App**：分别运行 Infrastructure `Recovery` 与 App `DeviceConnectionStatus` filter；fake clock/RNG 不得 sleep 实时 30 秒。
- [ ] **拟建 L2**：`dotnet test tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj -c Release --filter "FullyQualifiedName~SshAuthenticationRecovery|FullyQualifiedName~SshHostKeyRecovery|FullyQualifiedName~SshTransientReconnect"`，仅隔离 fixture；缺环境 skip/UNVERIFIED。
- [ ] **L3 人工**：观察倒计时错峰、auth/host block、App 重启、侧信道确认、revision+click、无输入重放与另一设备持续工作；记录环境/版本/步骤/实际/证据。

## 审查与回滚

- [ ] 独立审查公式、注入 clock/RNG、每 SessionKey 单 timer/attempt、generation race、profile-level block 持久化、revision 相关性、Core 无 SSH 依赖、UI keyed intent、日志无秘密。
- [ ] 任一重复 timer、串 session/设备、错误 unblock 或输入恢复缺陷都关闭自动重连，保留 keyed 手动 Retry；AC 保持 failed/not_run。
- [ ] rollback 只取消目标 DeviceSession 自有 timer/attempt、恢复旧 block 文件并断开本应用该 SessionKey 直接 child；不修改 trust key/credential、不停止 daemon/agent、不影响同设备其他 session 或其他设备。
