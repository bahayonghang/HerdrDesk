# HD-022 · 远端 RPC 与 terminal 通道

## 目标与用户价值

让同一 Windows 客户端通过已验证 SSH 身份连接远端 herdr，持续获得权威状态和可见 pane 的终端帧，同时保持各设备、各通道与 stderr 故障互相隔离。
远端连接必须沿用本地领域 port 与 epoch/控制策略，不能在 SSH 层创造第二套 RPC、终端或授权语义。

## 已确认事实

- 原任务处于 P3，依赖 HD-020/021，要求 `ssh -T` 透明 stdio、remote schema、长连接与退出分类：`tasks/HD-022.md:3-17`。
- 原 AC24 要求不用 `-tt`、stdout banner 明确失败、stderr 不作 NDJSON；原 AC26 要求单设备故障隔离、jitter backoff 和认证无无限重试：`tasks/HD-022.md:19-22`。
- 目标架构为同一设备的**每个 SessionKey** 一条 request RPC、一条 event subscription RPC，
  仅可见 pane 启动 terminal bridge：`docs/plan/docs/03_架构与数据流.md:54`、
  `docs/plan/docs/03_架构与数据流.md:78-82`。
- RPC、terminal 和 HerdDesk 自有消息是三类独立契约；RPC helper stdout 只能是 NDJSON，诊断只能走 stderr：`docs/plan/docs/05_协议与接口契约.md:3-11`、`docs/plan/docs/05_协议与接口契约.md:50-56`。
- 现有 parser 只负责一条完整 record 和一个连接 epoch，framing/lifecycle 仍属于未来 transport：`src/HerdDesk.Core/TerminalFrameParser.cs:12-29`。

## 需求

- **R1 通道拓扑**：每个完整 `DeviceId+SessionKey` 的 DeviceSession 独立拥有一个当前
  `RemoteSessionTransportSet`；该 set 持有 request RPC、event RPC 两个长期 `ssh -T` 子进程，
  每个可见 pane 另有 terminal child。通道组、epoch、pending map 与 child registry 均按
  SessionKey 隔离，不能让同设备后连接覆盖先连接。
- **R2 固定命令边界**：所有进程通过 HD-020 的固定 `ssh.exe`、私有 known-host 与类型化 ArgumentList/远端 POSIX 引用创建；只运行 HD-021 receipt 的绝对 helper 路径或配置中的绝对 herdr 路径，不经 shell 拼接任意 UI/terminal 文本。
- **R3 透明字节流**：request/event stdout 只送 RPC NDJSON framer，terminal stdout 只送 terminal framer；stderr 始终并行有界排空并单独分类，绝不能进入任一 parser。
- **R4 污染失败**：stdout 中 banner、提示、非 JSON 前缀、超长/截断 record 或 helper 自有握手必须停止当前通道并返回明确 `remote_stdout_protocol_pollution`/协议错误；不能跳过未知行后继续假正常。
- **R5 远端兼容性**：herdr CLI/schema/helper 的只读探针可按
  `DeviceId+profileRevision+hostKeyRevision+helperHash` 缓存；每个 SessionKey/endpoint 的 RPC
  ping 必须独立执行。客户端 binary schema 或另一 session 的 ping 不能替代该 daemon 证据。
- **R6 生命周期**：每个 stdout 只有一个 reader、stdin 串行写；取消/EOF/terminal.closed/remote exit/timeout/host-key/auth/daemon unavailable 产生不同 typed outcome，结束只清理本应用对应直接 ssh 子进程。
- **R7 epoch 与输入**：每个 SessionKey 的通道组重建产生新 `ConnectionEpoch`；旧响应、
  事件、terminal frame 与输入不得写回新状态；重连 terminal 默认 observe，旧输入队列
  立即丢弃且不重发。
- **R8 隔离与恢复接口**：一个 SessionKey 的 child、pending request、backpressure 或失败
  不能占用同设备其他 session 或其他设备的 actor/lock；本任务只报告 retry disposition，
  timer/attempt 仍由既有 DeviceSession actor 管理。
- **R9 隐私**：默认日志只含脱敏 device/session/pane、epoch、channel、duration、exit category、byte count；不含 terminal/RPC payload、输入、完整 argv、host/user/path 或原始 stderr。

## 验收标准

- [ ] **AC1（R1–R3）**：fake transport 并发运行 request、event、terminal；每类 record 只到对应 parser，慢/取消一个通道不会卡住另两个，stderr 高输出不会造成 stdout deadlock。
- [ ] **AC2（R2、贡献原 AC24）**：所有长期与短探针进程 argv 含 `-T` 且不含 `-t/-tt`、shell=true 或用户字符串命令；每条 SSH 路径都使用 HD-020 私有 trust 参数。
- [ ] **AC3（R3–R4、贡献原 AC24）**：banner-before-first-record、between-record banner、stderr JSON、超长行、truncated EOF 分别得到确定失败；stderr JSON 从未进入 RPC/terminal parser。
- [ ] **AC4（R5、贡献原 AC24）**：远端 version/schema/helper/ping 按设备记录；local/remote 不同 schema 会进入 incompatible/只读，不把 `api schema` 单独写成 daemon 已兼容。
- [ ] **AC5（R6–R8、贡献原 AC26）**：同设备 session A1 的断网/child/parser failure 不改变
  A2，设备 A 也不改变设备 B；失败 session 返回 disposition，transport 内无 retry timer。
- [ ] **AC6（R6–R7，贡献原 AC13/14/15）**：取消 3 秒内退出本应用该 session 的直接
  child；晚到旧 epoch 数据被丢弃；恢复先 observe/full baseline；断线前未确认输入不重发，
  远端 daemon/agent/pane 未被 stop/kill。最终真实生命周期验收归 HD-026。
- [ ] **AC7（R9）**：带秘密哨兵的 stdout/stderr/argv/profile 不出现在默认错误、日志或诊断摘要；显式诊断仍有大小限制与脱敏。
- [ ] **AC8（证据）**：L1 fake/contract 与 L2 Windows+隔离 Linux runtime 分开；无 L2 时为 `UNVERIFIED`，本任务不局部将原 AC24/26 改 passed。

## 原 AC 所有权

- HD-022 对 **AC24** 交付三通道透明流与污染处理的实现/L1/L2候选证据；**HD-026 拥有最终多设备汇总验收**。
- HD-022 对 **AC26** 仅交付通道级设备隔离和 retry disposition；HD-024 交付退避/认证阻断，**HD-026 拥有最终汇总验收**。
- HD-022 对 **AC13/14/15** 交付远端 session 的 stale→observe/full-baseline、输入丢弃和
  ssh child 所有权贡献；**HD-026 统一拥有三项产品最终验收**，不要求 HD-018/019 先关闭整项 AC。

## 范围外

- HD-020 负责 profile/host-key/argv；HD-021 负责 helper 部署；HD-024 负责 timer/backoff/revision；HD-025 负责连接/带宽预算；HD-026 负责最终支持矩阵。
- 本任务不实现 SSH multiplexing、remote Windows、密码/MFA provider、文件传输、daemon 自动启动/升级、输入重放或跨设备共享 parser/pending map。

## 阻塞与回退

若 remote helper、schema、stdout 纯净性或生命周期任一项未验证，则该 SessionKey 降级为
disconnected/incompatible；回退只断开该 session 的 app-owned child，不触碰 daemon、agent、
同设备其他 session 或其他设备。
