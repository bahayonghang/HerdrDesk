# GitHub 首次导入记录

日期：2026-09-07。目标：公开仓库 `bahayonghang/HerdrDesk`，默认分支 `main`。仓库由维护者创建，本次提交代码；没有创建另一个 `HerdDesk` 仓库，也没有改名。

## 已完成

导入 G0 C# Contracts/Core、Python 诊断与安全校验、测试、CI、活动任务及分章规划。源文件来自上轮交付的 `HerdDesk_G0_Source_2026-09-07.zip`；导入时修复 Windows UTF-8 读取和 fixture LF 换行，并新增 3 项回归。未加入用户凭据或真实终端输出。

仓库采用开发源码布局：完整保留 12 个规划专题及证据索引，保留设计契约与 Mermaid 源文件；不重复提交合并 Markdown/HTML、旧探针和原打包清单。完整原始交付 ZIP 不受本次整理影响。当前运行代码在根目录 `src/`、`scripts/` 和 `tests/`，不要运行规划档案中描述的历史入口。

## 可复查验证

[GitHub Actions run 34138627135](https://github.com/bahayonghang/HerdrDesk/actions/runs/34138627135) 对 commit `629bb01bd6bdb76e8576fd29668aa84a4894e59b` 运行的 Windows/Linux 两个 job 均成功；Python 回归、探针检查、结构检查、C# build 和 C# smoke 步骤均通过。Windows job ID 为 `101795314002`，Linux job ID 为 `101795314175`。

测试规模为 73 项 Python unittest、23 项 probe selftest、22 项 C# smoke；这是三套不同检查，不合并成覆盖率。后续提交必须读取自己的 CI 结果，不能把本记录当作所有未来版本的通过证明。

## 保留的边界

G0 门禁没有因此通过。CLI/daemon 版本一致性、真实 endpoint/ACL、observe/control 生命周期、SSH 身份、中文 IME、WinUI 产品和安装包仍待验证。当前没有 release；项目许可证仍待维护者决定。

`docs/implementation-g0.md` 是首次本地实施的历史记录；当前托管与自动检查状态记录在 `implementation/status.json`。安全回滚只影响本应用连接，不停止 herdr 或 agent。
