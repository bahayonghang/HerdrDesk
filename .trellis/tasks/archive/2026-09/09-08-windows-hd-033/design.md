# HD-033 设计

## 拟建产物

- `tests/Integration.Windows/Performance/`：拟建进程/ETW/queue/latency、disposable echo 与 screen-capture marker 探针，归统一 Windows integration project。
- `tests/Integration.Windows/Soak/`：拟建 8h、断连、切换和故障调度场景，归统一 Windows integration project。
- `tests/Integration.Windows/Accessibility/`：拟建 keyboard/Narrator/DPI/主题人工步骤与自动 UIA 辅助。
- `evidence/quality/environment.json`：拟建硬件/软件/commit/配置 manifest。
- `evidence/quality/performance/*.csv|json`：拟建 raw samples 与 derived summary。
- `evidence/quality/accessibility/*.md`、`soak/*.jsonl`：拟建人工矩阵与时间序列。
- `evidence/quality/report.md`：拟建结论、失败、缺陷和 UNVERIFIED 项。

## 测量边界

- Cold start：t0 为用户/测试 harness 发起 packaged app process，t1 为 Shell first-interactive marker；网络投影另计。
- Local input latency：t0 为 `TerminalInputController` 接受已授权输入，t1 为 Windows capture 在 terminal viewport 检出唯一高对比 echo token 像素。
- Remote input latency：相同 t0/t1，另测同时间窗基础 RTT；报告 observed、RTT 和 observed-RTT，不能用公式替代样本。
- Parser consumed：单独记录 xterm callback 时间，用于定位 queue，不命名 render latency。
- State latency：t0 为 DeviceSession 接收/应用事件，t1 为目标 AutomationElement/捕获像素显示新状态。
- Search latency：query admission→稳定 result list Ready；激活/renderer focus 另报。
- Idle CPU：每个采样窗记录进程 user+kernel processor time 原值；总占比为 `Δprocessor_seconds / (Δwall_seconds × logical_cpu_count) × 100`，并跟踪窗内创建/退出的 WebView/bridge 子进程。

## 样本与统计

- 冷启动至少 30 个独立 process 样本，标记首次、warm、缓存与异常；p95 使用排序后的 empirical nearest-rank 并保留所有样本。
- input latency 每种本地/远端至少 500 事件，预热 50 不计；固定 echo token、节奏和输出负载，报告 p50/p95/p99/max。
- search/state 每场景至少 500 事件；断连/切换固定 100 次并逐次记录目标/结果。
- 资源稳态按 1s 采样至少 10min；soak 按 5s 或更细采样 8h，事件/故障点另写 marker。
- idle CPU 先等待 60s 无用户输入/网络重连/文件 job 的稳定期，再做 3 个独立 10min run；后台校准属于真实 idle 工作并保留，外部系统噪声另记录不删样本。
- 每项至少重复 3 个 run；报告 run 间差异，不合并不同硬件/版本成一个 p95。

## 内存与流控观测

- 进程集合按签发/父子关系固定为 HerdDesk App、WebView2 children、自有 bridge/filebridge；herdr/agent 单列。
- 采集 working set、private bytes、managed heap、handles、threads、process count、GC、Core queue bytes、web-message bytes、xterm pending bytes。
- CPU 对同一进程集合采集 PID/start time/raw user+kernel time；PID 复用或子进程 churn 通过 start time 区分，聚合后报告 mean/p95/max 与每组件贡献。
- bytes→MiB 只在报告层换算，明确二进制单位；base64 expansion/JSON string/interop copies 分层直接计数或剖析。
- frame 产生、Core enqueue、Web post、xterm parsed、pixel detected 使用同一 monotonic clock/correlation id；真实内容用 synthetic marker。
- 丢帧/seq gap/reset 数与资源曲线同报，任何“低内存但 delta 丢失”判失败。

## 可访问性与视觉矩阵

```text
Workflows: launch/empty → add local → search → open pane → request/release control
           → create/close confirm → notification jump → files → attach/paste → diagnostics
Inputs: keyboard-only / Narrator + keyboard / mouse
Visual: 100/150/200% DPI × light/dark/high-contrast × supported monitors
States: loading/empty/error/offline/stale/expired/permission/disabled/focus/selected
```

- UIA 辅助检查 name/role/state/focus order；Narrator 可理解性与实际操作由人工记录。
- 颜色对比工具结果只能辅助，不替代 high-contrast 和颜色非唯一人工检查。
- 多显示器移动记录 DPI change、renderer resize rate、最终 cols/rows/candidate anchor；持续 oscillation 为失败。

## Soak 与故障注入

- 代表负载：30 pane 投影、1 visible 为主，定期开 4 visible；状态/terminal 高低输出交替，文件使用 synthetic disposable data。
- 注入：network disconnect、daemon unavailable、remote auth/permission、renderer process crash、bridge EOF、disk full sandbox、file cancel。
- 所有输入/文件写仅到 disposable target；故障脚本有明确 allowlist，绝不枚举/终止用户 daemon/agent。
- 每次恢复验证 observe-first、无 input replay、旧 epoch reject、其他设备继续、临时 job cleanup。

## 结论规则

- 每个 AC 独立 PASS/FAIL/UNVERIFIED；aggregate green 不覆盖某组合未跑。
- 性能失败可提出有证据的并发/预算 ADR；需要改产品目标时回到计划/批准，不能在报告中悄改。
- 发现缺陷按 owning HD task 修复并重跑受影响最小矩阵；无新变化不重复全套。
