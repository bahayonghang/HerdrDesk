# HD-025 · 连接与带宽预算

## 目标与阶段门

在 P3 为多设备 RPC、terminal、未来文件任务和维护动作建立统一连接准入与有界队列，
使隐藏 pane 不保有 renderer/terminal 流，并在高 RTT、丢包和慢消费下保持真实状态。
本任务保持 `planning`；只有 `implementation/status.json` 权威记录 `phase_gate=passed`、
前置 HD-023/024 与待实施 HEAD 的 `just ci` 通过且用户批准后才能启动。

## 当前证据

- 当前仅有 Contracts/Core/parser/input policy；连接 actor、SSH transport、WinUI 和 renderer
  均未建仓（`.trellis/spec/backend/directory-structure.md:32`）。
- 旧 plan 的起步值是最多 3 个远端设备、全局最多 4 个可见 pane，且隐藏 pane
  不持续渲染（`docs/plan/docs/03_架构与数据流.md:54`）。
- 24 MiB 是单可见 pane 未消费 decoded terminal payload 的防御性预算；超限不能
  截断 JSON 或丢 delta（`docs/plan/docs/05_协议与接口契约.md:66`）。
- 进程树聚合工作集目标另为 1 pane 500 MiB、4 pane 900 MiB，二者不是同一口径
  （`docs/plan/docs/10_测试与验收.md:31`）。
- G0 仍 `not_passed`，Windows live 为 `not_run`（`implementation/status.json:3`、
  `implementation/status.json:31`）。

## 需求

- R1：一个准入策略按完整 `DeviceId+SessionKey+ConnectionEpoch` 统一计算 request RPC、
  event RPC、terminal、file job、maintenance 五类租约；同一 Device 的多个 SessionKey
  不得共用计数或被后到连接覆盖，调用层不得另设隐形连接池。
- R2：初始活跃远端设备上限 `D_remote=3`、全局 terminal 上限 `T_global=4`、每设备
  file 上限 1、全局 file 上限 2、全局 maintenance 上限 1；另有一个经 L2 实测后锁定的
  内部总 SSH 槽 `B_ssh`，不是用户配置。没有可审计实测值时 P3 不完成，不能猜上限。
- R3：每个 SessionKey 的 request+event 两个槽必须一次原子预留，任一 child 启动失败就
  teardown 并同时归还，不能留下半连接。容量不足时会话明确 WaitingForCapacity；调度器
  可先把无可见 pane、无 mutation/file 的会话标 `PausedForCapacity` 并释放其 pair，不能
  静默缩成“每设备一个 session”。P4/部署任务只能通过同一准入接口占槽。
- R4：`Q_p` 只统计 renderer 尚未确认消费的 decoded 原始帧字节，硬上限 24 MiB；
  不把 Base64、JSON、managed/native 对象、WebView、ssh 或 OS pipe 开销算进该值。
- R5：terminal 不因压力丢 delta 或维持绿色假正常；预算耗尽时停止当前 epoch、
  标记 stale/overloaded，并以新 observe/full baseline 恢复。
- R6：RPC 事件按实体合并为 dirty 标记；达到实体上限时退化为一个 full-resync
  标记，不建立无界原始事件队列，也不把此规则套到 terminal delta。
- R7：隐藏 pane 释放 renderer 与 terminal 槽；有容量时保留该 SessionKey 的 RPC pair，
  被容量调度暂停时则把投影明确标 stale/paused。再次激活先原子取得 pair、建新 epoch、
  snapshot/observe，禁止把缓存冒充实时状态或重放输入。
- R8：显式用户取消在 3 秒目标内终止本应用拥有的 bridge/ssh 子进程并归还槽；
  不 stop daemon、不杀 agent，也不枚举终止非本应用进程。
- R9：UI 显示“等待连接名额”“预览已暂停”“过载后重新观察”等真实状态；
  不把排队、旧缓存或进程存活显示成在线/控制成功。
- R10：性能证据同时记录队列字节、连接数、吞吐、RTT/丢包和完整进程树的
  working set/private bytes/handles；所有原始数据标环境、版本、时间和 evidence level。

## 来源 AC27 映射与本任务验收

- AC1（R4/R5，贡献 AC27“慢 renderer/高输出内存有界”）：逐 pane 24 MiB 原始
  payload 上限可被单测和真实慢消费触发，超限进入明确失败而非丢 delta。
- AC2（R1/R2/R3，贡献 AC27）：准入测试证明 session pair 原子性、每设备/全局约束和
  `B_ssh` 在并发重连下不超限；同一设备至少两个命名 session 可分别等待、暂停、恢复。
  `3×单 session+4 terminal=10` 只是一条基线场景，不是多 session 产品总上界。
- AC3（R6，贡献 AC27）：状态事件积压有界并最终触发校准；terminal 流不使用合并。
- AC4（R7/R9，贡献 AC27）：隐藏/显示 pane 反复 100 次不留 terminal 槽，恢复只读，
  UI 状态与实际 transport 一致。
- AC5（R8，贡献 AC27“取消可终止本进程”）：取消只终止本应用子进程并在目标时间
  归还准入槽；daemon 和 pane 继续存在。
- AC6（R10，贡献 AC27）：高 RTT/丢包/限带宽矩阵保存原始采样，且报告明确区分
  `Q_p` 与进程内存。
- AC27 最终 owner 是 HD-033；HD-025 只交付连接/队列政策和 P3 网络证据，不能将
  AC27 整体改为 passed。

## 不在范围与回滚

- 不实现 renderer 字节消费细节（HD-014）、最终 soak/全应用性能（HD-033）或
  filebridge 操作（HD-027/028）。
- 任一正确性门失败时回滚为更低可见 pane 上限和关闭后台预览；保留 RPC 状态面，
  断开本应用拥有的连接，不触碰用户 herdr daemon/session。
