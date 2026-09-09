# HD-032 文件故障与安全证据索引

L1 收口目录。指向已交付的 HD-028/029/030/031 L1 产物。不是产品 AC 通过，也不是 live FS / SSH / TOCTOU / attack / UI 证明。

权威机器文件：

- `evidence/files/catalog.json`
- `evidence/files/support-matrix.json`
- `implementation/hd-032-l2.json`

`phase_gate=not_passed`。`g0_passed=false`。模板文件 `template=true`，空 hash / 退出码，不是一次成功运行。假文件系统、mock 进程、静态 validator 不能让任何真实 TOCTOU / 路径 / 故障条款通过。

## 执行卡

| 场景 | AC | owner | L1 产物 | live |
|---|---|---|---|---|
| payload integrity | AC31 | HD-028, HD-029 | `TransferCoordinator` / `FileHash` / `LocalFileEndpoint` | `not_run` |
| permission / ENOSPC | AC32 | HD-028 | `TransferCoordinator` 权限失败路径；无真实 ACL/quota | `not_run` |
| SSH link interrupt | AC32 | HD-028 | `FileBridgeClient` / `RemoteFileService`；不可并入断网 | `not_run` |
| ssh-kill | AC32 | HD-028 | 仅 harness 自有 ssh PID；不可并入断网 | `not_run` |
| helper-kill | AC32 | HD-028 | 仅 harness 自有 filebridge PID；不可并入断网 | `not_run` |
| symlink / junction / reparse swap | AC34 | HD-027, HD-028 | `WirePath` / `WindowsLocalNameMapping` / threat-model | `not_run` |
| 32-way KeepBoth | AC33 | HD-028, HD-029 | `KeepBothNames` / `ConflictDialogViewModel` | `not_run` |
| Fail / Replace conflict | AC33 | HD-028, HD-029 | `TransferStateMachine` / conflict dialog | `not_run` |
| dual-pane UI switch | AC31, AC33 | HD-029 | dual-pane ViewModels；无 WinUI | `not_run` |
| attach no Enter | AC35 | HD-030, HD-031 | `AttachmentCoordinator` / `NoAutoSubmitTests` / `PasteCoordinator` | `not_run` |

Contract catalog 各条保持 `not_run`。不得宣称静默覆盖已 live 测试。不得 glob 删除。不得停止用户 daemon/agent。

## 支持矩阵

承诺范围：Windows 11 x64 客户端 + Linux x64 远端。两行均为 `not_run`。禁止把 Windows 字段抄到 Linux。macOS / ARM64 无独立证据，标 `unsupported` 或 `experimental`，不得标 supported。

## live 行

| 行 | 状态 | 原因 |
|---|---|---|
| live FS | `UNVERIFIED` / `not_run` | 无授权 disposable payload / ACL / quota |
| live SSH | `UNVERIFIED` / `not_run` | 无授权 SSH 中断或自有 PID kill |
| live TOCTOU | `UNVERIFIED` / `not_run` | 无授权第二进程 symlink/junction/reparse |
| live attack | `UNVERIFIED` / `not_run` | 无授权 32-way KeepBoth / Replace race |
| live UI | `UNVERIFIED` / `not_run` | WinUI 未准入；`tests/Integration.Windows` 不存在 |

残差 JSON：`implementation/hd-027-l2.json` 至 `hd-032-l2.json`。产品 AC31/AC32/AC33/AC34/AC35 仍为 `not_run`。
