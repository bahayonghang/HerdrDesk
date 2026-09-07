# HerdDesk（牧台）

Independent Windows client for herdr. **G0 implementation in progress — not a runnable desktop application yet.**

代码托管仓库为 [bahayonghang/HerdrDesk](https://github.com/bahayonghang/HerdrDesk)。现有 solution、项目目录和 C# namespace 仍使用规划中的 **HerdDesk**；仓库名多出的 `r` 不影响构建，本次导入不做破坏性重命名。

目标技术路线：WinUI 原生外壳、.NET 10 LTS、独立 Core、可替换 WebView2/xterm 终端、Rust RPC bridge 与 OpenSSH。herdr 拥有 agent/PTY；HerdDesk 只拥有连接。本仓库不隶属于 herdr/herdrm，也不复制 herdrm 的源码、图标、字体或截图。

## 当前实现

G0 诊断工具已经实现：有界 NDJSON、严格 JSON/Base64、帧序列和输入参数校验、离线抓包检查，以及默认只读的兼容性探针。本次导入前在 Linux 环境重跑：**70 项 Python 回归测试通过，23 项 probe selftest 检查通过**。

`src/` 包含 BCL-only C# Contracts、帧解析器和输入权限策略；`tests/HerdDesk.Core.SmokeTests` 包含 **22 项 C# smoke 用例源码**。导入环境未安装 .NET SDK，因此不宣称这些测试已经运行。GitHub Actions 定义位于 `.github/workflows/ci.yml`，实际构建结果请以对应 commit 的 [Actions 记录](https://github.com/bahayonghang/HerdrDesk/actions) 为准。

没有 WinUI 产品窗口、生产 RPC bridge、SSH/文件后端或安装包。**G0 门禁尚未通过，48 项产品验收保留 `not_run`。** CI 的 Windows runner 也不等于真实 herdr、中文 IME 或交互桌面验收。

## 获取代码与验证

```powershell
git clone https://github.com/bahayonghang/HerdrDesk.git
cd HerdrDesk
python -m unittest discover -s tests/python -v
python scripts/probe_herdr.py selftest
python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson
python scripts/validate_repository.py
```

Python 3.10+，只使用标准库。精确版本 `.NET SDK 10.0.400` 对应的构建命令：

```powershell
dotnet build HerdDesk.slnx --configuration Release
dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build
```

C# smoke runner 是无第三方依赖的 console 测试程序，使用 `dotnet run`，不是已配置完成的 xUnit/`dotnet test` 项目。G0 暂不启用 NuGet 外部源；引入 WinUI/npm/Rust 时须重新验证并锁定依赖。

## 目录与实施状态

| 目录 | 内容 |
|---|---|
| `src/` | C# Contracts 与 Core |
| `scripts/` | Python 协议工具、只读探针与 PowerShell 入口 |
| `tests/` | Python 回归、C# smoke 源码、合成 fixture |
| `planning/`、`tasks/` | 当前 36 项任务、48 项验收及依赖状态 |
| `docs/plan/` | 原始规划档案；不与活动状态混用 |
| `evidence/`、`implementation/` | 版本基线、历史测试记录及未验证项 |

[首次 G0 实施记录](docs/implementation-g0.md) 描述的是建仓之前的历史交付；其中“未创建/未推送”的描述不代表本仓库的当前托管状态。原源码包中的 `scripts/publish_github.py` 是一次性建仓辅助工具，**不要在这个已有仓库执行 `--publish`**；后续使用正常 branch/PR 流程。

## 安全与许可

默认观察；可写探针要求明确标记 disposable target，输入另需 `--allow-input`。不自动 takeover、停止/升级 daemon、重放输入或绕过 agent 审批。不要把真实会话、密钥、私人路径和终端正文提交到公开仓库。

参考 [安全说明](SECURITY.md)、[许可登记](docs/licensing-register.md) 与 [执行约束](AGENTS.md)。项目许可证仍待维护者选定；公开可见不自动等于采用 MIT/Apache 等许可。
