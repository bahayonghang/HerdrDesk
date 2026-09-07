# GitHub 首次导入记录

日期：2026-09-07。目标：公开仓库 `bahayonghang/HerdrDesk`，默认分支 `main`。仓库由维护者创建，本次提交代码；没有创建另一个 `HerdDesk` 仓库，也没有改名。

`PUBLICATION_MANIFEST.json` 是这次导入的冻结 SHA-256 清单（reviewed source import），不是签名，也不是日常工作树完整性门禁。后续维护提交可以改变已列出文件的字节；默认 `python scripts/publish_github.py` 对该清单做离线历史审计。漂移时退出码 2。日常离线门禁是 `just ci`。不要刷新这些哈希来让审计变绿。`--publish` 是历史空仓建仓入口；已有仓库与已有 `.git` 会拒绝。不要对 `bahayonghang/HerdrDesk` 运行 `--publish`。

## 已完成

导入 G0 C# Contracts/Core、Python 诊断与安全校验、测试、CI、活动任务及分章规划。源文件来自上轮交付的 `HerdDesk_G0_Source_2026-09-07.zip`；导入时修复 Windows UTF-8 读取和 fixture LF 换行，并新增 3 项回归。未加入用户凭据或真实终端输出。

仓库采用开发源码布局：完整保留 12 个规划专题及证据索引，保留设计契约与 Mermaid 源文件；不重复提交合并 Markdown/HTML、旧探针和原打包清单。完整原始交付 ZIP 不受本次整理影响。当前运行代码在根目录 `src/`、`scripts/` 和 `tests/`，不要运行规划档案中描述的历史入口。

## 可复查验证（仅 SHA `629bb01`）

[GitHub Actions run 34138627135](https://github.com/bahayonghang/HerdrDesk/actions/runs/34138627135) 对 commit `629bb01bd6bdb76e8576fd29668aa84a4894e59b` 运行的 Windows/Linux 两个 job 均成功；Python 回归、探针检查、结构检查、C# build 和 C# smoke 步骤均通过。Windows job ID 为 `101795314002`，Linux job ID 为 `101795314175`。

该 SHA 的测试规模为 73 项 Python unittest、23 项 probe selftest、22 项 C# smoke；这是三套不同检查，不合并成覆盖率。run `34138627135` 只证明 `629bb01`。后续提交必须读取自己的 CI 结果。2026-09-08 closeout 探针：当时工作区 HEAD 无 hosted GitHub Actions run（`gh run list --commit` 返回 `[]`）。不要把 run `34138627135` 当作后续 HEAD 的通过证明。

后续工作树相对该清单的已知漂移（2026-09-08 只读比对，未刷新哈希）：`.gitattributes`、`.gitignore`、`AGENTS.md`、`README.md`。清单约 154 条；当前 Git 跟踪文件更多（含后加 Trellis 与索引）。默认脚本在第一处哈希不匹配处停止，因此常见诊断是 `Publication file changed: .gitattributes`。

## 2026-09-08 离线改造回写

适用工具：Claude Code、Codex、Grok Build、Kimi Code、OMP（can1357/oh-my-pi）。来源：`.trellis/tasks/09-08-evergreen-harness-audit` 及其子任务。五 CLI 新会话加载仍为 UNVERIFIED。

| 项 | 结果 | 边界 |
|---|---|---|
| 协议失败锁存 | 孤立 UTF-16 surrogate（type 值、属性名、`terminal.closed.reason`）对外为 `malformed_terminal_record`；同实例下一合法帧为 `terminal_stream_not_active`。本机 `just ci`：Python unittest 97、probe 23、C# smoke 26。 | 合成语料；无 live herdr。hosted Actions 未覆盖 closeout 工作区 |
| SDK setup | 默认 `just setup` / `Invoke-HerdDeskDotnetSetup.ps1` 只读。钉死 `global.json`。opt-in：`-InstallPinnedSdk`、`-PersistUserEnvironment`。 | 未在真实 User 环境执行安装或持久化 |
| 五工具入口 | tracked `AGENTS.md`、`docs/harness-workflows.md`、`.trellis/`。fresh clone 不依赖 gitignored 适配器目录。 | 五 CLI 实际 session-load：UNVERIFIED |
| 历史清单审计 | 默认 `publish_github.py` 输出 `kind=historical_bundle_audit`，`daily_gate=just ci`。漂移退出码 2。已有仓库拒绝。 | 未调用 `--publish`；未刷新 `PUBLICATION_MANIFEST.json` |
| 产品 AC | AC01–AC48 仍为 `not_run`。`phase_gate=not_passed`。 | HD backlog 仍按 `planning/` 与 `tasks/HD-*.md` 记录 |

## 保留的边界

G0 门禁没有因此通过。CLI/daemon 版本一致性、真实 endpoint/ACL、observe/control 生命周期、SSH 身份、中文 IME、WinUI 产品和安装包仍待验证。当前没有 release；项目许可证仍待维护者决定。

`docs/implementation-g0.md` 是首次本地实施的历史记录；当前托管与自动检查状态记录在 `implementation/status.json`。该 JSON 的计数可能滞后于后续提交。安全回滚只影响本应用连接，不停止 herdr 或 agent。
