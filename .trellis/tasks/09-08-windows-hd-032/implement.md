# HD-032 · 实施计划

## 启动条件

- [ ] `implementation/status.json` 已权威记录 `phase_gate=passed`，HD-028/029/031 已完成，
  HD-030 的 AC35 实现证据与待实施 HEAD 的 `just ci` 可用，用户批准本子任务。
- [ ] 测试操作者提供显式 disposable roots/hosts；任何 ACL、quota、network 操作另有相应授权。
- [ ] 预检脚本证明目标不在 workspace、用户资料/工作目录或 production；否则停止该场景。

## 顺序实施

1. 冻结 AC31–35 clause→scenario→owner→evidence mapping，列出所有支持平台组合。
2. 建立 attack fixture、独立 race attacker、run manifest 与 sentinel/hash oracle。
3. 先跑 L1 state/codec/UI projection 测试校准预期；明确它们不计最终通过。
4. 在真实 local process/filesystem 跑四档 payload、metadata、conflict 与 cancel 基线。
5. 在真实 Windows ACL/Linux mode 与 quota filesystem 跑 permission/disk-full，保存 OS 证据。
6. 分开执行实际 link interruption、app-owned ssh kill、helper hard kill及 next-launch recovery。
7. 用第二进程执行 leaf/parent symlink/junction/reparse swap、traversal 和 path 名称 corpus。
8. 跑 32-way KeepBoth、Fail race、Replace target-changed/exchange/restore-failure；逐文件
   独立 hash，并证明未知结果保留两边及 ledger、没有 silent overwrite。
9. 在 Windows 交互桌面跑双栏 conflict/progress/cancel、快速切目标和 stale remote 状态。
10. 对能力表中的 agent/platform 运行 path/attachment 流程，确认不追加 Enter/自动提交。
11. 审阅日志/evidence 隐私、PID/cleanup 范围和所有未覆盖项；失败路由 owning task。
12. 修复后只重跑受影响 scenario 加必要回归，最后重跑完整强制矩阵并生成验收建议。

## 关键次数与停止条件

- symlink/parent swap：barrier 定向每个窗口至少 20 次，无 barrier stress 至少 10,000 次。
- KeepBoth：每轮 32 jobs，至少 10 轮；每次检查 unique raw name、length、SHA-256。
- cancel：五个阶段各至少 10 次；network/ssh/helper kill 各至少 10 次。
- UI target switch：两个同名 pane/两设备至少 100 次，任何错目标立即阻断。
- 任一 outside sentinel 改变、非本 job 删除、静默覆盖、假 Complete 或自动提交即停止发布；
  先保存证据，不继续破坏性矩阵。

## 命令与证据等级

当前存在：`just ci`，只能验证离线结构/已有 G0。以下是拟建 harness 存在后的未来命令：

```powershell
dotnet test tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj --configuration Release --filter FullyQualifiedName~Files
dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj --configuration Release --filter FullyQualifiedName~Files
pwsh -NoLogo -File scripts/Run-FileFaultMatrix.ps1 -Config <approved-disposable-config>
```

- L1：mock/fake/contract，仅验证预期与状态机，不证明真实路径、权限、磁盘或 UI。
- L2：真实 OS filesystem、独立 helper/ssh process；证明 local/remote backend failure。
- L3：交互 Windows UI+真实 remote/agent；证明冲突、目标、capability、无自动提交。
- L4：高迭代 race/fault；作为 AC32–34 安全置信证据，不覆盖未测平台。

## 验收与回滚

只有 matrix 强制项均有合格 L2/L3（安全 race 含 L4）时，HD-032 才可向父任务建议
AC31–AC35 passed；状态实际更新仍需引用 evidence path。失败时禁用 write/Replace/附件或
整个文件面，退回只读/P3 MVP。结束只清理已解析并验证的 disposable run roots 与自有 PID；
cleanup 本身不确定时保留现场并标 blocked，不扩大删除范围、不停止 herdr daemon/agent。
