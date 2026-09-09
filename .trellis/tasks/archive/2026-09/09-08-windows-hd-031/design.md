# HD-031 设计

## 拟建文件责任

- `src/HerdDesk.Infrastructure/Clipboard/IClipboardSnapshotReader.cs`：拟建显式快照端口。
- `src/HerdDesk.Infrastructure/Clipboard/WindowsClipboardSnapshotReader.cs`：拟建 Windows format 读取与线程/权限错误映射。
- `src/HerdDesk.Core/Clipboard/ClipboardIntentResolver.cs`：拟建 Text/FileList/Image/Mixed 判别。
- `src/HerdDesk.Core/Clipboard/PasteCoordinator.cs`：拟建 target lease、多行确认和输入交接。
- `src/HerdDesk.Infrastructure/Clipboard/AttachmentCache.cs`：拟建私有对象、TTL、容量和 lease。
- `src/HerdDesk.Terminal.Web/Input/OscClipboardPolicy.cs`：拟建 OSC52 deny policy；不读取 Windows clipboard。
- `src/HerdDesk.App/Dialogs/PastePreviewDialog.xaml`、`ViewModels/PastePreviewViewModel.cs`：拟建 UI。
- `tests/Unit/HerdDesk.Core.Tests/Clipboard/`、`tests/Unit/HerdDesk.Infrastructure.Tests/Clipboard/`：拟建 unit 测试；真实 Windows formats 放统一 integration project。

## Snapshot 与意图模型

- `ClipboardSnapshot` 保存读取时刻、可用 format 列表、opaque text/file/image handles 与总量估计；不默认持久化正文。
- `ClipboardIntent = Text | FileList | Image | Mixed(options) | Empty | Unsupported`。
- Mixed 必须由用户选择；resolver 只依据 Windows data formats，不读取内容来推断危险意图。
- `PasteTargetLease` 保存完整 `PaneKey`、epoch、control lease、snapshot id；与 HD-030 attachment lease 对齐。

## 文字粘贴流

1. 用户按 Ctrl+Shift+V/菜单 Paste；host 显式读取一次 snapshot。
2. resolver 若为 Text，计算 line count、UTF-8 bytes 和受限 preview。
3. 单行可直接进入确认/发送策略；任何多行打开 PastePreview，默认焦点 Cancel。
4. Confirm 时重新验证 pane/epoch/control，生成 `ExplicitPaste`，不附加 Enter。
5. 发送取消/结果未知均销毁 pending snapshot，不自动重试或跨 epoch 重放。

## 文件/图像与缓存流

- FileList/Image/Mixed 交给 HD-030 建立 AttachmentDraft；当前任务只提供 snapshot handle/cache object。
- `CacheObject` 含随机 object id、私有物理路径、size、content hash（若已算）、created/expires、lease count、state。
- cache write 使用独占临时文件、完成后原子 rename；cleanup 仅按 registry 中 exact object id。
- eviction 只选择 expired 且 lease=0 对象；容量不足且无候选时拒绝新对象。
- 应用退出取消未完成 write，保留需后续 exact-id 清理的 journal；不扫描用户目录。

## UI 与状态

```text
PastePreviewDialog
├─ TargetBreadcrumb + access/freshness
├─ IntentSummary(format, lines, bytes, files/image)
├─ BoundedPreview（文字或缩略图，不执行内容）
├─ Warning(Multiline/Unsupported/CacheFull/Stale)
└─ Cancel(default) / Send or ContinueToAttachment
```

- 状态：LoadingClipboard、Ready、NeedsIntentChoice、NeedsMultilineConfirm、Caching、CacheFull、AccessDenied、Stale、Sending、UnknownOutcome、Failed。
- focus trap、Esc cancel、AutomationName 包含目标/多行/大小；预览不自动获取 terminal 键盘焦点。
- Cache settings 页面显示当前用量、上限、最早到期和“清理过期”；不显示内容列表或敏感路径。

## OSC 与安全

- parser 识别 OSC52 request 后返回 `clipboard_read_denied`；不调用 snapshot reader。
- OSC52 write 默认同样拒绝；若后续启用，必须是 host 层明确用户策略，不允许 JS 直接访问系统 clipboard。
- clipboard API 异常分 `busy/access_denied/unsupported/oversize/unknown`；不无限 retry 或弹隐藏 prompt。
- 日志只记录 format kind/count/bytes、target 脱敏 key、decision/error、cache object id；不记录正文/hash 可反查内容的组合。

## 取消与并发

- 新 paste 请求取消旧未确认 snapshot；已进入 attachment job 则由 HD-030/029 独立管理。
- target/epoch 变化立刻令 lease stale；UI 可复制 preview 到本地但不能发送。
- cleanup 与 active lease 更新在单个 cache owner 中串行；崩溃恢复读取 registry，只清 exact owned temp。
