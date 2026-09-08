# HD-023 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start。
- [ ] HD-011 提供稳定 navigation/search provider seam；HD-012 提供 notification target seam。
- [ ] HD-022（`.trellis/tasks/09-08-windows-hd-022`）提供 per-device Store/epoch/connection phase，不在本任务实现 SSH。
- [ ] HD-024 的 auth/retry 状态枚举可被 UI 消费；provider 未完成时保持 disabled/UNVERIFIED。

## 顺序清单

- [ ] 定义 DevicePartition、GlobalEntityRef、FreshnessStamp 和 resolver result。
- [ ] 实现 immutable partition replacement 和同 DeviceId epoch gate。
- [ ] 实现增量 search index、稳定排序、scope 与 query cancellation。
- [ ] 适配 HD-011 tree/search 和 HD-012 notification target，不复制 identity logic。
- [ ] 实现 partial/loading/offline/auth/incompatible/error UI 和诊断路由。
- [ ] 建立 3-device/100-item/same-name/old-epoch/concurrent-event fixtures。
- [ ] 跑 L1 identity、resolver、search latency 和 no-terminal-process 测试。
- [ ] 跑 L2 Windows 多进程/SSH disposable devices 的独立断连与恢复。
- [ ] L3 人工跑键盘、screen reader、focus、通知和快速切换 100 次。
- [ ] 把证据交 HD-026；本任务不提前改 AC19/AC21。

## 拟建测试路径

- `tests/Unit/HerdDesk.Core.Tests/Aggregation/PartitionIsolationTests.cs`：per-device epoch 和局部失败。
- `tests/Unit/HerdDesk.Core.Tests/Aggregation/GlobalTargetResolverTests.cs`：same w1:p1、expired、rename、remove。
- `tests/Unit/HerdDesk.Core.Tests/Aggregation/GlobalSearchPerformanceTests.cs`：3 devices/100 items/p95。
- `tests/Unit/HerdDesk.Core.Tests/Aggregation/OldEpochConcurrencyTests.cs`：100 switch/event/ack/input scenarios。
- `tests/Integration.Windows/Components/MultiDevice/PartialStateTests.cs`：loading/offline/auth/error/empty。
- `tests/Integration.Windows/MultiDevice/IsolationScenarios.md`：真实通道和快速切换。

## 命令和证据

- [ ] `[现有] rtk proxy just ci`：现有 G0 gate；不证明远端/多设备。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj -c Release --filter Aggregation`：L1。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter MultiDevice`：L2 component + disposable devices。
- [ ] 性能证据记录硬件、runtime、fixture、warmup、sample 数和原始结果；单次 stopwatch 不算 p95。
- [ ] L3 记录每次目标完整 key 的脱敏映射，不能只写“肉眼正确”。

## 回滚与结束门

- [ ] 聚合缺陷可关闭全局搜索，回退 device-scoped 导航；健康设备 Store 不重建。
- [ ] 一设备断开只释放本应用对应连接，不停止 daemon/agent。
- [ ] 任何跨设备错写均为发布阻断，修复前保持 AC21 not_run/failed。
- [ ] HD-026 独立复核 AC19/AC21 完整证据后才可申请状态变更。
