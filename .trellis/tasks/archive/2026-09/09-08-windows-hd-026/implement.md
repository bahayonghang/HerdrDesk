# HD-026 执行计划

1. 核对HD-023/024/025完成及HD-013/016/018/019本地证据；同一候选固定代码SHA、
   组件版本、支持平台和明确授权的隔离资源。
2. 建立两设备同名pane、同设备双named session、三DeviceId/100投影三组fixture。
   第三设备必须独立DeviceId；没有环境则AC19保持not_run。
3. 按设计执行SSH配置/host-key、-T分流/banner负例和100次跨设备竞态卡。
4. 注入真实SSH断网与EOF，验证stale、RPC收敛、新epoch observe/full baseline，
   以及同设备其他session和另一设备仍运行。
5. 用无秘密唯一标记验证排队输入、write结果不确定和断线后输入不自动重放；
   记录NotSent/ResultUnknown与端点计数。
6. 正常关闭和监督下强制崩溃GUI，证明只释放自有bridge/ssh，远端daemon/agent
   继续存活；接收P2本地所有权证据并按支持矩阵汇总。
7. 执行三设备100投影搜索，保留每次耗时、p95算法、正确目标key和桥计数。
8. 归档逐场景结果与支持组合；缺环境/授权标not_run并保留任务未完成，失败回传
   对应owner，不能为了过门移除原AC条款。

## 需求到验收追溯

| 需求 | 原AC | 机制 | 验证与最终owner |
|---|---|---|---|
| R1 | AC21 | 完整key、session transport registry、epoch | HD-023贡献，HD-026碰撞矩阵 |
| R2 | AC22/23 | SSH profile/host-key人工核验与认证支持行 | HD-020/024贡献，HD-026真实环境 |
| R3 | AC24 | -T、stdout/stderr分流、污染拒绝 | HD-022贡献，HD-026透明流负例 |
| R4 | AC26 | 每SessionKey唯一恢复owner、device/profile认证阻断 | HD-024贡献，HD-026故障timeline |
| R5 | AC13 | stale→RPC convergence→new observe/full baseline | HD-018/019本地+HD-022/024远端，HD-026最终汇总 |
| R6 | AC14/15 | no replay状态、直接子进程handle归属 | P2本地+HD-022远端，HD-026网络/崩溃证据 |
| R7 | AC19 | 投影搜索/导航与懒terminal启动 | HD-011/023贡献，HD-026三设备计时 |
| R8 | AC23及上述承诺平台条款 | 每OS/arch/认证组合独立证据 | HD-026支持矩阵 |

## 规划交付与实施完成

规划完成代表环境、场景、证据格式、owner及测试工程已明确。
实施完成须实际通过设计矩阵；本任务补充汇总AC13/14/15/19/22，并保留源
AC21/23/24/26责任。离线CI不等于真实SSH/WinUI；未测组合不能复制其他平台结果。
这些最终产品AC不作为P2 HD-018/019的前置或完成条件，避免阶段依赖闭环。
