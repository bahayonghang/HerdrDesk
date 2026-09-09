# HD-032 · 文件故障与安全测试

## 目标与阶段门

在真实进程、真实文件系统、真实 SSH 中断和 Windows 交互 UI 上验证 P4 文件链路，
以攻击/故障证据最终收口 AC31–AC35。任务保持 `planning`；只有
`implementation/status.json` 权威记录 `phase_gate=passed`、HD-028/029/031 完成、HD-030
的 AC35 实现可用、待实施 HEAD 的 `just ci` 通过且用户批准后才能启动；测试只允许显式
disposable 资源。

## 当前证据

- filebridge、双栏 UI、附件和真实文件测试当前都未建仓，Windows live 为 `not_run`
  （`implementation/status.json:31`）。
- 威胁模型已把 traversal、symlink、TOCTOU、独占 temp 和原子 rename 列为控制目标
  （`docs/plan/docs/08_安全与威胁模型.md:15`）。
- 测试分层要求 L2 真实 Windows/process，L3 交互桌面/remote，L4 受控故障；hosted/static
  不能替代（`docs/plan/docs/10_测试与验收.md:8`）。
- AC31–AC35 当前均 `not_run`（`planning/acceptance.json:245` 起）。

## 需求

- R1：建立可重复 fault/attack manifest；fake FS、mock process、静态 validator 只作 L1
  前置，不能让任何真实 failure/path/TOCTOU 条款通过。
- R2：至少覆盖 Windows 11 本地目标与 Linux remote；macOS/ARM64 只有实际纳入支持表时
  才能借证据晋级。每次记录 OS、filesystem、mount/options、App/helper/ssh 版本和 hash。
- R3：用真实 ACL/mode 制造 permission denied，用真实 quota-limited disposable volume
  制造 ENOSPC；无法安全提供环境时明确 `UNVERIFIED`，不以 injected exception 替代。
- R4：分别验证实际 SSH 链路中断、杀 app-owned ssh、杀 filebridge；三者不可合并为
  一个“断网”结论，且不得改 production firewall 或终止非测试进程。
- R5：第二攻击进程在 resolve→open、stat→commit 窗口交换 symlink/junction/reparse、
  parent 和 target，证明外部 sentinel 未读写、没有 sandbox escape 或静默 overwrite。
- R6：32 个并发 KeepBoth 针对同名目标，结果名和 hash 全部唯一；Fail 不覆盖；Replace
  在确认后或 atomic exchange 时 target identity 改变，必须换回并 conflict；换回失败时
  两对象与 ledger 都保留为 outcome unknown，任何情况下不得删除攻击者的新 target。
- R7：在 staging/streaming/verifying/commit/replace-restore 边界取消或 hard kill；恢复器
  只能取得已释放的 ledger 锁后处理 identity 匹配的本 job temp/backup，活动 job、其他
  job/source/final/sentinel 不变。
- R8：真实路径集覆盖 `..`、absolute/root mismatch、encoded separator/NUL、Windows 保留名/
  非法字符/ADS、Unicode/空格/换行、近上限长名、dangling link 和深目录。
- R9：真实传输覆盖 0B、1KiB、50MiB 和超过内存预算的大文件，逐端 SHA-256/length
  一致；progress 固定 JobId、DeviceId、SessionKey 与起始 epoch，Complete 只在 commit 后。
- R10：Windows UI 对 Replace/KeepBoth/Cancel 都给确定状态；切换 pane/device/session/focus 不得
  重定向 job，target-changed 必须重新询问，缓存目录不能显示在线。
- R11：按 agent+平台显示实测 capability；“上传完成”“路径已输入”“附件已接受”是三个
  不同状态，路径输入不自动追加 Enter/提交，unknown 不升级为 supported。
- R12：默认 evidence/日志不含凭据、文件内容、绝对私有路径或 terminal 正文；失败和
  cleanup residue 也必须保留可审计、脱敏的 actual result。
- R13：发现产品缺陷回到 HD-028/029/030/031 的 owning layer 修复并重跑原场景；本任务
  不用测试绕过、放宽预期或跨层复制状态来制造通过。

## 来源 AC 映射与本任务验收

- AC1（R9，最终 owner AC31）：四档真实 payload 在已声明路径组合上 hash/length 一致，
  进度和完整 session/epoch 目标正确，未测组合保持 not_run。
- AC2（R3/R4/R7，最终 owner AC32）：取消/断网/kill 仅清理本 job temp，无假完成，
  source、其他 job、final 和 sentinel hash 不变。
- AC3（R6/R10，最终 owner AC33）：Replace/KeepBoth/Cancel 的真实并发与 target-changed
  场景结果确定，不存在静默覆盖。
- AC4（R5/R8，最终 owner AC34）：真实 traversal、symlink/reparse、保留/非法名和 TOCTOU
  攻击被拒绝或重新询问，外部 sentinel 未改变。
- AC5（R10/R11，最终 owner AC35）：真实 UI capability/目标/路径投入流程与实际 agent
  行为一致，上传不自动提交，也不把 path 声称为 attachment acceptance。
- AC6（R1/R2/R12/R13）：每条结论都有环境、步骤、expected/actual、commit/hash、证据级别
  和缺陷；任一强制矩阵缺失时对应 AC 不得整体 passed。

## 回滚

安全/完整性失败即阻断文件面发布，按能力退回只读浏览或 P3 多设备 MVP；不得靠删除
失败证据、扩大 cleanup、停止 daemon/agent 或修改用户 SSH/文件来恢复绿色。
