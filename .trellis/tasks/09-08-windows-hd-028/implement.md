# HD-028 · 实施计划

## 启动条件

- [ ] `implementation/status.json` 已权威记录 `phase_gate=passed`，HD-027 protocol
  ADR/golden vectors 与待实施 HEAD 的 `just ci` 已通过，用户明确批准本子任务。
- [ ] 重新读取届时 Rust/C#/tests 目录规则，并确认 HD-025 file lease 已可用。
- [ ] 所有 real-file 测试只使用校验过的 disposable roots；不指向用户工作目录。

## 顺序实施

1. 先实现 local/filebridge 共享 Contracts 与 Core state machine，保持 Contracts BCL-only。
2. 扩展 HD-027 Rust codec 为 `serve` 单 job process；固定 stdout protocol、stderr diagnostics。
3. 实现 handle-relative path/lstat 与 list pagination，再实现 stat/read；先交付只读 gate。
4. 为 Unix helper 与 Windows local adapter 分别写 handle-relative exclusive same-directory
   temp、private atomic ledger/跨进程锁；先测 crash recovery，v1 不实现 remote Windows。
5. 实现流式 write、双侧 SHA-256/length、flush 与 commit 前完整复验。
6. 实现 Fail/KeepBoth no-replace；KeepBoth 直接竞争创建并返回真实 final raw name。
7. 实现 Linux/macOS atomic exchange 与 Windows local ReplaceFile+backup 候选；注入交换后
   identity mismatch 和换回失败，未通过无数据丢失 race gate 的平台保持 capability off。
8. 实现 C# `FileBridgeClient` 单 reader/单 writer、bounded stderr、EOF/exit 分类和取消宽限。
9. 实现 Local↔Remote/Remote↔Remote coordinator、两个 chunk buffer、完整
   DeviceId/SessionKey/epoch 的 HD-025 lease 与真实 progress。
10. 覆盖取消、断线、hard kill/恢复、hash/length mismatch；保证 cleanup 只查 exact ledger。
11. 在受支持真实 filesystem 跑 AC30 矩阵，并将环境/命令/hash/原始结果写入 evidence。
12. 运行全 gate；AC31/32 只写贡献证据，交 HD-029/032，不修改其最终状态。

## 精确测试

- list/stat：空目录、空格、Unicode、Unix 换行/non-UTF8、最大支持长名、symlink/reparse、
  dangling link、permission denied；raw name round-trip，禁止 `ls`/display-name 回传。
- transfer：0B、1KiB、50MiB、配置大文件；Local↔Remote 与 Remote↔Remote hash/length 一致，
  source 中途变化拒绝，buffer 不超过设计值，progress 绑定初始 session/epoch 且单调。
- commit：temp 与 final 同目录/文件系统；Fail 已存在不覆盖；32 个 KeepBoth 并发名称/hash
  唯一；Replace 在交换前/后目标改变都不删除被替换对象，restore 失败保留 ledger/两边；
  final 只在 flush/hash/commit 后 Complete。
- cleanup：cancel/EOF/ssh kill/helper hard kill；job A 不删除 source、job B temp/final 和
  外部 sentinel；identity mismatch 留存并报告，下一启动恢复精确 job。
- protocol/client：partial frame、stderr flood、early EOF、exit 0 without Complete、late progress、
  wrong JobId/device 均拒绝或标 outcome unknown。

## 命令与证据

当前存在：`just ci`，仅证明离线 G0/L1。以下均为文件建成后的未来命令：

```powershell
cargo fmt --manifest-path filebridge/Cargo.toml --check
cargo clippy --manifest-path filebridge/Cargo.toml --locked --all-targets -- -D warnings
cargo test --manifest-path filebridge/Cargo.toml --locked
dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj --configuration Release
dotnet test tests/Contract/HerdDesk.ContractTests.csproj --configuration Release --filter FullyQualifiedName~Files
dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj --configuration Release --filter FullyQualifiedName~Files
dotnet test tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj --configuration Release --filter FullyQualifiedName~Files
```

- L1：codec/client/state/conflict/queue 单元与 contract；fake FS 不能通过 AC30–32。
- L2：真实 helper process 与真实受控 filesystem/OS primitive；HD-028 的 AC30 最终证据。
- L3：真实 Windows UI+SSH remote、攻击/断网/权限/磁盘；HD-032 汇总 AC31/32。

## 回滚

分只读、write、Replace 三道 capability gate；后一道失败只退回前一道。协议或路径安全
失败则完全停用 filebridge。清理失败保留 exact ledger 和人工入口，不扩大删除范围；
任何回滚都不删除 source/用户 final、不停止 daemon/agent、不修改 SSH 身份配置。
