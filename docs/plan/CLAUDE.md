# docs/plan

[根索引](../../CLAUDE.md) · [docs](../CLAUDE.md) · 规划档案

生成日期：2026-09-08。2026-09-07 规划基线。人日、指标、就绪度评分属于当时设计，不能当作产品已完成证明。

## 职责

保存十二个专题、拟建契约、架构图源文件、规划期任务/验收副本。实施进度以根目录 [planning](../../planning/CLAUDE.md) 与 [tasks](../../tasks/CLAUDE.md) 为准。本目录内 `planning/`、`tasks/` 的 `planned`/`not_run` 不能覆盖当前 CI。

可运行代码在仓库根 `src/`、`scripts/`、`tests/`。历史文字里的旧探针路径以 [../publication.md](../publication.md) 为准。专题 12 提到根目录 `VALIDATION.md`；当前仓库无此文件，本地校验快照在 `implementation/`。

## 专题摘要

| 文件 | 要点 |
|---|---|
| [docs/01_核验与可行性.md](docs/01_核验与可行性.md) | 对照 herdr v0.8.2 / herdrm v0.5.3。`protocol` 为 20，此前“约 21”不得写入兼容判断。Windows `direct attach` 返回 Unsupported；observe/control 走 terminal session 桥。`herdr api` CLI 只有 schema/snapshot，没有通用 call/订阅。herdrm 根目录未见 LICENSE。v0.8.2 桥代码在 `src/client/mod.rs`，不能套用默认分支 `terminal_sessions.rs` 行号。 |
| [docs/02_产品定义与名称.md](docs/02_产品定义与名称.md) | 应用名 HerdDesk / 牧台。v1 不含 Windows 被控宿主、插件市场、Kitty/Sixel 全保真。UI：设备区、workspace 树、中央 terminal、可收起详情。 |
| [docs/03_架构与数据流.md](docs/03_架构与数据流.md) | 模块边界、身份键、先订阅再 snapshot 的 dirty 收敛、控制权状态机。起步预算：最多 4 个可见 pane、最多 3 个远端设备。 |
| [docs/04_技术选型与ADR.md](docs/04_技术选型与ADR.md) | ADR-001 独立实现；ADR-002 WebView2/xterm 基线、native 有替换门禁；ADR-003 自有 RPC bridge + CLI 终端；ADR-004 `ssh -T`；ADR-005 不自动复活 daemon；ADR-006 默认观察；ADR-007 模块化单体。Windows App SDK 产品版 2.4.0 可见，NuGet 精确版本未锁定。 |
| [docs/05_协议与接口契约.md](docs/05_协议与接口契约.md) | 三种契约不得混用：上游 JSON RPC、上游 terminal stdio、HerdDesk 自有 host/renderer 与 file helper。禁止虚构 `herdr api call`。 |
| [docs/06_终端与输入法.md](docs/06_终端与输入法.md) | 帧是 ANSI 重建流，不是原始 PTY 日志。xterm local scrollback 初始 0。观察者不得因自动应答写回。OSC 52 读默认拒绝。IME 表：预编辑不发送、提交恰好一次、composition 期间 Ctrl+K 不切 pane。 |
| [docs/07_多设备SSH与文件.md](docs/07_多设备SSH与文件.md) | 见下方 SSH/文件。 |
| [docs/08_安全与威胁模型.md](docs/08_安全与威胁模型.md) | 终端输出、远端文件名、agent 标题均不可信。renderer 无 generic exec。标准用户运行。 |
| [docs/09_里程碑与执行Backlog.md](docs/09_里程碑与执行Backlog.md) | G0–P5；36 任务 57–99 人日；另加 25% 储备。不含 native renderer 强制替换（另 8–20 人日）。 |
| [docs/10_测试与验收.md](docs/10_测试与验收.md) | L0–L4。Hosted Windows CI ≠ 交互桌面。性能数字是规划目标，尚未实测。 |
| [docs/11_工程化发布运维.md](docs/11_工程化发布运维.md) | 签名 MSIX；应用更新与 herdr 解耦；配置版本化 JSON 原子写；诊断默认不含 ANSI/输入/文件内容。 |
| [docs/12_风险与待验证.md](docs/12_风险与待验证.md) | Windows 桥/IME/ACL/文件 helper/许可均未运行验证。issue #3701 属于 preview 路径报告，不能当作 stable 缺陷。 |

阅读入口：[README.md](README.md)。证据定位：[evidence/版本与证据索引.md](evidence/版本与证据索引.md)。

## SSH 与文件（专题 07）

本地 `ProcessStartInfo.ArgumentList`，不经 `cmd /c` 或 `shell=true`。远端 exec 仍需 POSIX 引用。主机目标不得以 `-` 开头。使用 `ssh -T`，不用 `-tt`。stdout banner 视为协议污染并失败。

认证基线：OpenSSH config + key + ssh-agent。密码/MFA/专门 Tailscale 交互单独验收。每设备指数退避 1–30s 加 jitter；host-key 变更不无限重试。重连先 RPC，terminal 默认 observe，旧输入队列丢弃。

文件主线：按调用启动的 `herddesk-filebridge`，与 RPC bridge 分离；P4 才锁协议。不解析 `ls`/`dir` 文本。上传：同目录随机临时名、独占创建、hash、原子 rename。`IRemoteFileService` 保留 SFTP 替换点。

投入 agent 分三种意图：粘贴文字、粘贴路径、图像附件。路径已输入不等于 agent 已接收图像。拖放目标必须显示 设备 → session → pane。

## 测试层级（专题 10）

| 层 | 环境 | 能证明 | 不能证明 |
|---|---|---|---|
| L0 | 任意 | JSON/任务依赖/合成协议/脚本自测 | 真实 herdr |
| L1 | Linux/Windows CI | Core、NDJSON、epoch | IME、真实 SSH |
| L2 | 真机 Windows 11 + herdr v0.8.2 | pipe、桥、生命周期 | 全部 agent TUI |
| L3 | 交互桌面 | IME、host key、文件、DPI | 长期不退化 |
| L4 | 受控 soak | 8h 负载、100 次重连 | 未纳入矩阵的 SKU |

规划性能预算（未测）：冷启动 p95 ≤2.5s；本地输入→呈现 p95 ≤100ms；1 可见 pane 工作集 ≤500MiB；取消后自有桥 ≤3s 退出。达不到时先保正确性，不丢帧、不吞输入。

## 拟建端口草案

`contracts/HerdDesk.Contracts.cs` 未编译。已实现子集见 [src/HerdDesk.Contracts](../../src/HerdDesk.Contracts/CLAUDE.md)。草案多出的类型：

| 符号 | 作用 |
|---|---|
| `ConnectionPhase` | Offline / Connecting / Synchronizing / Ready / Stale / Incompatible |
| `TerminalMode` | Observe / Control |
| `CapabilityProfile` | CLI/server 版本、schema hash、已验证操作集；缺能力视为 unknown |
| `TerminalEvent` | `FrameArrived` / `StreamClosed` / `TransportEnded` |
| `IRpcConnection` | `RequestAsync`；`SubscribeAsync` 的元素必须 clone，调用方释放 `JsonDocument`；禁止盲目重试 mutation |
| `ITerminalTransport` | 构造前由领域层选定 mode 并授权 takeover；`ReleaseAsync` 只放连接 |
| `ITerminalRenderer` | `ApplyAsync` 完成表示 parser 消费，不是 GPU 呈现或远端执行 |
| `IControlPolicy.CanSend` | 校验当前 pane+epoch+ownership；不信任 renderer 自报 ID |
| `IRemoteFileService` | 与终端同一身份/host-key 策略 |

`contracts/compatibility-profile.example.json` 是拟议配置示例：`default_terminal_mode=observe`，`replay_input_after_reconnect=false`，`max_inflight_bytes_per_pane=24MiB`。`runtime_validation` 全为 null。

## 架构图源文件

| 文件 | 对应正文 |
|---|---|
| `diagrams/01_architecture.mmd` | 上游 herdrm → HerdrKit → socket/SSH |
| `diagrams/02_architecture.mmd` | 目标 Windows 模块与两条通信面 |
| `diagrams/03_architecture.mmd` | 订阅确认后再 snapshot |
| `diagrams/04_architecture.mmd` | 控制权状态机 |
| `diagrams/05_architecture.mmd` | 终端背压管线 |

## 五条架构约束

1. herdr 是运行状态唯一权威；本地 Store 可丢弃。
2. App/renderer 不持有任意 shell 执行器。
3. RPC 与终端帧流分离。
4. 关闭 GUI 不 stop server、不杀 agent、不把 daemon 放入随 UI 退出的 Job Object。
5. 断线不重放输入；恢复先只读。

## 约束

- 禁止写出虚构 CLI。
- 禁止把 `api schema` 当作运行中 daemon 的证明。
- 禁止复制 herdrm 源码、图标、布局资源。
- 禁止把草案 `HerdDesk.Contracts.cs` 整文件覆盖进 `src/`，当作已交付 API。
