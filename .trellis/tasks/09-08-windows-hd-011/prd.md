# HD-011 本地导航与搜索

## 目标

建立 HerdDesk 主窗口壳、本地 Device → Session → Workspace → Pane 导航、Ctrl+K 搜索、最近访问、设置、诊断与关于入口，使用户在连接、空、失败、过期和能力不足时仍能明确知道当前目标和下一步。

## 当前事实与边界

- 产品界面固定为设备/session 区、workspace/pane 树、中央 terminal、可收起详情/文件区（`docs/plan/docs/02_产品定义与名称.md:37-51`）。
- 本任务来源 `tasks/HD-011.md:5-13`；源 backlog 依赖 HD-009，完整实施还须集成 HD-010 的同步/重连投影，且不自行解析 RPC、启动 terminal 或拼 SSH 命令。
- 当前 App 尚未建仓，以下所有 `src/HerdDesk.App` 路径均为拟建。
- 编译契约已有 `DeviceId`、`SessionKey`、`PaneKey` 和 `ConnectionEpoch`（`src/HerdDesk.Contracts/TerminalModels.cs:4-9`），不得另建字符串全局键。

## 需求

- R1：主窗口始终显示当前 device/session/pane、连接状态、agent 业务状态、未读与控制权；四类状态独立建模。
- R2：树层级不得把 session 等同 workspace，同名 pane 必须以完整 `PaneKey` 隔离。
- R3：启动覆盖 Starting、Ready、NoDevices、DaemonUnavailable、Failed；网络连接不得阻塞壳显示。
- R4：每层覆盖 loading、empty、error、offline/stale、incompatible、disabled、selected、hover、focus-visible 状态。
- R5：Ctrl+K 搜索名称、类型、设备和最近项；搜索只索引 Store 投影，不创建隐藏 terminal bridge。
- R6：搜索选择在目标 renderer Ready 后聚焦；目标关闭或 epoch 变化时显示过期，不猜同名替代项。
- R7：Settings、Diagnostics、About 是完整稳定路由；本任务通过 HD-007 配置端口完成本地 Device、named session/explicit endpoint、主题/通知/诊断隐私设置的编辑与原子保存，HD-020 只扩展 SSH profile/身份/host-key。
- R8：支持全键盘树导航、焦点恢复、屏幕阅读器名称、中文本地化、语义色和 100/150/200% DPI。
- R9：窄窗口用可关闭的导航/详情 overlay，宽窗口呈现四区；具体 breakpoint 在首次可视 spike 后锁定。
- R10：最近访问只存稳定身份引用和时间，不存 ANSI、输入、凭据；不存在目标在列表中显示过期并可移除。
- R11：App 使用 Windows 支持的单实例激活/重定向机制；第二次启动和通知激活路由到已有窗口，通知只导航，不执行命令或输入。
- R12：Settings 提供 terminal font family/font size/zoom 的编辑、预览、保存和恢复；HD-014/015 消费，观察态只改变本地显示且不得越权发送 resize。
- R13：启动取消/退出按顺序取消待激活/导航、停止新连接、释放 renderer 与本应用 child processes、保存可保存配置；不停止 daemon/agent/pane。

## 子任务验收

- [ ] AC1（R3, R7）：空配置启动时 2.5s 预算内显示壳和“添加/诊断”入口，且不假装 daemon 在线。
- [ ] AC2（R1, R2, R4, R8）：树选择、折叠、键盘上下级导航和焦点视觉在本地 30 pane 投影下稳定。
- [ ] AC3（R5）：100 条本地投影搜索 p95 <100ms；没有因此创建 terminal 进程。
- [ ] AC4（R2, R6, R10）：同名 workspace/pane 的选择结果保留完整 `DeviceId/SessionKey/PaneKey`，最近/过期项不串目标。
- [ ] AC5（R1, R4, R7, R8）：loading/error/offline/stale/incompatible 分别有文本、图标与可执行恢复动作；颜色不是唯一信息。
- [ ] AC6（R5, R8）：Ctrl+K 在 IME composition 期间不拦截；搜索关闭后焦点返回原控件。
- [ ] AC7（R3, R7）：空设备可在 Settings 创建本地 Device、添加多个 named session/explicit endpoint、保存并分别连接；编辑一个 session 不覆盖其他 session，保存失败恢复原值且显示诊断，不等待 HD-020。
- [ ] AC8（R7）：Diagnostics 能显示版本/连接/epoch/queue/错误类别，生成默认脱敏导出预览，用户确认后才导出；About 显示独立客户端声明与实际版本。
- [ ] AC9（R4, R7）：Settings/Diagnostics/About 在 provider unavailable/permission/error 时仍可打开并解释，不触发隐式写入或泄漏。
- [ ] AC10（R8, R9）：窄/宽窗口、深浅色、高对比和字体缩放无内容不可达；native visual 结果留 L3 证据。
- [ ] AC11（R7, R11）：并发启动两个 App 时只有一个配置写 owner；第二个 activation 被现有窗口接收且 target 重新解析，不能靠进程内锁伪装跨进程互斥。
- [ ] AC12（R12）：font/size/zoom 保存失败恢复最后有效值；观察态修改 100 次 upstream resize=0，控制态仅经 capability/lease 发有效 resize。
- [ ] AC13（R13）：正常退出、启动中取消、通知激活后退出均只释放本应用 renderer/连接/child processes，已有 herdr pane 继续。

## 与产品 AC 的映射

- AC19（`planning/acceptance.json:149-154`）：本任务拥有本地导航、100 条投影、聚焦与无额外 bridge 的基础；HD-023 贡献多设备聚合，最终汇总归 HD-026。
- AC11：贡献能力不足时按钮 disabled + reason；兼容性解析最终归 HD-009/HD-010。
- AC18：贡献 deep-link 路由和 expired target UI；通知语义最终归 HD-012。
- AC15：贡献 App 退出/child ownership UI 生命周期，完整产品进程所有权最终归 HD-026。
- AC37/AC38：贡献 shell 的键盘、语义状态、主题和 DPI；最终整体验收归 HD-033。

## 非目标

- 不实现 terminal 渲染、控制权、远端 SSH、文件传输或系统通知语义。
- 不支持任意多窗口编辑同一配置；单实例 redirect 是 v1 的配置/激活边界。
- 不把 Settings 变成任意命令/路径编辑器；本地 endpoint 使用 HD-007 的类型化配置端口，ViewModel 不保存密码或拼接 argv。SSH profile/host-key 表单扩展归 HD-020。
- 不复制 herdrm 布局；四区信息架构是行为基线，不要求像素复刻。
