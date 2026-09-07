# 04 · 技术路线与决策记录

## 选定主线

**WinUI 原生外壳 + .NET 10 LTS + 独立 C# Core + 本地 WebView2/xterm.js 终端 renderer + 小型 Rust RPC bridge + OpenSSH。** 这是一条从 MVP 延续到 1.0 的路线，不是先 Tauri 再整套重写的抛弃式原型。

.NET 10 的官方支持期明显长于已接近生命周期末期的 .NET 8/9，适合作为新项目基线。Windows App SDK 最新下载页在核验日列出 stable 产品版本 **2.4.0 / 2026-08-13**；精确 NuGet package ID/version 要在 G0 与实际模板/还原结果核实，不能把产品展示版本直接写成未经验证的 PackageReference。[E19](../evidence/版本与证据索引.md#e19) [E20](../evidence/版本与证据索引.md#e20) [E21](../evidence/版本与证据索引.md#e21)

| 层 | 默认选择 | 固定策略 | 验证要求 |
|---|---|---|---|
| GUI | WinUI / Windows App SDK stable | exact package version + lockfile | Windows 11 x64 打包、焦点、DPI、WebView2 |
| 应用逻辑 | C# / .NET 10 LTS | exact SDK feature band，补丁经 CI 更新 | nullable、analyzers、async cancellation |
| Renderer | xterm.js + WebView2 | 精确 npm/NuGet lock；JS 随包，不用 CDN | 中文/鼠标/回压/无任意执行通道 |
| RPC | 自有 Rust bridge + stdio | 先对齐 interprocess 2.4.2 的路径映射 | timeout/EOF/ACL/路径含 Unicode |
| 终端 | herdr terminal session observe/control | 固定 v0.8.2 契约；单独允许升级 | 帧、独占、resize、释放、异常 |
| SSH | 系统 OpenSSH | 探测 exe/version/config/known_hosts | key、agent、ProxyJump、host key 变更 |
| 文件 | SSH 文件 helper；SFTP port 作为替代 | helper 协议与产物在 P4 锁定 | 和 SSH 身份策略一致，不静默降级 |
| 配置/凭据 | JSON 原子写入；凭据系统保存 | schema_version + migration | 密钥不入 JSON/日志；卸载行为明确 |
| 发行 | 签名 MSIX + App Installer 或受控发行通道 | 单主更新机制 | 更新/回滚不停止 herdr runtime |

### ADR-001 · 独立实现而非直接 fork macOS App

**决定**：保留 herdr 协议与产品行为，重新实现 Windows 应用；不复制 herdrm 图标、布局资源、Swift 文件。**理由**：构建目标是 macOS，且本次未取得可复用主项目代码的明确许可。**反对意见**：fork 能加速业务模型开发；这只在授权和可复用边界明确后成立。**代价**：需要自行实现 domain 投影和测试。**撤销条件**：作者明确授权且代码评审证明实质节省维护成本。[E02](../evidence/版本与证据索引.md#e02) [E13](../evidence/版本与证据索引.md#e13) [E26](../evidence/版本与证据索引.md#e26)

### ADR-002 · 保留原生壳，终端使用可替换 renderer

**决定**：xterm/WebView2 作为交付基线，EWTC/native 作为独立 spike。**理由**：已读 EWTC README 明示 WinUI alpha/unofficial，默认 ConPTY 和 HWND 空域需要适配。**反对意见**：纯原生更贴合产品愿景；接受这一目标，但不以未经证实的内存/帧率优势替代验收。**代价**：混合应用增加 WebView2 runtime、安全边界与资源消耗。**撤销条件**：native adapter 满足同一接口、所有回归用例、内存与冷启动预算，并有维护负责人。[E10](../evidence/版本与证据索引.md#e10)

不把 Electron 判为技术错误；本项目因 Windows 原生整合目标不选 Electron。Tauri/Avalonia 在跨平台需求明确上升时再评估，不预先付出双壳维护成本。libghostty/ghostling/Wintty 可研究，但不是首发必需依赖。[E27](../evidence/版本与证据索引.md#e27) [E29](../evidence/版本与证据索引.md#e29)

### ADR-003 · 状态面用真实 RPC，终端面长期 CLI

**决定**：先实现小型 platform socket relay；C# 以 stdio 连接，终端继续运行官方 CLI bridge。**理由**：`api schema/snapshot` 不等于通用 RPC 代理；长期事件不能靠不断创建一次性 CLI 补足。**代价**：新增一个 Rust 构建产物及跨平台测试。**反对意见**：C# 直接 NamedPipeClientStream 可能更简单；允许后续作为同一 port 的替换，但必须实测 pipe 名映射和生命周期，不能在 UI 猜路径。**撤销条件**：上游发布稳定通用 stdio RPC bridge，且满足关闭/超时/事件契约。[E03](../evidence/版本与证据索引.md#e03) [E06](../evidence/版本与证据索引.md#e06)

### ADR-004 · SSH exec，不复制 macOS Unix-to-Unix 隧道实现

**决定**：远端 RPC 执行自有 bridge；终端执行远端 herdr CLI；均 `ssh -T`，不申请伪终端，不开本地无认证 TCP API 端口。**理由**：不同 Windows OpenSSH 版本的 Unix socket 转发能力与原 App 的本地 Unix socket 假设需验证；stdio route 更容易隔离输出和身份。**代价**：多连接开销，远端 helper 安装需要明确同意。**撤销条件**：经过安全验证的官方跨平台 transport 提供稳定等价能力。

### ADR-005 · 不拥有 agent，按用户意图管理 daemon

**决定**：v0.1 要求用户已启动 herdr；v1 可增加显式 opt-in 的本地首次启动。曾连接成功后 server 消失，不自动复活。**依据**：HerdrService 明确区分首次未运行与被用户停止/升级中的 server。**代价**：初次启动多一步诊断，但避免 daemon bind race 和破坏用户操作。**回滚**：禁用自动启动，回到手动模式。[E09](../evidence/版本与证据索引.md#e09)

### ADR-006 · 最小权限与可确认副作用

**决定**：默认观察，主动控制不带 takeover；自动重连回到观察；上传、关闭 pane、删除 workspace、跳过 agent 审批均由明确动作授权。**反对意见**：熟练用户希望少点击；可以提供 scoped preference，但不得用一项全局开关默默授予所有新设备。**撤销条件**：不能撤销安全原则，只能优化交互。

### ADR-007 · 模块化单体，不引入自建 agent 后端

**决定**：App Core + sidecar；不重写 agent detection，不保存完整终端历史用于云同步，不建自有调度服务。**理由**：最小化第二事实源和不可恢复状态。**演进条件**：跨进程性能/多用户访问有测量证据且明确单独产品安全边界。

## Native renderer 替换门禁

必须证明能在不启动第二个 ConPTY shell 的情况下喂入 ANSI，并把真实用户输入送给 controller；中文 IME 预编辑与提交、Ctrl/Caps/AltGr、paste、resize、鼠标、只读模式、弹层覆盖、可访问性均通过。尤其 EWTC 的 Win32InputMode 默认行为不应原样送入 generic terminal input。需要适配 UTF-8 跨帧边界，不用每帧独立 `GetString`。未通过门禁则继续 WebView2，并记录具体失败，不重做 UI 壳。[E10](../evidence/版本与证据索引.md#e10) [E25](../evidence/版本与证据索引.md#e25)
