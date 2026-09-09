# HD-030 设计

## 拟建文件责任

- `src/HerdDesk.Contracts/AttachmentPorts.cs`：拟建 capability/profile、draft、target lease、delivery result 的 BCL 类型。
- `src/HerdDesk.Core/Attachments/AttachmentCoordinator.cs`：拟建能力选择、freshness、upload→input 状态机。
- `src/HerdDesk.Core/Attachments/AttachmentCapabilityCatalog.cs`：拟建证据化 agent/platform profile；unknown 保留。
- `src/HerdDesk.App/ViewModels/AttachToAgentViewModel.cs`：拟建意图、目标、预览、进度和命令。
- `src/HerdDesk.App/Controls/AttachToAgentFlyout.xaml`：拟建 UI。
- `src/HerdDesk.App/Controls/AttachmentCapabilityBadge.xaml`：拟建 Verified/Unsupported/Unknown 说明。
- `tests/Unit/HerdDesk.Core.Tests/Attachments/`、`tests/Integration.Windows/Components/Attachments/`：拟建 unit/Windows component 测试。

## 组件树

```text
AttachToAgentFlyout
├─ TargetHeader(Device > Session > Workspace > Pane, epoch/access)
├─ IntentPicker(Text / FilePath / ImageAttachment)
├─ CapabilityBadge(version, platform, evidence status)
├─ SourcePicker / DropZone / Preview
├─ DeliveryMethod(Path / VerifiedDirectClipboard / Manual)
├─ ProgressAndState
└─ Actions(Upload, InsertPath, CopyPath, Cancel, Close)
```

## 模型与状态机

- `AttachmentCapabilityKey`：agent kind + exact agent version + target OS + terminal/renderer version。
- `CapabilityEvidence`：status、verified operations、evidence ref、observedAt；布尔缺失不能当 false/true。
- `AttachmentDraft`：intent、source opaque reference、source metadata、initial target、generation。
- `AttachmentTargetLease`：完整 `PaneKey`、epoch、control lease id、capability key、expiry；不等于 terminal lease 实现。
- `DeliveryState`：`Editing | CapabilityLoading | Ready | Uploading | Uploaded | PathReady | Inserting | PathInsertedUnconfirmed | Cancelled | Failed | Stale`。

状态转换：Ready→Uploading 只调用文件 port；Uploaded→PathReady 记录 hash/最终路径；PathReady→Inserting 重新验证 target/epoch/control；成功只到 PathInsertedUnconfirmed，永不自动到 AgentAccepted。

## 命令与数据流

- `SetIntent`、`ChooseSource`、`AcceptDrop`、`ChooseDeliveryMethod`、`StartUpload`、`CancelUpload`、`InsertPath`、`CopyPath`、`DiscardDraft`。
- Source picker 返回用户明确选择的 opaque handle；Core 不扫描任意目录。
- upload 复用 HD-029 job/target lease，完成条件来自 HD-028 hash+rename。
- path 使用目标 OS 的结构化 path display/encoding；输入 bytes 由 HD-015 生成并经 HD-016 policy，不拼 Enter。
- target 变化只将 draft 标 Stale；用户须重新选择/确认，不能更新 lease 内 key。

## 能力与降级

- Verified path：允许相应按钮并显示证据版本；Unsupported：解释限制；Unknown：要求用户选择 path/manual。
- image preview 是本地视觉能力，不提升 agent attachment capability。
- direct clipboard 仅在真实 agent+platform 测试通过时出现；普通 Ctrl+V 成功不能外推到所有版本。
- capability catalog 是版本化数据/测试证据，不从 agent 标题或 terminal 文本动态猜测。

## 错误、取消与安全

- 错误分类：source unavailable、capability unknown、offline、permission、upload integrity、target stale、control revoked、input rejected、result unknown。
- upload cancel 等待后端 cleanup result；UI 保留 cleaning/unknown，不提前释放仍需清理的 lease。
- path insert 结果未知时不重放；用户可复制路径并手动确认。
- diagnostics 只记录 source kind/size、capability key、脱敏 target、job/input outcome；不记录内容或完整路径。
- 文件名、preview metadata 和 agent label 用 text binding；不能触发 URI/HTML/process。

## 焦点与可访问性

- 打开 flyout 保存原 terminal focus；关闭时仅在相同 target/epoch/renderer Ready 时恢复。
- drop zone 也提供键盘“选择文件”；意图和 capability 有可朗读文字。
- progress 宣告节流，PathInsertedUnconfirmed 由 live region 清晰读出“未自动提交”。
