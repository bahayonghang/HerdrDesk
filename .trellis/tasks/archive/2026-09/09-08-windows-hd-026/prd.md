# HD-026 · 多设备 MVP 验收

## 目标与事实

在 HD-023/024/025 之后，用 Windows 本地和 Linux 远端完成多设备集成门。
源任务要求同名 `w1:p1` 的两设备100次无串写、明确SSH支持矩阵、透明流和
隔离退避：`tasks/HD-026.md:19-30`。当前Linux远端为not_run，不得外推
macOS/ARM64：`evidence/compatibility-baseline.json:21-25`。

## 需求

- R1：完整DeviceId/SessionKey/PaneKey/ConnectionEpoch隔离；两设备同名pane、
  同设备两个named session及旧epoch并发消息均不串目标。
- R2：逐组合验证alias/port/identity/agent/ProxyJump；未知host走人工核验，
  host-key变更阻断，argv与日志不含密码；未支持认证模式清楚可见。
- R3：RPC与terminal均以`ssh -T`透明stdio连接；stdout banner明确失败，
  stderr不能当作NDJSON；SessionKey拥有自己的request/event pair与pending。
- R4：单session及单设备故障不阻塞其他对象；退避有jitter，认证错误不无限重试，
  每SessionKey仅有HD-018定义的DeviceSession恢复timer/attempt。
- R5：EOF、daemon重启和真实网络中断后显示stale；RPC收敛后terminal重新observe，
  获取新epoch完整baseline，旧控制权不继承。
- R6：网络边界前后的排队输入永不自动重发，UI区分NotSent和ResultUnknown；
  GUI正常关闭/崩溃只释放本应用持有的bridge/SSH子进程，远端daemon及既有agent存活。
- R7：三台独立DeviceId合计100条投影搜索p95 < 100 ms，正确聚焦，不为列表启动100个terminal桥。
- R8：支持矩阵按OS/arch和认证组合分别记录；macOS/ARM64只有独立环境完成同等矩阵
  后才列为支持，缺证据保持experimental或unsupported。

## 原 AC 映射与补充最终责任

源HD-026的AC贡献保持AC21/23/24/26。父计划补充最终汇总AC13/14/15/19/22；
不修改`planning/acceptance.json`的原始条款。

- AC13/14/15：接收HD-013/016/018/019本地证据，补齐HD-022/024真实SSH断网、
  输入不重放和SSH子进程生命周期证据，覆盖承诺支持的平台矩阵。
- AC19：接收HD-011/023的投影搜索实现，完成三设备100条性能及正确聚焦验收。
- AC21/22/23/24/26：跨目标隔离、主机身份、SSH配置、透明流和多设备恢复最终门。

HD-018/019完成条件为P2本地贡献，不能反向等待本任务的SSH证据；原AC13/14/15
在本任务汇总前保持未完成。子任务贡献完成与完整产品AC通过分别记录。

## 边界和撤销

实验仅用明确授权的隔离设备/会话。host-key变更、断网或崩溃按执行卡恢复本应用
连接，不停止用户daemon，不触碰生产会话。普通SSH成功不能证明MFA或未测平台可用。
