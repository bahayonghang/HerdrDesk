# Design

当前调用链为 justfile:24 → scripts/Invoke-HerdDeskDotnetSetup.ps1:44 自动安装，:52/:57 持久化环境，直到 :65 才读 global.json。README.md:26 仅描述核对及 DOTNET_ROOT，漏记自动安装和另一环境变量。

最小方案：保留现有脚本和 just 配方，默认只读；先读取仓库 SDK 版本，检测实际 PATH dotnet 与明确宿主，失败给出提示。安装和持久化分别用明确 opt-in 参数，不增加配置文件或自动回退。只有 opt-in 授权才走原有 winget / User 写入路径。脚本必须说明子 pwsh 的 process PATH 修改不会自动传回父 shell，最终检查依据后续 just build 实际使用的 host。

测试 seam：保留 Install-PinnedSdk 为安装边界，将两次 User setter 集中到单个 Set-UserDotnetEnvironment 函数；现有流程移入 Invoke-HerdDeskDotnetSetup，文件直接执行时调用它，dot-source 时仅定义函数。测试在独立 pwsh 子进程 dot-source 后重定义安装和 User 写入函数为记录调用的 fake sink，再调用真实主流程。正常、缺 SDK、global 不一致、显式 opt-in 四类用例断言调用次数/参数/顺序；默认和前置失败的两个 sink 均必须0次。显式分支只到 fake sink，绝不真实调用 winget 或静态 User setter。此为两处实际副作用的测试接缝，不增加 DI 框架或配置面。

文件：scripts/Invoke-HerdDeskDotnetSetup.ps1、justfile、README.md、tests/python/test_setup.py（新增，标准库 subprocess 调用 pwsh/stub；无 pwsh 环境明确 skip）。不改变 global.json 固定版本，不增加 Pester 或其他依赖。

回滚：按本任务改动恢复脚本/文档；验证不得触碰真实 User 环境，因此不需要用户环境回滚。

## Ownership and model

强模型负责契约、根因和最终审查；更便宜模型只执行明确文件与检查清单。适用 Claude Code / Codex / Grok Build / Kimi Code / OMP；具体边界见父 research/harness-matrix.md。
