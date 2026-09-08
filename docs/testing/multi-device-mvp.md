# HD-026 多设备 MVP 证据索引

L1 收口目录。指向已交付的子任务 L1 产物。不是产品 AC 通过，也不是 live SSH / WinUI 证明。

权威机器文件：

- `evidence/multi-device-mvp/catalog.json`
- `evidence/multi-device-mvp/support-matrix.json`
- `implementation/hd-026-l2.json`

`phase_gate=not_passed`。`g0_passed=false`。模板文件 `template=true`，空 hash / 退出码，不是一次成功运行。

## 执行卡

| 场景 | AC | owner | L1 产物 | live |
|---|---|---|---|---|
| identity collision | AC21 | HD-023 | `WriteIntentGuard` / `GlobalProjectionStore` / `PartitionIsolationTests` | `not_run` |
| SSH identity/config | AC22, AC23 | HD-020, HD-024 | OpenSSH preview/trust / `SshFailureClassifier` / `EditDeviceViewModel` | `not_run` |
| transparent stream | AC24 | HD-022 | `RemoteSessionTransportSet` fake `ssh -T` | `not_run` |
| recovery | AC13 | HD-018, HD-019, HD-022, HD-024 | `RecoveryPolicy` + local-mvp catalog | `not_run` |
| no replay | AC14 | HD-016, HD-018, HD-022 | `ControlLeaseCoordinator` / `InputPolicy` | `not_run` |
| ownership | AC15 | HD-013, HD-018, HD-019, HD-022 | `AppExitCoordinator` / `OwnedChildProcess` | `not_run` |
| isolation/retry | AC26 | HD-024 | per-`SessionKey` backoff / auth block | `not_run` |
| search p95 | AC19 | HD-011, HD-023 | in-memory `GlobalSearchIndex`；无第三 DeviceId | `not_run` |
| budget/platform | AC27 残差 | HD-025, HD-026 | admission / queue / `SshConnectionLease`；`B_ssh` 未测 | `not_run` |

同一 host 上两个 named session 不能计作三台设备。AC19 缺授权第三 `DeviceId`，保持 `not_run`。

## 支持矩阵

承诺范围：Windows 11 x64 客户端 + Linux x64 远端。两行均为 `not_run`。禁止把 Windows 字段抄到 Linux。macOS / ARM64 无独立证据，标 `unsupported` 或 `experimental`，不得标 supported。

L1 可预览：alias / port / identity_file / ssh_agent / ProxyJump / 未知 host 核验 / host-key 变更阻断。未支持：password argv/stdin、keyboard-interactive、隐藏 passphrase、MFA/浏览器、Tailscale SSH、PKCS#11。未支持行不得标 live pass。

## live 行

| 行 | 状态 | 原因 |
|---|---|---|
| live SSH | `UNVERIFIED` / `not_run` | 无隔离 Windows OpenSSH / Linux herdr 授权 |
| live WinUI | `UNVERIFIED` / `not_run` | WinUI 未准入；`tests/Integration.Windows` 不存在 |
| three DeviceId search | `UNVERIFIED` / `not_run` | 无第三 DeviceId；内存搜索不是 live p95 |
| supervised GUI crash | `UNVERIFIED` / `not_run` | 无监督崩溃授权；不得停用户 daemon |

残差 JSON：`implementation/hd-013-l2.json` 至 `hd-026-l2.json`。产品 AC13/14/15/19/21/22/23/24/26 仍为 `not_run`。
