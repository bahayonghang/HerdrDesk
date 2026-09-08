# HD-013 · TerminalCliTransport 与生命周期

状态：`planning`。保持原依赖 HD-008、HD-010；本任务不决定谁可取得控制权，也不实现 renderer。

## Goal

实现进程级 `TerminalCliTransport`：只用固定的官方 `herdr terminal session` CLI 子进程承载一个 PaneKey/ConnectionEpoch 的终端连接，完整读取 stdout 帧、持续排空 stderr，并把 input/resize/scroll/release 按顺序写入唯一 stdin writer；断连后不重放，结束时只释放本应用拥有的直接 client 子进程。

## Confirmed facts

- 两条通信面必须分开，terminal frame 走 CLI stdio，不把 JSON RPC 发往 herdr binary client socket：`CLAUDE.md:28`。
- 已核验 argv 是 `herdr [--session S] terminal session {observe|control} TARGET --cols --rows`；target 与单次输入已有严格边界：`scripts/CLAUDE.md:46`。
- stdout 是逐行 NDJSON；`bytes` 是 Base64，stdin 支持 input/resize/scroll/release 且无逐命令 ACK：`docs/plan/docs/05_协议与接口契约.md:13-36`。v0.9.0 `terminal_sessions.rs` 对内部 `ServerMessage::Graphics` 空匹配丢弃，不写入 stdout；transport 不得把该静默当作 parse 失败或图像能力。
- 当前 `TerminalFrameParser` 只接收一条完整 JSON record，framing/lifecycle 明确归未来 transport：`src/HerdDesk.Core/TerminalFrameParser.cs:12-20`。
- 首帧、子进程存活、窗口焦点都不能令 `ControlVerified=true`：`src/HerdDesk.Contracts/TerminalModels.cs:20-23`。

## Requirements

- R1：每个实例固定一个完整 PaneKey、严格正值 ConnectionEpoch、mode、绝对 herdr executable path 与尺寸；使用 `ProcessStartInfo.ArgumentList`，`UseShellExecute=false`，不得拼 shell 命令或接受任意 argv。
- R2：observe/control mode 与 takeover 授权由 HD-016 在构造前选择。只有 HD-004 真实 help/capture 证实的 takeover 参数才可追加；默认构造永不 takeover。
- R3：stdout 由一个异步 reader 以固定缓冲逐字节寻找 LF；单行超过 16 MiB、EOF 前残缺记录、CR 规则不符或 parser 失败都 fail-closed，不截断、不跳帧。
- R4：每个连接新建一个 `TerminalFrameParser`；seq 只在当前 epoch 内比较。parser 输出被包装为带 PaneKey/epoch 的 owned frame，byte buffer 在 consumer 完成前不得归还池。
- R5：stdout 下游采用有界 item/byte budget。不能及时交付时以 `terminal_consumer_backpressure` 结束当前桥并报告，不静默丢 delta、不让内存无限增长。
- R6：stderr 从进程启动即由独立 drainer 持续读取，不能因 UI 未消费诊断而阻塞 client。只发布有界、脱敏的分类/计数；raw terminal text、input、路径凭据不进默认日志。
- R7：所有 stdin 命令先由 typed serializer 验证，再进入一个有界、byte-accounted FIFO；全实例只有一个 writer，可并发调用但实际 write+LF+flush 严格串行。
- R8：input 必须二选一编码 text 或 bytes，长度为 1..64 KiB；resize/scroll 的数值范围按已核验 wire contract 验证。拒绝空 payload、错误 pane/epoch 与未知 command。
- R9：每个 enqueue 生成本地 command id 和结果 `NotSent`、`WrittenUnacknowledged` 或 `UnknownAfterDisconnect`。该结果只描述本地写入边界，不宣称上游执行成功。
- R10：取消发生在 writer 开始前返回 NotSent；一旦开始写或 flush 与断连竞态，结果为 UnknownAfterDisconnect。任何结果都不得触发自动 retry；未开始队列在断开时清空。
- R11：`terminal.closed`、干净 stdout EOF、残缺 EOF、stderr classified rejection/busy、process exit、caller cancellation、protocol failure、backpressure 与 app shutdown 分开记录；exit code 0 不等于 pane 已退出。
- R12：`ReleaseAsync` 幂等且最多序列化一次 release；进入 closing 后先拒绝新 input。release write 无 ACK，只在后续桥结束证据中报告；dispose 不把 release 成功等同 pane/daemon 关闭。
- R13：transport 只拥有其直接 herdr CLI client。正常关闭等待有限时长后最多停止该 direct child，不调用 server stop，不 kill process tree，不把 daemon/agent 放进应用 job。
- R14：transport 可发布 HD-004 已验证 classifier 的 ownership observation；没有明确、与 pane/epoch/attempt 绑定的上游证据时必须是 Unknown，frame/process/focus 不能作为 grant。
- R15：公开 port 不暴露 `Process`、stdin writer、raw stderr 或可注入命令字符串；HD-014 只读 frame，HD-016 通过受限 send/resize/release port 调用。

## Acceptance criteria

- AC1（R3/R4/R11；原 AC05）：合成流按任意 byte chunk 切分仍得到一次完整基线与连续 delta；oversize、gap、malformed、残缺 EOF、`terminal.closed`+EOF 分别产生预期结果。
- AC2（R5/R6）：慢 consumer 与 stderr flood 下内存保持预算内；stderr 始终被排空，frame 不被静默丢弃，超预算只终止该 transport。
- AC3（R7-R10；原 AC06/AC14 贡献）：100 个并发 fake sends 的 stdin 是 100 条不交错 NDJSON；每个 command id 最多出现一次。断开竞态返回 NotSent 或 Unknown，不重排、不重放。
- AC4（R8/R12；原 AC06 贡献）：fake child 捕获 resize/release 的准确 wire shape；release 仅一次、release 后 input 被拒绝、transport 结束不调用 pane close/server stop。实际 resize 与 pane 存活仍需 L2。
- AC5（R11/R13；原 AC15 贡献）：正常、异常、取消和 app stop 测试都只 dispose/终止记录中的 direct child pid，fake daemon/agent pid 从未成为 kill target。
- AC6（R2/R14）：observe 永不产生 verified ownership；control 首帧和进程存活仍为 Unknown。仅载入 HD-004 认可 fixture 后 classifier 才能产生带 attempt/epoch 的 verified observation。
- AC7（最终证据边界）：L1 fake-process/fixture 证明 transport contract；本任务以获批 disposable-pane L2 汇总原 AC05 的真实 Windows observe baseline/seq/尺寸/Base64/EOF。resize/release交给HD-019汇总AC06，进程存活贡献交给HD-026汇总AC15；各自未运行时保持 `not_run`。

## Out of scope

- 不申请/抢占控制权，不决定 busy UI，不实现 renderer、IME、TUI、resource RPC、SSH 或 daemon lifecycle。
- 不运行 live herdr、不发送真实输入；不因 CLI 缺少逐条 ACK 而设计虚构 ACK、terminal.granted 或 delivery guarantee。

## Blocking gate

HD-004 未提供可解释的 control/busy/takeover/release signal matrix 时仍可完成 observe transport 和 Unknown 降级，但不得实现 verified control classifier。HD-010 未交付 current PaneKey/epoch 时不得接入产品 session。
