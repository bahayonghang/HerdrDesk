# HD-014 可交付 WebView2 终端 adapter

## 目标

按 HD-005 决策交付本地 WebView2/xterm.js renderer adapter，把 HD-013 的终端 frame 安全、按序、有背压地呈现，并向可信 host 输出受限的输入、尺寸、焦点和链接请求。

## 当前事实与边界

- WebView2/xterm 是当前交付基线，精确依赖版本须由实际恢复验证锁定（`docs/plan/docs/04_技术选型与ADR.md` 第 5–19、25–27 行）。
- 本任务来源 `tasks/HD-014.md:5-13`，依赖 HD-005、HD-007、HD-013。
- terminal frame 是 ANSI 重建 delta，不是原始日志；本地 scrollback 初始为 0（`docs/plan/docs/06_终端与输入法.md:3-7,50-54`）。
- 当前 `ITerminalRenderer` 只存在于拟议草案，编译 Contracts 尚未实现（`src/HerdDesk.Contracts/CLAUDE.md:30-44`）。

## 需求

- R1：renderer 绑定完整 `PaneKey + ConnectionEpoch`；旧 epoch frame、input、resize、parsed ack 全部拒绝。
- R2：xterm、JS、CSS、字体资源随包且离线可用，固定本地 origin，不使用 CDN 或任意导航。
- R3：frame bytes 以 `Uint8Array` 进入 xterm，保持跨 frame UTF-8；不得将不可信 ANSI 拼入脚本或 HTML。
- R4：host→web 与 web→host 使用版本化 allowlist 消息；无 generic exec/file/RPC proxy。
- R5：端到端在途窗口同时覆盖 Core queue、WebView message 和 xterm 内部 write queue；xterm callback 只表示 parser consumed。
- R6：seq gap/回退、full 基线缺失、WebView process failure、消息损坏或慢消费进入 Faulted/Resetting，停止交互并重新 observe。
- R7：观察状态在 renderer 与可信 host 双层只读；xterm 自动回复标 `EmulatorReply` 并默认拒绝。
- R8：提供明确 loading、ready、observing、controlling、backpressured、resetting、offline/stale、error、disabled 和 focus 状态。
- R9：消费 HD-011 保存的主题/font family/font size/zoom，重算本地 cell pixels 和 IME anchor；观察态保持 server cols/rows 且不发 upstream resize，控制态 resize 仍交 HD-015/016 gate。
- R10：提供选择复制和受控 http/https 链接请求；不改写 ANSI 颜色语义。
- R11：HD-015 可在不绕开 host schema 的前提下扩展 IME、keyboard、selection；HD-016 可切换只读但 renderer 不自行判断控制权。

## 子任务验收

- [ ] AC1（R3）：cross-chunk 中文/emoji/组合字符无 U+FFFD、重复或顺序错误；控制键 bytes 不被文本层改写。
- [ ] AC2（R1, R4）：未知/伪造/超长 web message、错 pane/epoch、导航、新窗口、下载、permission request 均被拒绝和分类。
- [ ] AC3（R5）：slow xterm、高输出和超长行下内存有界，不通过丢 delta 维持假 Ready。
- [ ] AC4（R1, R6）：renderer crash/seq gap 后 UI 禁止输入，重建必须从同 epoch 的有效 full frame 或新 observe epoch 开始。
- [ ] AC5（R7, R11）：观察状态的 user input、paste、mouse、scroll 与 emulator reply 不到达 transport。
- [ ] AC6（R2, R4）：本地 bundle 在无网络环境加载；发布配置关闭 devtools、host objects 和任意 dialogs。
- [ ] AC7（R5）：隐藏 pane 不持续渲染；当前预算最多 4 个可见 pane，切换后资源可释放。
- [ ] AC8（R8, R10）：状态 overlay 不遮断屏幕阅读器/键盘恢复，实际 DPI/主题表现留 L3 证据。
- [ ] AC9（R9）：font/size/zoom 修改和恢复在 observe 下产生 0 个 upstream resize；本地 glyph/cell/IME anchor 更新且无持续 layout oscillation。

## 与产品 AC 的映射

- AC08（`planning/acceptance.json:61-66`）：本任务贡献交付 renderer 的字节正确性，HD-005 提供候选前置证据；最终汇总归 HD-015。
- AC27（`planning/acceptance.json:213-218`）：本任务贡献 renderer 背压与限额实现；统一负载最终验收归 HD-033。
- AC44：贡献 WebView2 特权边界；最终整体安全归 HD-006/HD-035。
- AC37/AC38：贡献 terminal surface 的可访问性、主题、DPI；最终归 HD-033。

## 非目标

- 不负责 terminal transport 生命周期、控制 lease、资源 CRUD、文件面或 agent 能力判断。
- 不承诺 Kitty/Sixel 图形、原始 PTY 日志、GPU 呈现 ack 或原生 renderer。v0.9.0 `herdr terminal session` 已丢弃内部 Graphics 帧；本任务不把该丢弃实现成图像通道，也不改用户 `terminal.kitty_graphics`。
- 不在 renderer 中信任/保存服务器身份，也不从 JS 直接调用 Core service。
