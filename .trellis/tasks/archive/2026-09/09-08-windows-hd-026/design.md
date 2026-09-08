# HD-026 设计

## 测试环境与产物

Windows本地加Linux远端至少组成两设备碰撞环境；AC19另需第三个独立DeviceId，
不能把同一设备的两个session计作三设备。至少一台设备配置两个named session，
验证同host不同endpoint各自的RPC pair、pending、epoch和恢复attempt。

环境记录DeviceId/SessionKey、OS/arch、herdr/bridge/renderer版本、SSH组合、
endpoint、网络RTT/带宽/丢包和测试SHA。证据采用现有格式及普通场景表，不新增
状态数据库。输出`evidence/multi-device-mvp/`脱敏原始采集及`docs/testing/multi-device-mvp.md`
支持矩阵（均为未来产物），逐AC记录pass/failed/not_run及原始数据路径。

## 执行卡

| 场景 | 操作与可观察预期 | 证据和实现贡献 |
|---|---|---|
| AC21 identity collision | 两设备均有w1:p1；100次切换与并发旧消息零串写；同设备两个named session独立 | HD-023；含完整key/epoch的操作timeline |
| AC22/23 SSH identity/config | alias/port/identity/agent/ProxyJump逐组合；未知host核验、变更阻断；argv/log不含密码 | HD-020/024；effective config与脱敏host-key outcome |
| AC24 transparent stream | RPC/terminal使用-T；stdout banner协议失败；stderr仅诊断 | HD-022；argv及分流capture，污染负例 |
| AC13 local/remote recovery | 继承P2本地EOF/daemon证据；真实切断SSH网络并恢复，stale→RPC收敛→新observe/full baseline | HD-018/019/022/024；前后epoch、access、seq及UI状态 |
| AC14 no replay | 控制授权下在断网前排队、写入不确定点、断网后各提交无秘密测试标记；重连不出现自动重发 | HD-016/018/022；发送attempt/接收计数、NotSent或ResultUnknown状态 |
| AC15 ownership | 正常关GUI、强制崩溃分别检查本地bridge/ssh句柄释放，远端daemon/原agent PID与心跳继续 | HD-013/018/019/022；启动归属ledger和关闭前后进程证据 |
| AC26 isolation/retry | 逐个session和单设备断网/EOF/认证失败；其他连接仍工作，timer唯一、jitter有界、认证阻断等待用户修正 | HD-024；多个SessionKey的retry timeline |
| AC19 search | 三DeviceId/100条投影，预热后重复固定查询集记录每次耗时并计算p95 <100ms，聚焦目标正确 | HD-011/023；原始timing、查询命中key、terminal桥计数 |
| budget/platform | 同候选记录HD-025网络/队列预算；每OS/arch独立支持行 | HD-025/026；不得从Linux外推macOS/ARM64 |

AC14使用唯一无敏感标记，验证客户端没有重放attempt并对照端点接收记录；
断网前已成功到达的一次输入允许存在，不因ResultUnknown在重连后补发。
AC15按直接子进程handle/启动记录归属回收；不能按通用进程名杀进程或遍历未知daemon树。
GUI崩溃实验须独立监督脚本采样，不能仅靠GUI在退出时自报成功。

## 自动化与人工边界

使用父`research/test-layout.md`规定的
`tests/Integration.Ssh/HerdDesk.Integration.Ssh.csproj`（HD-020创建）扩展
Aggregation/Auth/Transport/Recovery/Ownership场景；本地Shell/生命周期复跑进入
`tests/Integration.Windows/HerdDesk.Integration.Windows.csproj`。
搜索算法回归复用Core/App单测，实际三设备UI聚焦与计时属于L3。
`tests/E2E/`只放场景脚本/执行卡，不放未接入工程的孤立C#测试。

远端argv遵守HD-020/022固定调用与POSIX引用合同；不会由terminal output拼接命令，
源约束见`docs/plan/docs/07_多设备SSH与文件.md:9-17`。故障注入的启动/回收脚本、
构建入口和失败退出传播在实现时接入现有测试门；离线编译不记录为真实SSH执行。

## 完成门与失败回流

先满足HD-023/024/025实施前置，再执行矩阵并接收P2本地贡献。
任何网络恢复、跨session隔离、主机身份或daemon存活失败都阻断P3门，回到对应
实现任务修复后只复跑受影响矩阵。未测平台不得进入stable支持表；所有承诺范围的
AC13/14/15/19/21/22/23/24/26证据齐全才汇总最终结果。
