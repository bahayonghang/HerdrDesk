# HD-022 技术设计

## 边界与前置契约

HD-008 的 `IRpcConnection`/stdio RPC、HD-013 的 `ITerminalTransport`/terminal process 与 HD-010 的 DeviceSession actor 是复用边界；本任务只把它们接到远端 SSH child。
规划 port 已明确 request/subscribe 和 terminal transport 的所有权/取消语义：`docs/plan/contracts/HerdDesk.Contracts.cs:47-66`。实施时使用前置任务已编译版本，不整文件复制草案。
SSH 命令、host-key 和认证来自 HD-020；helper 绝对路径/version/hash 来自 HD-021 current receipt。缺任一可信输入都不启动 child。

## 精确拟建/修改文件

- `src/HerdDesk.Infrastructure/SshTransports/SshProcessChannel.cs`：单 child 的 stdin/stdout/stderr、取消、bounded diagnostics 与 ownership。
- `src/HerdDesk.Infrastructure/SshTransports/RemoteRpcConnectionFactory.cs`：分别创建 request 与 event 的 bridge process，并适配 HD-008 RPC port。
- `src/HerdDesk.Infrastructure/SshTransports/RemoteTerminalTransportFactory.cs`：按可见 PaneKey/mode 创建远端 herdr process，并适配 HD-013 terminal port。
- `src/HerdDesk.Infrastructure/SshTransports/RemoteCompatibilityProbe.cs`：按 profile revision 缓存
  host binary/schema/helper 探针，并按 SessionKey 单独 RPC ping。
- `src/HerdDesk.Infrastructure/SshTransports/RemoteSessionTransportSet.cs`：一个完整 SessionKey 的
  request/event 生命周期组、epoch、Ready/teardown 顺序和 terminal child registry。
- `src/HerdDesk.Infrastructure/SshTransports/SshTransportOutcome.cs`：typed channel/stage/exit/outcome 与 HD-024 retry disposition；不含秘密文本。
- `tests/Unit/HerdDesk.Infrastructure.Tests/SshTransports/`、`tests/Contract/SshTransports/`、`tests/Integration.Ssh/Transports/`：下述通道、协议与 runtime 用例。

以上是未来路径，不附伪造行号；若 HD-008/013 已把 factory 放在同一项目，扩展其 port adapter，禁止另建第二套 parser/RPC pending map。

## 进程与命令拓扑

每个完整 SessionKey 的 `RemoteSessionTransportSet` 顺序为 host probe/cache lookup → 本
session receipt/endpoint 核对 → 原子预留 request+event 两槽 → 依次启动两个 child →
本 SessionKey RPC ping/subscribe/snapshot。request/event 都运行该 session receipt 指向的
`herddesk-bridge rpc --socket-path <endpoint>`，但有独立 process/streams；任一步失败 teardown pair。
每个可见 pane 按需运行远端绝对 `herdr terminal session` 命令；observe/control 与 takeover 已由领域层授权后作为类型化参数进入 factory。隐藏 pane 不在本任务预开 terminal。
本地进程始终 `UseShellExecute=false`、redirect 三流、`CreateNoWindow=true`，ArgumentList 由 HD-020 service 生成，包含 `-T`、BatchMode、StrictHostKeyChecking 和应用私有 UserKnownHostsFile；不存在 `-t/-tt`。
远端 exec 参数经唯一 POSIX quote 函数产生，参数集合来自受信 profile/contract；UI、terminal bytes、RPC payload 和 stderr 均不能成为命令片段。

## 字节流与并发

`SshProcessChannel` 启动后立即并发 drain stdout/stderr。stdout 暴露原始 byte stream；RPC 层按 16 MiB NDJSON 上限分帧，terminal 层沿用单 epoch parser 的 16 MiB line/8 MiB decoded frame 限额：`src/HerdDesk.Core/TerminalFrameParser.cs:17-35`。
stderr 使用独立有界 byte ring，只产生 length/truncated/category；永远不馈入 stdout parser。若 stderr 消费方慢，仍继续 drain 或安全丢弃超预算诊断，不能反压协议流。
request writer 用单队列串行；response 由唯一 reader 按 request id 完成 pending。event child 只订阅/读取。terminal writer 串行输入/resize/scroll/release；不同 child 不共享锁。
每个 DeviceSession actor 只接收带完整 SessionKey/epoch 的 typed event。网络 reader 不直接
改全局 Store；慢 session A1 的 await/queue 不能运行在 A2 或设备 B 的 actor 上。

## 兼容性与启动序列

短探针分别执行固定 `herdr --version`、`herdr api schema --json`、helper `--version`，记录
bounded output 与 SHA-256；缓存 key 含 DeviceId、profile/host-key revision、helper hash，任一
改变就失效。之后每个 SessionKey 的 request RPC `ping` 才提供该 endpoint daemon 运行证据。
`api schema` 只是远端 CLI binary 证据。CLI/helper/protocol/schema allowlist 不满足时状态为 Incompatible，request mutation、event Ready 和 terminal control 全部禁用；允许 UI 显示脱敏差异。
request 建立后启动 event 并等待 subscribe 确认，再交给 HD-010 的 snapshot+dirty 收敛。任一步失败都 teardown 当前 transport set，不把部分连接标 Ready。

## 生命周期、取消与 epoch

transport set 拥有 linked CancellationTokenSource 和 child registry。teardown 顺序：停止接受写入→取消 pending/reader→关闭 stdin→等待 child≤3s→只终止仍存活的直接 ssh child→dispose streams。
不调用远端 `server stop`，不 kill process tree。SSH 断开后远端 exec 是否立即退出属于 L2 观察项，但本地回收不能等待无限期。
每次重建由 DeviceSession 分配更大 epoch，创建新的 RPC framer/terminal parser/pending map。generation/epoch 不匹配的 late result 只计数丢弃，不应用 Store、不触发输入。
terminal.closed、clean EOF、unexpected EOF、非零 exit、用户取消和 parser failure 保持不同 outcome；exit 0 不能自行证明 pane closed。

## 污染、错误与重试边界

RPC/terminal stdout 的首条或中间非法 record 立即 latch 当前 framer/channel；不扫描到下一 `{`、不跳 banner、不把 stderr JSON 当恢复数据。
稳定 outcome 至少含 `remote_stdout_protocol_pollution`、`remote_record_too_large`、`remote_stream_truncated`、`remote_bridge_incompatible`、`remote_daemon_unavailable`、`remote_child_exited`、`remote_transport_cancelled`、`remote_terminal_closed`。
HD-020 已知 host/auth gate 可附带 `HostKeyBlocked`/`AuthenticationBlocked` disposition；无法
可靠分类的 exit 是 `UnknownBlocked`，不能自动当瞬态重试。既有每 SessionKey
DeviceSession actor 是 timer/attempt 唯一 owner；HD-024 只提供 classifier/policy 与共享
profile/host-key block revision，不另建 coordinator。
回退关闭该 RemoteSessionTransportSet 并清空该 session 未发送 input；同设备其他 session、
本地和其他设备继续，默认日志不包含原始 channel 数据。

## 验证设计

L1 fake child 可独立控制三流、分块、时序、exit 与 cancellation，验证无串流/死锁/旧 epoch；contract fixture 覆盖 banner、stderr JSON、truncated 和大 record。
L2 在 Windows 11 系统 OpenSSH + 隔离 Linux herdr/helper 上验证真实 `-T`、三 child、schema/ping、EOF、断网和直接 child 回收。L3 UI/多设备可观察结果仅形成候选，由 HD-026 汇总，不在本任务写 AC passed。
