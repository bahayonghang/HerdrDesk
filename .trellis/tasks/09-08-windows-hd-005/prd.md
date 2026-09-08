# HD-005 WinUI renderer 与中文输入 spike

## 目标

在不实现产品业务的最小 WinUI 宿主中，对 WebView2/xterm.js 交付基线与 native raw-stream 候选做同接口对照，形成可审计的 renderer 决策和真实 Windows/IME 证据采集方案。

## 当前事实与边界

- 当前仓库没有 `HerdDesk.App`、WebView2 renderer 或 native renderer；前端规范仍是 deferred 模板（`.trellis/spec/frontend/index.md:3-13`）。
- 技术主线已选择 WinUI/.NET 10，WebView2/xterm 为交付基线，native 仅为独立 spike（`docs/plan/docs/04_技术选型与ADR.md` 第 5–19、25–29 行）。
- 本任务的产品来源是 `tasks/HD-005.md:5-13`，依赖 HD-004；本轮只规划，不运行 WinUI、安装依赖或修改产品代码。
- 收到的是用于重建视图的 ANSI delta，不是原始 PTY 日志（`docs/plan/docs/06_终端与输入法.md:3-7`）。
- spike 不创建 workspace、agent、shell，不连接真实用户 pane，不授予 takeover，也不决定产品 UI 布局。

## 需求

- R1：两个候选必须消费同一组带 epoch/seq/full/尺寸的 frame fixture，并产生可比较的解析消费记录。
- R2：Web 候选使用随包本地资源、固定 origin、结构化 web message；native 候选不得启动第二个 ConPTY 或 shell。
- R3：字节路径必须保留 UTF-8 跨 frame 状态，覆盖中文、emoji、组合音标和控制键，不逐帧 `GetString`。
- R4：输入路径必须区分预编辑、已提交文本、用户按键、显式粘贴和 emulator reply；预编辑不得进入 transport。
- R5：覆盖 focus、候选窗定位、resize、alternate screen、鼠标、bracketed paste、选择复制和只读观察。
- R6：slow renderer 与高输出必须受有界在途窗口约束；消费回执不得被描述为 GPU 已呈现。
- R7：每个候选按正确性、IME、可访问性、安全、资源、维护性和可替换性记录 PASS/FAIL/UNVERIFIED，不用主观截图相似替代。
- R8：决策文档必须给出保留 WebView2、晋级 native 或继续阻塞的条件，失败时不要求重写 WinUI 外壳。

## 子任务验收

- [ ] AC1（R7, R8）：生成 renderer 决策矩阵，逐项标注证据层级、环境、版本和未测项；无证据项保持 UNVERIFIED。
- [ ] AC2（R1, R3）：合成分块 fixture 在两个候选中无替换字符、无重复和无 seq 跨 epoch 复用。
- [ ] AC3（R3, R4）：bytes 模式的 Ctrl+C、Tab、Esc、方向键与 resize 不经文本归一化。
- [ ] AC4（R4, R5, R7）：真实交互桌面记录微软拼音预编辑不发送、提交恰好一次、候选窗靠近光标；未运行则不得勾选。
- [ ] AC5（R2, R4）：观察模式不转发用户输入或 emulator 自动应答；renderer 消息不能触发任意进程、文件或目标切换。
- [ ] AC6（R6, R8）：slow renderer/高输出测试证明队列有界且恢复路径是销毁局部基线后重新 observe，而非丢 delta 假正常。
- [ ] AC7（R2, R5, R8）：native 只有在满足 `docs/plan/docs/04_技术选型与ADR.md` 第 51–53 行的完整门禁后才可替换默认 adapter。
- [ ] AC8（R7, R8）：`docs/spikes/renderer-decision.md` 记录选择、撤销条件、证据位置和后续 HD-014/HD-015 输入。

## 与产品 AC 的映射

- AC08（`planning/acceptance.json:61-66`）：本任务提供 renderer 可行性和字节正确性前置证据；HD-014 贡献交付 adapter，最终汇总归 HD-015。
- AC09（`planning/acceptance.json:69-74`）：本任务提供候选级真机 IME 证据；完整 renderer/IME 汇总归 HD-015。
- AC27（`planning/acceptance.json:213-218`）：只提供候选级背压数据，不拥有最终性能验收。
- AC37/AC38：只记录候选 renderer 的可访问性、DPI 与主题风险，最终归 HD-033。

## 非目标

- 不新增正式 `HerdDesk.App` 产品模块，不锁未经还原验证的 NuGet/npm 版本。
- 不宣称 native 性能更好，不复制 herdrm 布局、图标、字体或源码。
- 不以 synthetic fixture、WebView 单元测试或源码审查替代 L3 IME 证据。
