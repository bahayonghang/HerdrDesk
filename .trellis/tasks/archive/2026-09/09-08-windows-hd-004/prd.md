# HD-004 · 终端桥协议探针

## 目标与事实

在隔离 pane 观察 `observe/control/resize/release/EOF` 的真实输出、失败方式和
控制权信号，为 P2 transport/lease 定案。原任务 blocked，现有离线 probe/合成
capture 已完成但等待 runtime 与隔离 Windows pane：`tasks/HD-004.md:1-35`。
`TerminalFrameParser` 只解析完整记录，framing/lifecycle 仍属于未来 transport：
`src/HerdDesk.Core/TerminalFrameParser.cs:12-16`。

## 需求

- R1：分别采样 observe 首帧、seq/delta、width/height/Base64、EOF、stderr、进程
  退出和 pane 自然退出；不得把 EOF 冒充 pane exit。
- R2：验证 control、busy/rejection、takeover confirmation、resize 和 release 的
  实际可观察信号。上游没有清晰证据时状态必须是 Acquiring/Unknown，绝不伪造
  虚构的授权 wire event。
- R3：输入实验仅在可丢弃 pane、明确授权且可撤销时进行；观察模式不能发送输入。
- R4：probe 成功只说明 CLI/protocol，不说明 WebView/WinUI/IME 或产品 UI 成功。

## 原 AC 映射

- AC05-C1（G0 贡献）：observe 完整基线、序号/尺寸/Base64/EOF 行为有 runtime 记录。
- AC06/07/14/15/16-C1（G0 贡献）：control/release/rejection 信号和未知项真实记录。
- AC05（最终，HD-013）：生产 transport 再验证；AC06/07/14/15/16 由 HD-016/018/019
  的端到端测试最终关闭。

## 边界和撤销

不实现 GUI、长期 transport 或 takeover 自动化。任一写入/控制风险不清晰时停止
可写试验，保留 observe 证据和只读降级建议。
