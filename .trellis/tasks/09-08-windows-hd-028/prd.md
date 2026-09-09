# HD-028 · 文件枚举与传输后端

## 目标与阶段门

按 HD-027 已锁定 wire contract 实现独立 Rust filebridge、C# 文件端口与传输协调器，
完成真实 list/stat 和无静默覆盖的流式传输。任务保持 `planning`；只有
`implementation/status.json` 权威记录 `phase_gate=passed`、HD-027 与待实施 HEAD 的
`just ci` 通过且用户批准后才能启动。

## 当前证据

- 当前仓库没有 filebridge/Infrastructure，只有未编译的单一 `CopyAsync` 草案
  （`docs/plan/contracts/HerdDesk.Contracts.cs:89`）。
- 规划要求上传使用同目录随机 temp、独占创建、hash、原子 rename；KeepBoth 也必须
  防 TOCTOU，取消不得通配清理（`docs/plan/docs/07_多设备SSH与文件.md:39`）。
- 文件验收矩阵要求 0B、1KiB、50MiB、大文件、Unicode/长名、权限、symlink、断网和
  磁盘不足，Complete 必须晚于 hash/rename（`docs/plan/docs/10_测试与验收.md:45`）。
- 所有这些行为当前均未运行，G0/Windows live 仍未通过。

## 需求

- R1：只消费 HD-027 accepted v1；不另造 wire variant，不解析 `ls`/`dir`，协议不匹配
  时停用文件面而非静默降级。
- R2：`herddesk-filebridge` 是按 job 启动的 Rust 进程；C# 通过 local process 或
  HD-022 `ssh -T` 使用同一 codec，UI/Core 不拼远端 shell 路径。
- R3：remote Rust adapter 在 Linux/macOS 无损返回 Unix raw name、type、size、mtime、
  identity、symlink 和 permission error；本地 Windows C# adapter 返回原生 name/reparse。
  两者默认 lstat/no-follow，display name 仅供展示；remote Windows 不在 v1。
- R4：Local↔Remote 与 Remote↔Remote 均由 TransferCoordinator 流式中继；后者获取
  两个设备 file 槽，任何文件不整体载入内存。
- R5：每个写 job 在目标同目录用 `CreateNew/O_EXCL` 语义创建不可预测 temp，禁止
  跟随 link，并记录 `JobId + temp raw path + file identity + directory identity`。
- R6：传输两端增量计算 SHA-256 和长度，使用有界 chunk/backpressure；读取结束要复验
  source identity/size/mtime，目标 flush 后在 commit 前复验 temp identity、length/hash、
  parent identity 和 conflict token。检测到 source 改变就清理 temp，不交付混合快照。
- R7：Fail 与 KeepBoth 使用原子 no-replace rename；KeepBoth 对候选名直接 no-replace
  重试，绝不先 exists-check 后覆盖。Replace 只接受用户确认的目标 identity。
- R8：Replace 目标在确认后发生改变必须返回 conflict；平台无法提供满足契约的安全
  primitive 时禁用 Replace，不能退回无条件 overwrite。
- R9：取消、stdin EOF、连接断开只清理精确 job ledger 中且 identity 匹配的 temp；
  不删除 source、final destination、其他 job 文件或路径通配结果。
- R10：每个 active ledger 持有跨进程锁；硬 kill 释放锁后，下一次同 endpoint 启动才可
  恢复。identity 不匹配或 commit 结果不确定时保留文件并重观察，宁可留下孤儿也不猜删。
- R11：progress 固定携带 JobId、source/destination 的 DeviceId、SessionKey 与起始
  ConnectionEpoch、bytes accepted/total，单调且不超 total；重连/焦点切换不能改目标，
  Complete 只在最终 commit 后产生。
- R12：每设备最多一个、全局最多两个 active file job，排队项只持 metadata；复用
  HD-025 的统一 lease，不建立第二连接计数器。

## 来源 AC 映射与本任务验收

- AC1（R1–R3，最终 owner AC30）：真实 helper/filesystem 覆盖空格、Unicode、换行、
  长名、symlink 和权限拒绝，元数据准确且没有 `ls` 文本解析。
- AC2（R4/R6/R11，贡献 AC31）：0B、1KiB、50MiB 和可配置大文件端到端 hash/length
  一致，进度绑定原 Job/设备；AC31 最终汇总由 HD-032 完成。
- AC3（R5–R8，贡献 AC31/32）：temp 同目录独占，commit 前复验，Fail/KeepBoth 不覆盖，
  Replace target-changed 明确 conflict。
- AC4（R9/R10，贡献 AC32）：取消、EOF、断网与 hard kill 恢复只处理本 job temp；
  source、其他 job 和 sentinel hash 不变。
- AC5（R11/R12，贡献 AC31/32）：有界流和准入并发下无假完成、无无界内存；同设备
  多 session、旧 epoch 晚结果和 Remote↔Remote 均不能写错 endpoint。
- AC31/32 最终 owner 均为 HD-032；本任务不得凭 unit/mock 或局部成功提前整体 pass。

## 不在范围与回滚

双栏/冲突 UI 归 HD-029，agent 投入归 HD-030，真实攻击与总体验收归 HD-032。
任一写安全门失败即退回只读 list/stat；不能安全列举时完全停用 filebridge，且不影响
terminal、daemon 或用户文件。
