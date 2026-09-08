# HD-023 多设备聚合与搜索

## 目标

把多个独立 DeviceSession 的权威投影聚合到同一导航、搜索和通知目标空间，在一台设备失联、旧事件晚到或多设备存在同名资源时仍保持身份、状态和操作严格隔离。

## 当前事实与边界

- `DeviceId` 是稳定 UUID，`SessionKey`/`PaneKey` 含设备，pane ID/标题不能作全局键（`docs/plan/docs/03_架构与数据流.md:78-82`）。
- 起步预算最多 3 个远端设备、4 个可见 pane；搜索不能为每项启动 terminal（`docs/plan/docs/03_架构与数据流.md:52-54`）。
- 本任务来源 `tasks/HD-023.md:5-13`，依赖 HD-011 导航和 HD-022 远端通道。
- 每设备重连独立，旧输入立即丢弃；一设备认证错误不得拖住其他设备（`docs/plan/docs/07_多设备SSH与文件.md:21-25`）。

## 需求

- R1：每个 DeviceSession 维护独立 epoch、connection phase、Store partition、错误、retry 和 capability；聚合层不合并同名实体。
- R2：全局行项目以完整 Device/Session/Workspace/Pane 身份为 key，display label 只用于搜索和 breadcrumb。
- R3：聚合状态覆盖 loading、partial-ready、empty、offline/stale、auth-required、permission denied、incompatible、error 和 cancelling。
- R4：一设备更新、失联或重连不阻塞其他设备投影、搜索、通知与已有 terminal。
- R5：搜索跨最多 3 设备/100 条投影 p95 <100ms，不触发网络请求或创建 terminal bridge。
- R6：搜索结果显示 device/session breadcrumb、freshness、业务/连接状态；过期激活返回 expired，不跳到同名实体。
- R7：多设备快速切换、并发事件、旧 epoch 消息和迟到 renderer/input 下，所有写请求重新校验当前完整 key。
- R8：通知 target 与搜索 target 共用一套 `GlobalEntityRef`/resolver，不各自实现字符串映射。
- R9：聚合排序稳定：设备用户顺序、session、workspace、pane；局部错误不能清空其他 partition。
- R10：诊断可按设备显示连接/同步/terminal/file 状态，但隐藏远端地址、用户名和绝对路径。

## 子任务验收

- [ ] AC1（R5, R9）：3 设备、100 条 mixed 投影的搜索 p95 <100ms，结果数/顺序正确且 terminal 进程数不增加。
- [ ] AC2（R2, R6, R8）：两台设备均含 `w1:p1`，100 次搜索/通知/树切换激活均命中预期完整 key。
- [ ] AC3（R1, R7）：旧 epoch event/ack/input 在 100 次并发切换中零次应用到新 epoch 或其他设备。
- [ ] AC4（R1, R4）：设备 A offline/auth error 时，设备 B/C 导航、搜索、通知与控制状态继续更新。
- [ ] AC5（R3）：partial loading、empty device、stale result、incompatible capability、permission error 都有独立可访问 UI。
- [ ] AC6（R5, R6）：搜索取消/新查询不会让旧结果覆盖新结果；激活前再次 resolve target freshness。
- [ ] AC7（R2, R8）：删除/重命名 device label 不改变稳定 key；最近和通知旧引用显示 expired 或新 label，不串目标。
- [ ] AC8（R9, R10）：聚合层不持久化 terminal 内容或复制业务状态权威，只保存可重建索引，诊断保持脱敏。

## 与产品 AC 的映射

- AC19（`planning/acceptance.json:149-154`）：本任务贡献跨 3 设备搜索、聚焦和无额外 bridge；最终汇总归 HD-026。
- AC21（`planning/acceptance.json:165-170`）：本任务贡献同名身份和旧消息隔离；最终多设备验收归 HD-026。
- AC18：复用并回归 HD-012 notification target，最终归 HD-012。
- AC26：贡献 UI/聚合独立失败表现；重连退避最终归 HD-024/HD-026。

## 非目标

- 不实现 SSH argv、host-key、认证 prompt、重连算法或 transport；分别归 HD-020/022/024。
- 不提升远端设备或可见 pane 产品预算，不建立中心数据库或云状态源。
- 不让聚合层直接发送 terminal input、文件操作或资源 mutation。
