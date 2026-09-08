# herdr v0.9.0 规划基线（2026-09-08 复核）

规划上游钉 GitHub 稳定 tag **v0.9.0**（`prerelease=false`）。
不运行 `herdr channel set`，不停止用户 preview daemon。
IME / 真实 SSH L3 保持 UNVERIFIED。
G0、AC01–AC48 不因此标 passed。本文件是规划依据，不替代 `evidence/` 运行时记录。

Q1–Q3 = **A/A/A**（已授权 1.0 默认）：
1. 1.0 远端路径保持牧台 DeviceId + OpenSSH + API socket + `herdr terminal session`。`herdr machine` 与 endpoint generation 1 为并行合同，不是 Windows 1.0 默认。
2. 规划钉 GitHub v0.9.0；不切 channel；稳定 tag 运行时以后单独授权采集。当前 preview 不得写入 `compatible_by_default`。
3. 1.0 Agent TUI 验收仍为 Claude Code / Codex / OpenCode。Muse / Qwen 只做状态识别降级，不扩大 AC10。

## 来源

| 项 | 值 | 来源 |
|---|---|---|
| 稳定 tag | `v0.9.0`，发布 2026-09-07T19:21:31Z | GitHub Releases |
| 注解 tag 对象 | `cca4af8dfad160bc5fb5ae133b70882b5fe28f61` | `git ls-remote` / clone |
| 源码 commit | `b99002ac99b09e00b4ca692436cb15a6b0d676f1`（`release: v0.9.0`） | 浅克隆 scratch |
| 二进制 protocol | `PROTOCOL_VERSION = 22` | `src/protocol/wire.rs` |
| API schema | `"protocol": 22`, `"schema_version": 1` | `docs/next/api/herdr-api.schema.json` |
| endpoint 代 | `ENDPOINT_PROTOCOL_GENERATION = 1` | `src/protocol/endpoint.rs` |
| 许可 | Apache-2.0 | `Cargo.toml` |
| Rust toolchain | `1.96.1` + clippy/rustfmt | `rust-toolchain.toml` |
| 仓库内 `distribution/latest.json` | 仍为 `0.8.2` / protocol `20` | tag 内文件；网站通道滞后 |
| 本机 PATH | `0.9.0-preview.2026-09-08-62431dbd033b` | `herdr --version`；未执行 `channel set` |
| 历史对照 | tag `v0.8.2` commit `9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c`，`api_protocol=20` | `evidence/compatibility-baseline.json` |

Windows 稳定资源（规划记录，未下载、未安装）：

| 资源 | SHA-256 |
|---|---|
| `herdr-windows-x86_64.zip` | `b4508c445de1c1a68c760a01735da2aba2fa214b2aafd4b07f732e49b2a64b11` |
| `herdr-linux-x86_64` | `4fa1a01158dd8043da92d31b270780b0dcc10603038d9b61cac4d81ab63fb71f` |
| `herdr-linux-aarch64` | `9c8db20fb7e7427b138d5367113f1621ffd319f2f65d6f009e2594029115f0d2` |
| `herdr-macos-x86_64` | `d0c920b2a126a74809fa1491411c9a097a44786cac9c2ca51b818a995581cf16` |
| `herdr-macos-aarch64` | `32b53df09872628059c789a69f02a6b8e29e14ddf26711421f3463f70c1aef17` |

preview `2026-09-08-62431dbd033b` 比稳定 tag 新约一天。规划钉 **稳定 tag**。preview 只作漂移行，不得写入 `compatible_by_default`。

## 两套协议，不得混用

| 合同 | 常数 | 用途 | 牧台 1.0 现状 |
|---|---|---|---|
| 同安装二进制 wire | `PROTOCOL_VERSION=22`，长度前缀帧，`MAX_FRAME_SIZE=2MiB`，图形帧上限 `32MiB` | 同安装 CLI / direct-terminal / handoff | **禁止**把 JSON RPC 发到该 socket |
| JSON RPC schema | `protocol=22` / `schema_version=1` | API named pipe / Unix socket NDJSON | 1.0 默认 RPC 面 |
| endpoint generation 1 | `endpoint.hello.v1` / `endpoint.welcome.v1`；codec `shell.snapshot.v1`、`shell.surface.v1`、`shell.input.semantic.v1`、`shell.blob.v1` | Local/SSH/Cloud **client-owned shell**；新字段 optional/default；未知 enum 须 `Unknown` | 并行合同，是否作 1.0 默认见开放决策 |
| `herdr terminal session` stdio | `terminal.frame` NDJSON | observe/control 的 ANSI 重建帧 | 1.0 终端面（待决策确认） |

`PROTOCOL_VERSION` 与 `ENDPOINT_PROTOCOL_GENERATION` 独立。早于 generation 1 的 server 需要一次性升级（CHANGELOG #3509）。saved-machine 连接额外要求 server 的 `surface_interest` 与 `health_check`。

## `herdr terminal session`（v0.9.0 源码）

`src/client/terminal_sessions.rs` 在 observe 握手要求 `RenderEncoding::TerminalAnsi`。stdout 仍为：

```json
{"type":"terminal.frame","seq":…,"encoding":"ansi","width":…,"height":…,"full":…,"bytes":"<canonical Base64>"}
{"type":"terminal.closed","reason":"…"}
```

stdin 仍为 `terminal.input`（`text` 与 `bytes` 互斥）、`terminal.resize`、`terminal.scroll`、`terminal.release`。无逐命令 ACK。

`ServerMessage::Graphics { .. }` 在该 CLI 路径被 **空匹配丢弃**，不写入 stdout。因此 1.0 若继续走 `herdr terminal session`，Kitty 图形不会进入 HerdDesk renderer。HD-014 不宣称图像。不得把该丢弃写成“已实现图形”。

二进制 wire 的 `MAX_GRAPHICS_FRAME_SIZE` 只约束内部读；stdio 帧预算仍用牧台 16MiB 行 / 8MiB 解码上限。

Windows API listener 映射未变：`Path` → `to_string_lossy` → `to_ns_name::<GenericNamespaced>()`；Unix `to_fs_name::<GenericFilePath>()`；Windows 写 marker 文件，marker 不是可读 RPC 流（`src/ipc.rs`）。

## JSON RPC schema 22

`herdr api schema --json` 仍只证明 **该 CLI 内嵌** schema，不证明正在运行的 daemon。

启动必需仍为 `ping`（若 schema 含）、`session.snapshot`、`events.subscribe`。本轮从 schema 抽出的 RPC method 约 90+ 条业务方法；lifecycle 事件约 23 类。1.0 mutation allowlist 仍只开放 schema 已验证且 HD-017 选定的窄集。禁止虚构 `herdr api call`。

对牧台有合同影响的变更：

1. **先 subscribe 再 snapshot**（#1270）。`events.subscribe` 的 lifecycle 订阅从请求被接受时开始，**不回放**此前保留事件。文档：`docs/next/website/src/content/docs/socket-api.mdx`。HD-010 已按此顺序设计。
2. **`workspace.close` 与 worktree 组**（#2874）。主 workspace 在关联 worktree workspace 仍打开时，params 必须含 `"close_group": true`，否则错误码 `workspace_group_close_required`。显式组关闭为每个被关 workspace 发一条 `workspace.closed`。HD-017 必须把组意图做成显式确认，默认不得静默 `close_group: true`。
3. **图形 RPC**：`pane.graphics.set` / `clear` / `info` 进入 schema。1.0 不开放为 UI 能力，除非另有批准。
4. **多客户端**（#3526 / #3487）：不同 client 可看不同 workspace/tab；同 tab 最后交互者控制尺寸；终端 UI 在每个 client 上跑。HerdDesk 作为额外 client 时，resize 必须遵守 HD-016 控制权，不得因“最后交互”自动取得 stdin。
5. **去掉 `--no-session`**。所有终端 UI 附着后台 server。与牧台“herdr 拥有进程”一致。

`server.stop` 与 `server.live_handoff` 仍禁止由 GUI 关闭或牧台默认路径调用。

## `herdr machine` 与 Windows

官方文档 `connecting-machines.mdx`：

- `herdr machine add|list|rename|remove|enable|disable` 管理 **client 本地** catalog：opaque id、label、SSH target、显式 remote session、enabled。不存密码、私钥、agent ticket、control socket。
- 多机连接 **已支持 Linux/macOS 客户端 → Linux/macOS 服务器**（x86_64/aarch64）。
- **Windows 上多机连接尚未验证或支持**；Windows 仍用独立 `herdr --remote`。
- **Windows 不能作为 SSH 远端宿主**。
- 丢失一台不影响其他机；后台连接不回答 prompt、不安装/更新/重启/handoff。
- workspace/tab/pane id 与 agent 名按 **单 server** 作用域。两台机器可以都有 `w1:p1`。

catalog 是 herdr **客户端**状态，不是 JSON RPC method。牧台 `DeviceId` / `SessionKey` 不能用 pane id 或 herdr profile label 替代。是否在 Windows 上用自有 SSH + API + terminal session 填补官方空缺，见开放决策。

`herdr --remote`：本机 Linux/macOS/Windows 客户端可连 Linux/macOS 宿主；版本不必相同，但双方需支持 stable endpoint generation。非交互失败时不改远端。Windows OpenSSH 不用 per-attach control socket。

## 默认开启的 pane graphics

CHANGELOG：兼容终端上 pane 图像与 graphics API **默认开启**；`terminal.kitty_graphics = false` 可关。旧键 `experimental.kitty_graphics` 仍接受。

牧台不拥有用户 herdr 配置，默认不写该键。stdio 路径已丢弃 `Graphics`。endpoint generation 1 hello 含 `direct_graphics` 布尔。1.0 不实现 Kitty/Sixel。

## Agent 检测

0.9.0 新增 Muse（idle / working / approval / question，#2489）。0.8.2 已有 Qwen Code。原 1.0 TUI 验收仍是 Claude Code / Codex / OpenCode。其余 kind 按未知/降级显示，除非另有批准扩大 AC10。

## 对子任务的合同含义（不改 AC 状态）

| 任务 | v0.9.0 含义 |
|---|---|
| HD-001 | 源码矩阵增加 v0.9.0 / protocol 22 / commit `b99002a` 行。preview 已记录，不得与 v0.8.2/20 互证。稳定 zip hash 已记录；本机未安装稳定二进制。 |
| HD-003 | Windows pipe 映射与 marker 规则仍成立。ACL 仍缺 L2。 |
| HD-004 / HD-013 | stdio 帧 JSON 形状保持；Graphics 丢弃必须在 transport 设计中写明，不得当解析失败。 |
| HD-005 / HD-014 | 不承诺 Kitty；stdio 无图形字节。IME L3 仍 UNVERIFIED。 |
| HD-008 | listener 映射以 v0.9.0 `ipc.rs` 为准。Rust toolchain 候选 `1.96.1`，Cargo 依赖仍须实施时核验，不编造版本。订阅 ack 后才推事件。 |
| HD-009 | decoder 绑定 protocol 22 / schema_version 1 / schema SHA。未知 enum 保留 raw。 |
| HD-010 | 顺序强制 subscribe 成功 → 收 invalidation → snapshot → 安装。禁止依赖事件回放。 |
| HD-017 | `workspace.close` 实现 `close_group` 与 `workspace_group_close_required`；默认关单个 workspace 不得带组关闭。 |
| HD-020–026 | 官方 Windows 多机未支持。牧台 P3 仍按自有 Device/OpenSSH 规划，除非开放决策改为跟 `herdr machine` / endpoint gen 1。 |
| HD-007+ | 建仓不跨越 G0 证据门。不得把 preview 或 `distribution/latest.json` 的 0.8.2 当作当前 GitHub latest。 |

## 明确未做

- 未 `herdr channel set`
- 未停止用户 daemon/agent
- 未下载/安装 GitHub 稳定 zip
- 未把 `planning/acceptance.json` 或 `phase_gate` 标 passed
- 未把 preview 二进制当作稳定 tag 证明
- 未把 herdr.dev / 仓库 `distribution/latest.json` 的 0.8.2 当作当前 GitHub latest

## 已记录决策（Q1–Q3 = A/A/A）

1. Windows 1.0 远端路径：牧台 DeviceId + OpenSSH + API socket + `herdr terminal session`。不在 1.0 实现 endpoint generation 1，不读写 `herdr machine` catalog。
2. 不执行 `herdr channel set`。稳定 zip 仅记录 hash，本机安装与 runtime 采集另授权。
3. AC10 仍为 Claude Code / Codex / OpenCode。Muse / Qwen 未知降级。
