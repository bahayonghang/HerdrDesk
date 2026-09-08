# HD-012 通知与未读

## 目标

从 HD-010 收敛后的权威投影生成一致的 blocked/done 注意力状态、未读计数、应用内通知与 Windows 通知，并将点击安全定位到原 device/session/pane。

## 当前事实与边界

- agent 业务状态、连接状态、未读和控制权必须分开；`done` 不等于成功，未知状态显示未知（`docs/plan/docs/02_产品定义与名称.md:39-41`）。
- 首次 snapshot 建立基线且不触发历史 done，失联显示 stale，不能保持在线（`docs/plan/docs/03_架构与数据流.md:84-106`）。
- 本任务来源 `tasks/HD-012.md:5-13`，依赖 HD-010 的同步语义与 HD-011 的导航路由。
- 系统通知点击只携定位信息，不携待执行命令（`docs/plan/docs/08_安全与威胁模型.md:20-24`）。

## 需求

- R1：以完整 `PaneKey` 加 agent 实体标识作为注意力键，不用 pane ID、标题或 agent 类型单独定位。
- R2：首次同步、重连基线和旧 epoch 事件只更新基线，不补发历史完成通知。
- R3：只对确认的业务状态转换发通知；blocked/done/idle/unknown 保留原义，done 文案不得写“成功”。
- R4：同一转换按稳定 transition key 去重和限流；dirty 重读造成的重复投影不得重复 toast。
- R5：未读状态在导航层级向上聚合，但阅读/清除一项不得清除另一设备或另一个同名 pane。
- R6：连接 stale/offline 时不得从旧投影产生“仍在运行/已成功”通知；既有通知显示过期上下文。
- R7：支持全局、每设备和每 session 的静默/限流偏好；偏好不改变 Core 业务状态。
- R8：通知中心覆盖 loading、empty、error、offline、expired、muted、unread/read、hover、selection、focus。
- R9：点击通知先由当前 Store 解析目标，等待对应内容 Ready 后聚焦；目标已关闭则显示过期，不跳同 ID 替代项。
- R10：诊断只记录 transition 类别、脱敏 identity、epoch、抑制原因和投递结果，不记录 terminal 正文或用户输入。

## 子任务验收

- [ ] AC1（R2）：initial snapshot 和 100 次重连均不补发历史 done/blocked toast。
- [ ] AC2（R4）：相同转换在事件、dirty refresh、重复 snapshot 交错下只发一次。
- [ ] AC3（R3）：blocked→working→done 顺序产生独立语义；unknown 值不崩溃、不映射 idle。
- [ ] AC4（R6）：一设备 stale 不产生成功暗示，其他设备通知仍可用。
- [ ] AC5（R1, R5）：未读计数在 pane/session/device 层逐级聚合并可按精确范围清除。
- [ ] AC6（R1, R9）：系统通知和应用内项都能定位完整目标；关闭目标显示 expired，不串到同名设备。
- [ ] AC7（R7, R8）：quiet/muted/限流对 toast 生效但通知中心保留可解释记录；屏幕阅读器可读状态和数量。
- [ ] AC8（R9, R10）：通知不可触发 takeover、输入、资源关闭或任意 deep link 命令，诊断保持脱敏。

## 与产品 AC 的映射

- AC17（`planning/acceptance.json:133-138`）：本任务最终拥有通知一致性、基线、去重与失联语义。
- AC18（`planning/acceptance.json:141-146`）：本任务最终拥有通知目标；HD-011 提供导航路由，HD-023 补充多设备隔离。
- AC12：依赖并复核同步竞态，但最终归 HD-010。
- AC37：贡献通知中心键盘与屏幕阅读器行为，最终整体验收归 HD-033。

## 非目标

- 不推断权限请求，不自动批准 agent，不从终端文本解析“完成”。
- 不保证无持久事件游标时恢复所有瞬态转换；产品需如实说明缺口。
- 不在本任务实现安装包 toast 注册、远程推送或云同步；打包整合由 HD-034。
