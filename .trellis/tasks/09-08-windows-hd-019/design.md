# HD-019 设计

## 验收矩阵

行：PowerShell、Claude Code、Codex、OpenCode 文本 TUI；列：observe、control、IME、
single delivery、resize、scroll、release、close GUI、renderer restart、EOF、daemon
exit、pane exit。每格有 version、环境、结果、证据路径、限制和复现步骤。

## 保护机制

只使用 disposable pane；每次 reconnect 新 epoch，旧 epoch 的输入由 `InputPolicy`
拒绝（`src/HerdDesk.Core/InputPolicy.cs:13-31`）。关闭测试先确认 bridge 是本应用
direct child，再断言 daemon/agent 继续。所有终端截图/日志脱敏并不提交正文。

## 退出

任何 agent/IME/ownership 未通过，能力矩阵即时降级；AC 记录 failed/not_run，而不是
以其他 agent 的成功替代。

## 逐场景执行卡

| 场景 | 步骤和预期 | 采集 | 验收 owner/测试路径 |
|---|---|---|---|
| observe baseline | 打开 pane；首帧 full、delta 有序；不允许写 | frame metadata、epoch、renderer log | HD-013/014，`tests/Integration.Windows/Terminal/` |
| acquire/control | 显式申请；busy 不抢占；确认后才可写 | lease transition、control signal | HD-016，`tests/Integration.Windows/Lease/` |
| IME | 预编辑不发送；提交一次；候选窗/快捷键正确 | composition/input ledger、录屏脱敏 | HD-015，`tests/Integration.Windows/Input/` |
| text TUI | 每个 agent 完成导航、确认、取消、输入、滚动 | agent/version、步骤、结果 | HD-019，`evidence/local-mvp/` |
| resize/release | writer resize 生效；release 只结束 bridge | dimensions、exit、pane liveness | HD-013/016，Terminal tests |
| renderer restart | 重建观察者，不重放旧输入 | epoch/state/queued-input ledger | HD-018，Recovery tests |
| bridge EOF | 明确连接结束，不冒充 pane exit | stdout EOF/stderr/exit classification | HD-013/018，Recovery tests |
| daemon/pane exit | 显示不同原因，其他 pane 不被结束 | ownership/process snapshot | HD-018/019，`evidence/local-mvp/` |
| GUI close | 100 次关闭只释放 direct child | bridge pid/handle baseline、daemon/agent liveness | HD-019，process evidence |

建议路径是将来实施输出；当前仓库没有这些 Integration 项目，不能把路径存在当测试
通过。每个场景条目要标 `planned/executed/passed/failed/not_run`，其中 `not_run` 只
说明尚未执行。

## 采集包

一个 run 的 manifest 至少记录 Windows build、CPU/GPU、DPI、WebView2、renderer、
herdr、bridge、agent、test pane、start/end UTC、输入授权、结果、失败注入、evidence
附件 hash 和残余限制。终端正文/credentials 只存受控本地附件或 hash。

## 跨阶段证据与测试入口

本矩阵仅使用本地 Windows 连接；AC13/14/15 的本地证据交 HD-026，真实 SSH 断网、
远端恢复与 SSH 子进程所有权在 P3 验证，不作为本任务完成前置。
以上 C# 场景统一进入 `tests/Integration.Windows/HerdDesk.Integration.Windows.csproj`
（HD-011 建立），脚本与人工执行卡放 `tests/E2E/`。新增场景必须在该工程发现列表和
执行输出出现；离线编译或孤立源文件均不算本地场景已执行。
