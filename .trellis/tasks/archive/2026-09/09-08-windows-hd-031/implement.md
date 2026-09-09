# HD-031 实施计划

## 开始前

- [ ] 用户批准实施且任务已 start。
- [ ] HD-030（`.trellis/tasks/09-08-windows-hd-030`）定义 AttachmentDraft/cache lease 消费接口。
- [ ] HD-015/016 提供 ExplicitPaste 和当前 pane/epoch/control gate。
- [ ] HD-007 提供 app data/config/diagnostics 端口；不硬编码用户目录。

## 顺序清单

- [ ] 定义 clipboard snapshot/intent/paste target/cache object 类型和稳定错误类别。
- [ ] 实现显式 Windows snapshot reader，不注册后台 watcher。
- [ ] 实现 format-only resolver 与 Mixed 用户选择。
- [ ] 实现 PasteCoordinator、任何多行确认、0-extra-Enter 和 stale target gate。
- [ ] 实现 OSC52 read/write 默认 deny，验证不触发 Windows clipboard API。
- [ ] 实现私有 AttachmentCache 的原子写、exact-id registry、TTL/capacity/lease eviction。
- [ ] 实现 PastePreview/Cache settings 全状态、键盘、screen reader 和本地化。
- [ ] 接入 HD-030 file/image draft，不重复文件 upload logic。
- [ ] 跑 L1 intent/paste/cache property/fault tests。
- [ ] 跑 L2 Windows clipboard formats、busy/permission、crash cleanup；L3 验证真实快捷键和 dialog focus。
- [ ] 安全检查诊断/导出/缓存路径权限和内容泄漏。

## 拟建测试路径

- `tests/Unit/HerdDesk.Core.Tests/Clipboard/ClipboardIntentResolverTests.cs`：text/file/image/mixed/empty。
- `tests/Unit/HerdDesk.Core.Tests/Clipboard/PasteCoordinatorTests.cs`：multiline、cancel、no Enter、stale epoch。
- `tests/Unit/HerdDesk.Core.Tests/Clipboard/OscClipboardPolicyTests.cs`：read/write deny 且 reader 调用次数 0。
- `tests/Unit/HerdDesk.Infrastructure.Tests/Clipboard/AttachmentCacheTests.cs`：TTL/capacity/lease/exact cleanup。
- `tests/Integration.Windows/Clipboard/WindowsFormatsTests.cs`：真实 format 与 access error。
- `tests/Integration.Windows/Components/Clipboard/PastePreviewStatesTests.cs`：focus/accessibility/error/cache full。

## 命令和证据

- [ ] `[现有] rtk proxy just ci`：当前 Core gate；不证明 Windows clipboard。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Core.Tests/HerdDesk.Core.Tests.csproj -c Release --filter Clipboard`：L1 Core。
- [ ] `[拟建] dotnet test tests/Unit/HerdDesk.Infrastructure.Tests/HerdDesk.Infrastructure.Tests.csproj -c Release --filter Clipboard`：L1 cache/adapter logic。
- [ ] `[拟建] dotnet test tests/Integration.Windows/HerdDesk.Integration.Windows.csproj -c Release --filter Clipboard`：L2 Windows API + component state。
- [ ] L3 记录真实 text/file/image formats、快捷键、目标、确认、Narrator 和 bytes trace；内容证据使用 synthetic data。

## 回滚与结束门

- [ ] image/file cache 不可靠时禁用对应 intent，保留显式单行文本 paste。
- [ ] 多行/target/OSC gate 任一失败时禁用 paste，不回退为直接 WebView clipboard API。
- [ ] cache cleanup 只处理 exact owned objects；未知 ownership 保留并报告，不猜测删除。
- [ ] AC36 需 L1+L2+L3 与安全证据；HD-032 再验证 cache/file fault 组合。
