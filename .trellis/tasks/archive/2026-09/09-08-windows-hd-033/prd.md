# HD-033 性能、可访问性与 soak

## 目标

在统一 Windows 11 参考环境对完整 1.0 客户端做端到端性能、资源回收、无障碍、DPI/主题和 8 小时稳定性验收，保留原始数据与失败注入证据，并修复阻断发布的关键问题。

## 当前事实与边界

- 所有性能数字仍是规划目标、尚未实测；参考机和负载定义见 `docs/plan/docs/10_测试与验收.md:15-37`。
- 本任务来源 `tasks/HD-033.md:5-13`，依赖 HD-026 多设备验收与 HD-032 文件安全验收。
- 发布需 L3/L4；hosted Windows CI 不等于交互桌面，IME/Narrator 必须真机（`docs/plan/docs/10_测试与验收.md:3-13`）。
- xterm write callback 仅表示 parser consumed，不是 GPU/屏幕呈现；输入→呈现必须用可见像素或等价端到端探针。

## 需求

- R1：参考环境至少 8 logical CPU、16GiB、SSD、Windows 11 x64，记录 CPU/GPU/OS/App SDK/WebView2/Display/DPI/电源模式。
- R2：基准负载为 30 pane 投影、1 可见 terminal；扩展为 4 可见 terminal、最多 3 远端设备，并记录 RTT/丢包/带宽。
- R3：冷启动 p95 ≤2.5s，网络不可阻塞壳显示；首次安装/runtime setup 单独报告。
- R4：本地 input admission→捕获到目标可见像素 p95 ≤100ms；远端 p95 ≤基础 RTT+150ms；不得以 parser callback 作为终点。
- R5：全局搜索 100 投影 p95 ≤100ms，状态事件→UI 可见状态 p95 ≤500ms；样本、warmup 和统计方法可复算。
- R6：App+WebView2+自有 bridge 聚合 working set：1 可见 pane ≤500MiB、4 pane ≤900MiB；同时报告 private bytes、handles、process count。
- R7：所有大小以 bytes 采集，报告时注明 MiB=1,048,576 bytes；直接测量 base64/JSON/managed/native/WebView 副本和 queue peak，不用载荷比例推导总内存。
- R8：连接/关闭 pane view 100 次，等待预先定义的稳定窗口后句柄回到基线±5%，无持续工作集/进程增长。
- R9：键盘与 Narrator 完成搜索、控制、释放、关闭确认；loading/empty/error/offline/expired/permission/disabled/focus 状态可辨且颜色非唯一。
- R10：100/150/200% DPI、多显示器、深浅色、高对比、字体缩放通过，无持续 resize 抖动或焦点丢失。
- R11：8 小时代表负载覆盖 terminal/状态/文件队列；100 次断连/切换和故障注入不破坏用户 pane 存活。
- R12：每项输出 raw JSON/CSV、环境 manifest、步骤、预期/实际、commit、defect、证据级别；未跑项不写 Passed。
- R13：参考机稳定空闲窗口内 App+WebView2 children+自有 bridge 总 CPU 占比 ≤1.5%；采集 raw user/kernel processor time，按 wall time×logical CPU 归一化，herdr/agent 单列。

## 子任务验收

- [ ] AC1（R1, R2, R3, R12）：至少 30 次独立冷启动给出原始样本、empirical p50/p95/max，满足 ≤2.5s 或记录阻断缺陷。
- [ ] AC2（R1, R2, R4, R12）：至少 500 个本地和 500 个远端 disposable echo 事件用屏幕捕获标记测端到端 latency，满足目标并另报 parser consumed 时延。
- [ ] AC3（R2, R6, R7）：1/4 visible pane 各 10 分钟稳态采样与高输出峰值证明工作集/queue 有界，未丢 delta 假正常。
- [ ] AC4（R8）：100 次 open/close 记录每轮 process/handle/private bytes/working set，稳定后 handles 在±5%且趋势无持续增长。
- [ ] AC5（R9）：Narrator/键盘逐步完成指定任务；所有关键状态有文本/AutomationProperties/focus visual。
- [ ] AC6（R10）：DPI/主题矩阵逐组合截图/步骤/结果，resize 事件速率和最终行列稳定。
- [ ] AC7（R11, R12）：8h soak 原始时间序列无无界增长；100 次断连/切换、renderer crash、网络/权限/磁盘失败后 pane 存活。
- [ ] AC8（R5, R6, R7, R8, R11）：失败先保正确性，允许降低并发/关闭实验 renderer，但不得丢帧、吞输入或停止 user daemon 美化指标。
- [ ] AC9（R1, R2, R12, R13）：至少 3 个 10 分钟 idle run 在 60 秒稳定期后按 1 秒采样，保留逐进程 raw processor time/process lifetime，聚合归一化 p95/mean 均可复算且目标 ≤1.5%。

## 与产品 AC 的映射

- AC27/AC28/AC29（`planning/acceptance.json:213-234`）：本任务最终拥有背压限额、性能预算和资源回收。
- AC37/AC38（`planning/acceptance.json:293-306`）：本任务最终拥有可访问性、DPI 和主题。
- AC46（`planning/acceptance.json:365-370`）：本任务最终拥有 8h/100 次稳定性。
- AC08/AC09/AC19/AC21/AC31/AC33/AC35/AC36：只做完整产品回归；最终 owner 仍按父 acceptance map，不因本任务通过自动改写。

## 非目标

- 不用平均值替代 p95/long tail，不用 source inspection、mock、parser callback 或 hosted CI 替代 native visual evidence。
- 不扩大支持 SKU、agent/version、renderer 或远端平台矩阵；只验证发布候选声明的组合。
- 不把 herdr/agent 资源混入 App 预算后隐藏，也不停止用户 pane 作为回滚。
