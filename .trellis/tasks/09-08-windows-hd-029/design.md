# HD-029 设计

## 拟建文件责任

- `src/HerdDesk.App/Files/Views/FileWorkspacePage.xaml`：拟建双栏/堆叠工作区。
- `src/HerdDesk.App/Files/Controls/FilePane.xaml`：拟建单栏 breadcrumb、列表和状态。
- `src/HerdDesk.App/Files/Controls/TransferQueue.xaml`：拟建 job 队列与进度。
- `src/HerdDesk.App/Files/Dialogs/TransferConfirmDialog.xaml`、`ConflictDialog.xaml`：拟建目标与冲突确认。
- `src/HerdDesk.App/Files/ViewModels/FileWorkspaceViewModel.cs`：拟建两栏协调和 target lease。
- `src/HerdDesk.App/Files/ViewModels/FilePaneViewModel.cs`：拟建目录加载、选择和 stale 状态。
- `src/HerdDesk.App/Files/ViewModels/TransferQueueViewModel.cs`：拟建 job 投影和命令。
- `tests/Unit/HerdDesk.App.Tests/Files/`、`tests/Integration.Windows/Components/Files/`：拟建 unit/Windows component 测试。

## 组件树

```text
FileWorkspacePage
├─ WorkspaceHeader(target device/session, provider, online state)
├─ FilePane Left
├─ TransferActions(Copy → / ← Copy, Cancel, Refresh)
├─ FilePane Right
├─ TransferQueue(expand/collapse)
└─ DialogHost(TransferConfirm / Conflict / ExitWithJobs)
```

窄屏将 Left/Right 切为两个明确 tab，并在 action bar 持续显示 `源 → 目标`；不通过视觉左右位置推断传输方向。

## ViewModel 模型

- `FileLocation = Local(root,path) | Remote(deviceId, providerId, path)`；远端位置含 connection epoch/freshness。
- `FileEntryViewState` 保存 opaque entry key、原始 display name、type/size/mtime/symlink/permission、selection/focus。
- `DirectoryState = Initial | Loading | Ready | Empty | Refreshing | Stale | Offline | PermissionDenied | Failed | Incompatible`。
- `TransferDraft` 保存 immutable source entries、destination location、createdAt、generation 和 total known/unknown。
- `TransferJobViewState` 保存 jobId、immutable route、phase、bytes、speed sample、ETA、conflict、error、cleanup state。
- `ConflictPrompt` 保存 jobId、source/destination display、server conflict token；不得只按文件名提交。

## 目录加载与导航

1. 导航命令递增 pane generation，并取消旧 list request。
2. HD-028 provider 返回结构化 page/entries；ViewModel 校验 location/generation 后应用。
3. offline 时保留上次条目但标 stale、禁用 destructive/transfer 动作；refresh 显示明确失败。
4. symlink 以独立 icon/text 暴露，跟随与否由后端 policy/显式动作决定，UI 不偷偷当目录。
5. permission denied 显示路径上下文和诊断入口，不建议以管理员身份重试。

## Transfer 命令与状态

- `Navigate`、`Refresh`、`SelectEntries`、`CreateTransferDraft(direction)`、`ConfirmTransfer`、`ResolveConflict`、`CancelJob`、`OpenCompletedTarget`。
- Confirm 时冻结 source/destination + device/session/provider，生成 job；focus 变化不修改。
- progress 用节流 UI 更新但保留最后 bytes；速度/ETA 基于滑动样本且可 unknown，不参与完整性判定。
- completed 由后端明确 `hash_verified + rename_committed` 投影；网络 EOF 不能映射 completed。
- OpenCompletedTarget 只对原 target 有效，目标 offline/expired 时显示错误，不改投到当前 pane。

## 全状态行为

- Loading：列表 skeleton + cancel；Empty：明确“此目录为空”；Stale：灰态 banner + last updated。
- Permission/Incompatible：浏览信息与诊断可用，copy/upload disabled + reason。
- Conflict dialog：Replace 危险样式、KeepBoth 显示候选名、Cancel；关闭 dialog 等价 Cancel 当前 conflict。
- Failed job 保留可复制 error code 和 Retry 入口；Retry 重新创建 draft/确认，不复用未知后端 job。
- ExitWithJobs 提供等待/取消并退出/返回；取消后等待清理 bounded time，未知时说明。

## 安全与可访问性

- 文件名/路径用 text binding，不用 markup/URI；拖放 payload 先解析为受限 local item list。
- UI 永不构造远端命令，不把 remote path 放 argv；provider 使用结构化 port。
- 列表 AutomationName 含 name/type/size/状态；symlink、stale、selected 不只靠颜色/图标。
- 键盘可导航 breadcrumb/list/actions/queue/dialog，dialog focus trap 且关闭后返回发起按钮。
