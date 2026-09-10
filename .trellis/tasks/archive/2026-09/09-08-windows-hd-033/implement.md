# HD-033 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start，候选 commit/版本/支持矩阵冻结。
- [ ] HD-026 与 HD-032 已完成其真实多设备/文件验收；未满足则本任务不伪造替代证据。
- [ ] 参考 Windows 11、交互桌面、Narrator、多 DPI/显示器和受控远端/网络/磁盘故障资源就绪。
- [ ] 所有 synthetic input/file/daemon target 有 allowlist 和停止条件，不接触用户真实训练/agent。

## 顺序清单

- [ ] 冻结 environment manifest、负载、样本数、时钟、统计、单位和 process ownership。
- [ ] 建立 raw schema/correlation id，先验证采集器开销和时钟一致性。
- [ ] 实现 cold-start/search/state/queue/process 采集。
- [ ] 实现 IdleCpu raw processor-time sampler、logical-CPU 归一化、child process lifetime 和 3×10min 场景。
- [ ] 实现 visible pixel latency probe；明确 parser-consumed 指标另列。
- [ ] 实现 1/4 visible pane 资源与高输出/slow renderer 场景。
- [ ] 实现 100 次 open/close、稳定等待与 handle/process 泄漏检查。
- [ ] 执行 keyboard/Narrator 全工作流和所有关键状态审计。
- [ ] 执行 DPI/monitor/theme/high-contrast 矩阵并记录 resize/candidate focus。
- [ ] 执行 8h soak、100 次 disconnect/switch 和 fault injection。
- [ ] 对失败路由 owning task 做最小修复与定向重跑，再跑受影响综合项。
- [ ] 生成可复算 summary/report，独立审查后才申请 AC 状态更新。

## 拟建测试/证据路径

- `tests/Integration.Windows/Performance/Scenarios/ColdStart.json`：30+ launch samples。
- `tests/Integration.Windows/Performance/Scenarios/VisibleInputLatency.json`：local/remote 500+ samples。
- `tests/Integration.Windows/Performance/Scenarios/VisiblePaneMemory.json`：1/4 pane 与副本/queue 指标。
- `tests/Integration.Windows/Performance/Scenarios/OpenClose100.json`：process/handle baseline。
- `tests/Integration.Windows/Performance/Scenarios/IdleCpu.json`：60s stabilize、3×10min、1s raw processor samples。
- `tests/Integration.Windows/Accessibility/KeyboardNarratorMatrix.md`：完整 workflow。
- `tests/Integration.Windows/Accessibility/DpiThemeMonitorMatrix.md`：100/150/200% 与显示器/主题。
- `tests/Integration.Windows/Soak/Scenarios/EightHourRepresentative.json`：负载/故障计划。
- `evidence/quality/report.md`：结论和 AC→raw evidence 链。

## 命令和证据层级

- [ ] `[现有] rtk proxy just ci`：L0/L1 回归；不证明 GUI/IME/性能/soak。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Performance`：L2/L3 性能采集；scenario/output 由 versioned runsettings 指定。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Soak`：L4；8h duration/output 由 versioned runsettings 指定。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release`：L2 完整集；交互项应外部矩阵，不得假 passed。
- [ ] 人工 L3 表记录实际版本、操作员、日期、截图/录像/原始 capture ref、预期/实际/defect。

## 回滚与结束门

- [ ] 失败时先禁用实验 renderer、降低可见 pane 或相关功能；不丢 delta、不吞 input、不停止 daemon。
- [ ] fault harness 只终止自有受控进程/网络代理/临时目录；退出后核对用户 pane 存活。
- [ ] 仅重跑代码或配置变化影响的场景；最终 release candidate 需一次全范围 L3/L4。
- [ ] AC27/28/29/37/38/46 分项证据齐全才可申请 passed；任何缺环境项保持 UNVERIFIED/not_run。
