# HD-012 设计

## 拟建文件责任

- `src/HerdDesk.Core/Attention/AttentionReducer.cs`：拟建纯 reducer，接收收敛投影与同步边界。
- `src/HerdDesk.Core/Attention/AttentionState.cs`：拟建 baseline、transition key、read marker 与 aggregation 类型。
- `src/HerdDesk.Core/Attention/NotificationPolicy.cs`：拟建去重、限流、静默和 stale 抑制决策。
- `src/HerdDesk.App/Notifications/INotificationSink.cs`：拟建应用内/Windows sink 的窄端口。
- `src/HerdDesk.App/Notifications/WindowsNotificationSink.cs`：拟建系统 toast adapter，不含业务判断。
- `src/HerdDesk.App/ViewModels/NotificationCenterViewModel.cs`：拟建列表、筛选、读取和过期状态。
- `src/HerdDesk.App/Controls/NotificationCenter.xaml`、`AttentionBadge.xaml`：拟建 UI。
- `tests/Unit/HerdDesk.Core.Tests/Attention/`、`tests/Unit/HerdDesk.App.Tests/Notifications/`：拟建测试。

## 数据模型

- `AttentionKey`：完整 `PaneKey` + server entity kind/id；目标身份不从标题派生。
- `ProjectionStamp`：`ConnectionEpoch` + baseline generation + observedAt；只在同一 device/session 内比较。
- `BusinessState`：`Working | Blocked | Done | Idle | Unknown(raw)`，Unknown 保存原值供诊断。
- `AttentionTransition`：key、from、to、stamp、transitionId、isBaseline、freshness。
- `NotificationDecision`：`Deliver | CenterOnly | Suppress(reason)`；reason 包含 baseline/reconnect/duplicate/stale/muted/rate_limited。
- `NotificationTarget`：完整 key、投影 epoch、route version；不含 executable action。
- `ReadMarker`：最后阅读的 transitionId；聚合值由 children 派生，不保存第二份可漂移计数。

## 状态流

1. HD-010 宣告 baseline begin；Reducer 安装 snapshot 前不产生 deliverable transition。
2. baseline complete 后记录每实体当前状态，清除旧 epoch 的 pending transition。
3. 新投影与相同 epoch baseline 比较，生成候选 transition。
4. Policy 检查 stale、duplicate、mute、rate limit，输出 decision。
5. Center store 持久化最小元数据并更新未读；Windows sink 仅处理 Deliver。
6. deep-link 点击交给 HD-011 router，router 以当前 Store 重新解析；失败返回 Expired。

## 组件树与 UI 状态

```text
AppTitleBar
└─ NotificationButton(unread count, muted state)
   └─ NotificationCenter
      ├─ Filter(All/Blocked/Done/Unread)
      ├─ StateBanner(Loading/Offline/Error/Muted)
      ├─ EmptyState
      └─ NotificationList
         └─ NotificationItem(Read/Unread/Expired/Selected)
```

- item 文案包含 device/session/workspace/pane breadcrumb、业务状态、相对时间和 freshness。
- stale 项显示“连接已过期，状态可能已变化”；done 显示“已结束/状态为 done”，不用“成功”。
- expired 点击后留在通知中心并给出可清除动作；不得偷偷选中同名 pane。
- live region 只播报新、高价值 transition，批量重连不连续朗读历史项。

## 命令与取消

- `OpenNotificationCenter`、`SelectNotification`、`ActivateTarget`、`MarkRead`、`MarkScopeRead`、`ClearExpired`、`SetMuteScope`。
- `ActivateTarget` 是可取消路由请求；新点击取消前一个待聚焦动作，不改变未读之外的业务状态。
- Windows sink 失败降级为 CenterOnly，记录 error category；不阻断 Store 更新。
- 清除通知只删除本地注意力记录，不删除 pane/agent 或服务器状态。

## 安全、持久化与恢复

- 通知缓存用版本化 JSON 原子写，只含稳定身份、状态类别、时间、read marker 和脱敏 display snapshot。
- 设备删除后相关通知标 expired/可清除；不保留远端地址、用户名或绝对路径。
- deep link 只接受 app 自己签发的 opaque route token，并在 host 校验当前 key；不能传命令或 takeover 参数。
- 系统通知不可用、权限关闭或应用未打包时显示明确状态；应用内中心保持工作。
