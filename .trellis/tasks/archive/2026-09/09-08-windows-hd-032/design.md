# HD-032 · 验证设计

## 拟建文件

- `tests/fixtures/files/attack-cases.json`：case 数据与 expected code；仅为输入，不是实证。
- `tests/tools/HerdDesk.FileRaceAttacker/HerdDesk.FileRaceAttacker.csproj`、`Program.cs`：
  Windows 第二 OS 进程执行受控 swap/create/replace，不链接产品内部状态；显式注册为
  Integration.Windows 的 test support，不形成第三个测试入口。
- `tests/Integration.Windows/Files/FileFaultMatrixTests.cs`、`PathRaceTests.cs`、
  `ConflictRaceTests.cs`、`CancellationRecoveryTests.cs`、`DiskFullTests.cs`、
  `FileWorkflowE2ETests.cs`、`AttachmentCapabilityE2ETests.cs`。
- `tests/Integration.Ssh/Files/RemoteFileFaultTests.cs` 与固定 POSIX race support：在 disposable
  root 用第二真实进程触发 swap，不要求远端 Python/.NET，也不链接产品内部状态。
- `scripts/New-DisposableFileTestRoot.ps1`、`Run-FileFaultMatrix.ps1`。
- `evidence/files/environment.json`、`matrix.json`、`hashes.json`、
  `capability-matrix.json`、`README.md`；逐 run 脱敏附件在 `evidence/files/runs/<run-id>/`。

以上为未来路径；脚本必须要求显式 base root/target，解析绝对路径并验证仍位于 disposable
root 后才能清理。它只跟踪自己启动的 PID/job，不按进程名或 glob 删除。

## 场景模型与判定 oracle

`matrix.json` 每项固定 `scenario_id`、source AC、owner、environment、precondition、steps、
expected invariants、actual、status、artifact hashes、defect、evidence_level。状态只允许
passed/failed/blocked/not_run；L1、L2、L3 分开记录，不能用最高一行覆盖未测平台。

每次 run 创建随机 root，预先写 outside-root sentinel、source、other-job temp/final，记录
file identity/length/SHA-256。测试后重新从 OS 读取这些对象；产品事件或 UI 文本不能作为
唯一 oracle。成功文件也由独立读者重新 hash，而非复用 TransferCoordinator 的结果。

## 真实故障矩阵

| 类别 | 实际操作 | 必须观察 |
|---|---|---|
| permission | Windows ACL/Linux mode 的真实拒绝 | stable permission error；无 temp/final |
| disk full | 受控 quota/小容量真实 filesystem 写满 | no Complete；精确 temp cleanup/residue |
| network | 对测试 SSH endpoint 实际中断链路 | 两端状态、无盲重试、恢复只读 |
| ssh kill | 终止 harness 启动的 ssh PID | client EOF 分类、remote cleanup 实际结果 |
| helper kill | temp/stream/rename 前后终止 helper | next-launch ledger recovery、结果未知边界 |
| symlink swap | attacker 进程交换 leaf/parent | outside sentinel 不变，拒绝/重新询问 |
| KeepBoth race | 32 个真实进程同名 commit | 32 个唯一名字与对应 hash |
| Replace changed | 确认 A 后或 exchange barrier 放入 B | B 保留；换回后 conflict，换回失败为 unknown且两边不删 |

permission/disk/network 必须由 OS/链路实际产生。允许 test-only barrier 放大竞态，但 swap
仍由第二进程和真实 filesystem 完成；另跑无 barrier 高迭代 stress，二者都保存结果。

## 取消与线性化检查

在 temp-create、首 chunk、中段、hash complete、commit 请求五个观察点触发 cancel。
commit 线性化前结果只能 Cancelled/Failed 且 temp 精确清理；线性化后只能 Verified Complete
或 OutcomeUnknown 后查询，不能报告 Cancelled 并删除 final。hard kill 后 recovery 必须先
取得 ledger 排他锁，再按 raw path+parent/temp/backup identity 恢复；锁仍被活动进程持有、
identity mismatch 或 commit phase 不明都必须保留并显式报告。

KeepBoth oracle 不接受“先查不存在”：attacker 同步抢占候选名，产品必须通过 atomic
no-replace 重试。Replace oracle 在用户确认后替换 identity；旧 token 绝不能授权 B。

## UI、附件与信任检查

E2E 固定初始 source/destination DeviceId、SessionKey、ConnectionEpoch、pane 和 JobId，
再快速切换焦点/设备/session；
进度、冲突 dialog、完成跳转均须仍指向原目标。Replace dialog 展示脱敏但可区分的目标，
target-changed 关闭旧确认并重新询问。Cancel 可由键盘完成且状态不依赖颜色。

对已声明 agent/platform 逐项实际尝试 path input/attachment；记录版本、shell、renderer。
只上传不输入显示 Uploaded；输入 path 显示 PathEntered；仅由 agent 的可观察确认才能标
AttachmentAccepted。所有输入不追加 Enter，unknown/失败状态不得自动批准或重试。

原始远端名、stderr、文件 bytes 视为不可信；证据只保留 stable code、脱敏 ID、大小、
hash、阶段和时间。需要人工截图时先审阅裁剪，私有 raw logs 留在批准的非仓库位置。

## 缺陷路由

protocol/codec→HD-027，list/write/recovery→HD-028，冲突/进度 UI→HD-029，附件→HD-030，
剪贴板/cache→HD-031。每个 failed scenario 绑定 defect 和重跑记录；HD-032 只在原场景
实际转绿后接受，不因 owning task 的 unit test 通过而关闭。
