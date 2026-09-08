# HD-023 设计

## 拟建文件责任

- `src/HerdDesk.Core/Aggregation/GlobalProjectionStore.cs`：拟建只读 partition 聚合和增量索引。
- `src/HerdDesk.Core/Aggregation/GlobalEntityRef.cs`：拟建 discriminated reference，内部携完整 identity。
- `src/HerdDesk.Core/Aggregation/GlobalTargetResolver.cs`：拟建 freshness/epoch/exists 重解析。
- `src/HerdDesk.Core/Aggregation/GlobalSearchIndex.cs`：拟建 100+ 项本地索引和稳定排序。
- `src/HerdDesk.App/ViewModels/MultiDeviceNavigationViewModel.cs`：拟建 partial/offline/auth/error 状态组合。
- `src/HerdDesk.App/ViewModels/GlobalSearchViewModel.cs`：拟建对 HD-011 search palette 的多设备 provider。
- `src/HerdDesk.App/Controls/DeviceConnectionSummary.xaml`：拟建设备状态/重试/诊断入口。
- `tests/Unit/HerdDesk.Core.Tests/Aggregation/`、`tests/Integration.Windows/Components/MultiDevice/`：拟建 unit/Windows component 测试。

## 聚合模型

- `DevicePartitionKey = DeviceId`；partition 值含 connection epoch、projection generation、connection phase、capabilities 和 immutable entity map。
- `GlobalEntityRef` variant 为 Device/Session/Workspace/Pane/Agent，保存对应 typed key + entity server id；不保存可执行行为。
- `FreshnessStamp` 含 device epoch + partition generation；跨设备不比较数值大小。
- `AggregateReadiness = Empty | Loading | PartialReady | Ready | Degraded`；Degraded 保留健康 partition。
- `SearchDocument` 含 ref、规范化 label、kind、breadcrumb、business status、connection status、recent rank；不含 ANSI。

## 数据流

1. 每个 DeviceSession actor 发布不可变 partition snapshot/delta。
2. Aggregator 按 DeviceId 替换对应 partition，并增量更新 search documents。
3. HD-011/012 只读取 aggregate view；用户激活结果交给 resolver。
4. Resolver 查询当前 partition，核对 target exists、epoch/freshness/capability，返回 Resolved/Expired/Offline/Incompatible。
5. 任何写意图继续交给 owning Core service 重新校验；resolver 结果本身不授予控制或权限。

## UI 组合

```text
DeviceSessionRail
├─ AggregateHeader(Ready 2/3, unread, diagnostics)
└─ DeviceGroup × N
   ├─ ConnectionSummary(Loading/Ready/Stale/Auth/Error)
   └─ Session/Workspace/Pane children

SearchPalette
├─ Scope(All devices / current device)
├─ PartialResultsBanner
└─ Result(device breadcrumb + freshness + status)
```

- 某 partition loading 时显示 partial results banner，不把结果总数当完整。
- auth-required/host-key/permission 的动作只导航 HD-020 settings/diagnostics provider，不在聚合 ViewModel 读取密码。
- offline 结果允许查看已缓存元数据/诊断，terminal 写与 file 操作 disabled 并给 reason。
- focus/selection 使用单一 coordinator；新选择取消旧待聚焦请求。

## 命令和取消

- `SetSearchScope`、`UpdateQuery`、`CancelSearch`、`ActivateGlobalResult`、`SelectDevice`、`OpenDeviceDiagnostics`、`RequestReconnect`。
- `RequestReconnect` 调用 HD-024 provider，并显示 scheduled/auth-blocked；不在 UI 循环 retry。
- 每次 query 有 generation token；迟到结果不覆盖新 query。
- 设备移除只清本地 partition/index；远端 session/pane 不删除。

## 错误与安全

- 单设备错误保存 device scoped category；aggregate error 只用于聚合器自身故障。
- 错误文案区分 network/auth/host-key/permission/protocol/incompatible/stale，不显示原始未脱敏 stderr。
- 旧 epoch 消息在进入聚合层前由 DeviceSession 拒绝，聚合层仍检查 partition epoch 作第二边界。
- 目标 breadcrumb 至少含 device + session + workspace + pane；颜色只辅助连接状态。

## 性能策略

- 100 条规模优先内存不可变索引，无需数据库、后台 terminal 或网络搜索。
- normalized label 在 partition 更新时计算，query 只评分/排序；取消检查在批次间执行。
- 测量搜索本身和激活 resolve 分开；记录 p50/p95/max、GC 和 fixture size。
