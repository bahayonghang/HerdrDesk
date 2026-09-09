# HD-028 · 技术设计

## 拟建文件与边界

- Rust：`filebridge/src/main.rs`、`server.rs`、`path.rs`、`metadata.rs`、`job.rs`、
  `operations/{mod,list,stat,read,write,rename}.rs`、`platform/unix.rs`；v1 不建 remote Windows adapter。
- Rust tests：`filebridge/tests/list_stat.rs`、`transfer_integrity.rs`、
  `conflicts.rs`、`cancellation_recovery.rs`。
- Contracts：`src/HerdDesk.Contracts/Files/FileModels.cs`、`IRemoteFileService.cs`。
- Core：`src/HerdDesk.Core/Files/TransferCoordinator.cs`、`TransferStateMachine.cs`。
- Infrastructure：`src/HerdDesk.Infrastructure/Files/LocalFileEndpoint.cs`、
  `FileBridgeProcessFactory.cs`、`FileBridgeClient.cs`、`RemoteFileService.cs`。
- C# tests：`tests/Unit/HerdDesk.Core.Tests/Files/TransferCoordinatorTests.cs`、
  `tests/Contract/Files/FileBridgeClientTests.cs`、
  `tests/Integration.Windows/Files/LocalFileEndpointTests.cs`、
  `tests/Integration.Ssh/Files/FileBridgeProcessTests.cs`。
- 证据：`evidence/files/backend-environment.json`、`backend-matrix.json`、
  `backend-hashes.json`、`README.md`。

均为未来路径；HD-027 的 codec/spec 文件由本任务消费，不复制协议 parser。

## 端口和数据流

`IFileEndpoint` 提供 `ListAsync`、`StatAsync`、`OpenReadAsync`、`BeginWriteAsync`；local
与 filebridge adapter 返回同一 typed result。`TransferEndpointKey` 固定包含 DeviceId、
SessionKey、开始时 ConnectionEpoch 和 resolved endpoint identity；协调器另持 opaque path、
identity、length/hash 与 conflict policy。Remote→Remote 数据经 app 的
两个 endpoint 流动，绝不把 source path 交给 destination 主机执行命令。

每次 copy 生成固定 `JobId` 与 source/destination identity。状态为：

`Queued → Admitted → Starting → Staging → Transferring → Verifying → Committing → Completed`

取消/失败从非 terminal 状态转 `CancelRequested/Failed → Cleaning → Cancelled/Failed`；
cleanup 未确认则 `FailedCleanup`。terminal 状态不可回退，晚到 progress/result 按 JobId
丢弃并记录类别。UI 只能消费投影，不能直接把状态写成 Completed。

## 流式完整性与背压

protocol data chunk 最大 1 MiB；协调器最多保留两个未写 chunk，故业务 payload buffer
上限 2 MiB/job，额外 runtime/codec/OS buffer 另测。source open 后记录 identity/length，
读取过程中累计 SHA-256；destination 独立累计 bytes/hash。EndData 后复验 source
identity/size/mtime，并同时满足声明 length、source digest、destination digest；任何
mismatch 或源变化都进入 Cleaning，不能 commit。

progress 表示 destination 已接受的 payload bytes，不表示磁盘 durability 或最终成功。
Remote→Remote 若任一端断开，取消另一端并分别完成清理；不自动重试未知 commit 结果。

## temp、ledger 与恢复

写入先持有 destination directory handle/identity，在同目录以
`.herddesk-upload-{JobId:N}-{128-bit nonce}.tmp` 进行 exclusive/no-follow create。
helper 在用户私有 state 目录原子写 job ledger，权限仅当前用户；local Windows 用同一
字段的本地私有 ledger。ledger 保存 endpoint/epoch、raw temp 与 intended final、temp/parent/
confirmed-target identity、expected length/hash、conflict mode 和 commit phase；这些敏感值
可写私有 ledger，但不得进入日志。每个 active ledger 全程持有 OS 文件锁。

正常取消/EOF 通过仍持有的 handle 清理。hard kill 后，同 endpoint 的恢复器只处理能取得
排他锁的私有 ledger；用 no-follow 打开精确 temp 并比较 parent/temp identity，匹配才删，
不匹配则报告 orphan/conflict。恢复从不删除 destination。若 commit 已完成但 terminal
result 丢失，则按 ledger 的 intended final/length/hash/identity 重观察后分类为 committed、
conflict 或 unknown，不盲重传；确认安全后才删除 ledger。

## commit 与冲突

commit 前按顺序重新验证 directory、temp identity、length/hash、destination identity/token，
随后 flush file，使用同目录 primitive：

- Fail：一次 atomic no-replace rename；存在即 conflict。
- KeepBoth：按 `name (n).ext` 生成合法候选，直接 no-replace，碰撞后最多重试 1000 次；
  返回实际 raw final name，任何分支都不 overwrite。
- Replace：要求 `ConfirmedTarget(identity, observedVersion)`。不能用“最后 stat 后普通
  overwrite”冒充条件替换：Linux 候选用 `renameat2(RENAME_EXCHANGE)`、macOS 候选用
  `renameatx_np(RENAME_SWAP)`，Windows local 候选用 `ReplaceFileW` 的同目录 backup，均在
  原子交换/替换后检查被移到 staging/backup 的旧对象是否正是 confirmed identity。
  匹配才提交并精确清理旧对象；不匹配则立即原子换回，返回 conflict。换回失败保留两边
  identity/ledger 并返回 outcome unknown，绝不删除任一对象。只有该 commit/restore race
  在 HD-032 真文件系统 gate 通过的平台才暴露 Replace；否则明确 unsupported。
- Cancel：不进入 commit，转 Cleaning；它是领域选择，不转换成 Replace/Fallback。

commit 后再验证 final identity/length；成功才删除 ledger 并发 Complete。Replace 的
staging/backup identity 与恢复阶段也写入 ledger，恢复器从不把 backup 当普通 temp 删除。
Unix 目录 fsync、
Windows file/directory durability primitive 的能力差异必须记录，不能把 rename 等同断电持久。

## 路径、并发、错误与 UI 契约

Unix helper 每个 component 用 `openat` + `O_DIRECTORY|O_NOFOLLOW` 从持有的 directory fd
逐级解析，temp 用 `openat(O_CREAT|O_EXCL|O_NOFOLLOW)`；Linux no-replace 候选为
`renameat2(RENAME_NOREPLACE)`，macOS 为受 gate 的 `renameatx_np(RENAME_EXCL)`，运行时返回
unsupported 就关闭写 capability，不退回 `rename()` 覆盖。Windows local adapter 的小型
FFI 边界用 root-relative directory/file handle、`CREATE_NEW` 与 no-reparse flags；若目标
filesystem 无法证明同等语义则只读。list 用 lstat/native metadata 显示 link/reparse；
read-follow 必须是单独明确 capability/用户动作。
Windows 保留名、非法字符或不可无损映射在本地 destination 层返回 confirmation required，
不自动改名；raw remote name 永不进入 XAML/HTML 或 shell。

每个 active endpoint 以完整 DeviceId/SessionKey/epoch 获取 HD-025 file lease；
Remote→Remote 原子获取两槽，失败则不启动
任何进程。稳定错误类别包括 permission/not-found/path/link/conflict/length/hash/disk-full/
cancel/EOF/protocol；unknown raw code 保留。日志只记脱敏 DeviceId、JobId、stage、bytes、
duration/code，不记文件名、绝对路径、内容或 stderr 原文。
