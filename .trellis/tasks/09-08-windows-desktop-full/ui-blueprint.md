# WinUI 产品 UI 实施蓝图

这是待实现界面的行为设计，不是现有截图，也不要求复刻herdrm素材。依据 `docs/plan/docs/02_产品定义与名称.md:42` 与 `docs/plan/docs/06_终端与输入法.md:1`。壳使用WinUI，terminal使用本地WebView2/xterm；不从Trellis的React模板生成产品。

## 窗口与路由

```text
┌ HerdDesk 牧台 ─ 设备 / 会话 / 连接状态 ─ 当前观察或控制状态 ┐
│ 设备/会话 │ 工作区 / pane树 │ 终端标签与操作栏 │ 详情/文件 │
│ 本机     │ 工作区A        │ 观察 控制 释放   │ 状态详情  │
│ Linux机  │   Agent / Shell│                 │ 本地⇄远端 │
│          │                │ 真正终端视图    │ 传输作业  │
├ 搜索 Ctrl+K / 未读 / 通知 ─── 状态或错误说明 ─────────────┤
│ 设置 / 诊断 / 关于                                         │
└────────────────────────────────────────────────────────────┘
```

四个区域可调整宽度，详情可收起；小窗口优先保持terminal与当前目标可见，侧区收起为导航入口，不让覆盖层遮挡IME候选窗口。精确尺寸和视觉tokens在HD-011实现时由可读性/DPI验证确定，不增加通用主题编辑器。

窗口退出释放本应用连接；关闭pane视图与真正关闭pane是不同命令，后者显示设备/会话/pane确认。重开程序只恢复导航偏好，连接后observe，不恢复控制授权/未发输入。

## 页面/组件/ViewModel责任

| 区域 | 拟建类型/状态 | 用户动作 | 拥有任务 |
|---|---|---|---|
| Shell | MainWindow / ShellViewModel、当前Device/Session/PaneKey | 页面切换、显示连接/控制状态、窗口关闭 | HD-011 |
| 设备与会话 | DeviceListView、SessionListView、DeviceEditorViewModel | 新建/编辑/移除客户端配置、选择显式endpoint/session、连接/断开 | 本地HD-011，SSH HD-020 |
| 工作区树 | WorkspaceTreeView、PaneItemViewModel | 展开、选择、创建、重命名、关闭 | HD-011/017 |
| 搜索 | SearchDialog / SearchViewModel、结果快照与取消 | Ctrl+K，设备/会话/标题/目录过滤，Enter定位 | HD-011/023 |
| Terminal host | TerminalPaneView / TerminalPaneViewModel | 观察、申请控制、确认接管、释放、复制粘贴、滚动、选择 | HD-014/015/016 |
| 资源操作 | CreateResourceDialog / OperationViewModel | 类型、已验证agent、工作目录/名称、提交、结果查询 | HD-017 |
| 通知中心 | NotificationList / NotificationService | 查看未读、清除本地未读、静默、点击定位 | HD-012 |
| SSH身份 | DeviceEditor / HostKeyReviewDialog / ConnectionDiagnostic | alias/port/key reference、指纹核验、失败诊断、重试 | HD-020/024 |
| 文件区 | FileBrowserView / TransferQueueViewModel / ConflictDialog | 本地/远端列举、上传下载、取消、Replace/KeepBoth/Cancel | HD-028/029 |
| 投入agent | DropTargetOverlay / AttachmentIntentDialog | 文字/路径/图像意图选择、目标确认、上传后插入预览 | HD-030/031 |
| 设置/诊断/关于 | SettingsPage / DiagnosticExportDialog / AboutPage | 主题/字体/通知偏好、版本/能力、导出预览、更新帮助 | HD-011/031/034 |
| 可访问性 | AutomationPeer、语义label、focus顺序 | 屏幕阅读器、键盘全流程、DPI/高对比 | 每UI任务自测，HD-033汇总 |

类型名是拟建责任建议；若实现合并类型仍须保留这些状态与可测试行为。ViewModel调用typed services/ports，不持有generic shell或原始SSH字符串。

## 共用状态呈现契约

| 场景 | 呈现 | 允许动作/下一步 |
|---|---|---|
| 第一次启动/无设备 | 简短连接引导，说明herdr前提 | 配置本地路径/endpoint；不后台安装或自动启动daemon |
| Connecting / Synchronizing | 显示当前阶段与取消；保留shell可操作 | 取消、诊断；写操作不可用 |
| Ready但无workspace/pane | 空列表与可用创建入口 | 仅真实schema确认的创建命令 |
| 目标尚未建立terminal基线 | 单独loading；不把空白区标可控制 | 取消/重试观察 |
| Offline / Stale | 文本和图标显示过期，保留上次快照并标时间 | 重新连接/诊断；禁止写 |
| Incompatible / Unknown capability | 版本/缺失能力说明 | 可验证的只读浏览；不默认尝试未知写方法 |
| Acquiring / Unknown control | 显示申请中或控制状态未知 | 取消/释放/只读；不接受输入 |
| Busy controller | 显示占用信息（能获得时） | 保持观察；用户另行明确确认接管 |
| 操作超时 | “结果未知”，保留目标上下文 | 查询权威状态；禁止自动重发创建/关闭 |
| 目标关闭/通知过期 | 明确过期目标 | 回到所属会话，不跳到另一设备同ID |
| renderer异常 | 保留目标和故障说明 | 重建renderer+observe基线，不重放输入 |
| 认证失败/key变化 | 可理解原因、指纹、已配置身份 | 用户处理后手动重连，后台停止反复提示 |
| 文件权限/冲突/断网 | 按job显示失败、等待冲突、取消/清理结果 | 操作精确job，不默认覆盖或“重试全部” |
| 更新或runtime不可用 | 显示当前可用版本和受控恢复入口 | 用户选择；不阻塞普通壳诊断 |

所有异步完成须比对当前请求/绑定，离开页面、切换设备、关闭窗口后晚到结果不重置当前UI。UI取消意图由服务负责资源退出，不能只隐藏spinner。

## 焦点、键盘与输入

- 初始focus在可理解的导航/连接入口；选择pane后聚焦terminal，但不因此获得控制。对话框关闭回到原触发控件；目标过期则回会话入口。
- composition开始到结束期间不处理会切换pane的全局快捷键；取消预编辑零发送，提交恰好一次。后台事件/通知不能抢输入焦点。
- Ctrl+K搜索；Ctrl+N/T/Shift+N创建入口按既有plan实现。Ctrl+Shift+C/V复制粘贴；Ctrl+C保留SIGINT语义。未在目标agent验证的Shift+Enter行为不统一强制。
- 观察态保留选择/复制和已验证的只读滚动；终端DA/DSR等自动应答不得伪装用户输入。resize/scroll由控制与上游能力决定是否可发送。
- 多行或高风险粘贴先显示目标和内容摘要，由用户确认；内容不进入默认日志。大payload在可信发送边界限额检查，不静默截断。
- 高频输出下focus、快捷键、取消仍可操作；状态更新合并，不能持续把键盘焦点重置到terminal。

## 文件与投入agent路径

拖入文件首先显示固定目标设备→会话→pane。普通文字粘贴、远端路径输入、图像附件是三种意图；已验证agent+platform能力决定入口。远端目标需要上传时显示目的目录、冲突和权限，上传完成后只进入“可插入”状态。

选择Replace时显示已存在文件与目标；KeepBoth返回实际保留名；Cancel不创建最终文件。job列表区分传输、校验、提交、取消清理、完成、失败；只有hash和原子rename完成可显示完成。路径已输入不等于agent已接受附件，不自动按Enter提交。切换pane后原拖放操作必须重新确认目标或取消，不将结果投入新目标。

## 设置、诊断与可访问性

设置至少含跟随系统/深浅/高对比主题、终端字体与缩放、通知静默/去重偏好、设备入口、版本和已验证能力、诊断预览/导出及更新信息。配置复用HD-007原子JSON；不保存控制授权、input队列或私钥。

诊断默认只有版本、脱敏ID、错误类别、时延、队列预算；用户看见将导出的字段并可取消。关于页展示独立客户端声明和许可证入口。

每个状态用文本/图标+语义名称，不只靠颜色。搜索→定位→控制→释放→关闭确认可以仅键盘和屏幕阅读器完成。100/150/200%DPI、多显示器切换、深浅/高对比需实际桌面验收。此要求贯穿任务，不留到最后仅做颜色美化。
