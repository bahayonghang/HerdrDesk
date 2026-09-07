# HerdDesk（牧台）

Independent Windows client for herdr. **G0 实施中；尚不是可运行的桌面产品。**

仓库：[bahayonghang/HerdrDesk](https://github.com/bahayonghang/HerdrDesk)。仓库名采用你创建的 `HerdrDesk`；应用名、solution 和 C# namespace 保留规划中的 `HerdDesk`，本次导入不做无关重命名。

目标路线：WinUI 原生外壳、.NET 10 LTS、独立 Core、可替换终端 renderer、RPC bridge 与 OpenSSH。herdr 拥有 agent/PTY，HerdDesk 只拥有连接。本项目独立于 herdr/herdrm，不复制未经授权的源码和素材。

## 已实现与已验证

日常离线门禁是 `just ci`（与 `.github/workflows/ci.yml` 同步骤）。`PUBLICATION_MANIFEST.json` 与默认 `python scripts/publish_github.py` 核验的是 2026-09-07 首次导入清单。`implementation/status.json` 的计数可能滞后；以本次命令输出为准。

| 范围 | 当前结果 |
|---|---|
| Python 协议、探针门、setup stub、仓库与发布诊断 | 本机 97 项通过（2026-09-08，`python -m unittest discover -s tests/python -v`）。含协议 surrogate 边界、setup stub、发布诊断。 |
| 只读探针 selftest | 23 项合成检查，不执行 herdr |
| C# Contracts/Core | 本机 `dotnet build` 可复现；hosted Actions 只证明对应 SHA |
| C# smoke runner | 26 项（含孤立 surrogate 失败锁存与合法 surrogate pair）；`dotnet run`，不是 `dotnet test` |
| SDK setup | 默认 `just setup` 只读；版本以 `global.json` 为准；不调用 winget、不写 User 环境 |
| 实施规划 | 12 个专题、36 项 HD 任务、48 项产品验收；AC01–AC48 仍为 `not_run` |
| 产品门禁 | G0 尚未通过；真实 herdr/SSH/GUI/中文 IME 未验收 |
| 五 CLI 新会话加载 | UNVERIFIED |

可复查的历史双平台记录：[CI run 34138627135](https://github.com/bahayonghang/HerdrDesk/actions/runs/34138627135)，仅覆盖 commit `629bb01bd6bdb76e8576fd29668aa84a4894e59b`。该次规模为 73 项 Python unittest、23 项 probe、22 项 C# smoke。当前 HEAD 的 hosted GitHub Actions 为 UNVERIFIED，不得沿用 run `34138627135`。

2026-09-08 离线改造（适用 Claude Code / Codex / Grok Build / Kimi Code / OMP）：JSON 字符串/属性名上的孤立 UTF-16 surrogate 收口为 `malformed_terminal_record` 并锁存解析器；默认 setup 改为只读。这些改动只有本机离线证据；hosted Windows runner 未覆盖 closeout 时的工作区 HEAD。交互桌面、IME 与真实 herdr 仍未验收。

## 获取与检查

Windows 本机：`just setup` 核对 Python 3.10+ 与 `global.json` 固定的 SDK 10.0.400。默认只读：不调用 winget，不写 User `DOTNET_ROOT` / `DOTNET_MULTILEVEL_LOOKUP`。子进程 PATH 变更不会回到父 shell。显式 opt-in：

```powershell
pwsh -NoLogo -File scripts/Invoke-HerdDeskDotnetSetup.ps1 -InstallPinnedSdk
pwsh -NoLogo -File scripts/Invoke-HerdDeskDotnetSetup.ps1 -PersistUserEnvironment
```

离线 G0 全套与 GitHub Actions 相同：

```powershell
just ci
```

分步：`just build`、`just smoke`、`just test-python`。`just` 列出全部配方。没有 `just` 时：

```powershell
git clone https://github.com/bahayonghang/HerdrDesk.git
cd HerdrDesk
python -m unittest discover -s tests/python -v
python scripts/probe_herdr.py selftest
python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson
python scripts/validate_repository.py

dotnet build HerdDesk.slnx --configuration Release
dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build
```

Python 3.10+，仅标准库；C# 使用 SDK 10.0.400。smoke runner 是 console 测试程序，使用 `dotnet run`，不是 xUnit/`dotnet test` 项目。

## 开发入口

[执行约束](AGENTS.md) · [五工具派发](docs/harness-workflows.md) · [Trellis](.trellis/workflow.md) · [活动任务](planning/backlog.json) · [验收标准](planning/acceptance.json) · [HD 任务正文](tasks/CLAUDE.md) · [分章实施规划](docs/plan/README.md) · [首次导入记录](docs/publication.md)

产品 backlog 与本轮工程改造分开记录：

| 权威 | 路径 | 说明 |
|---|---|---|
| 产品 backlog / AC | `planning/`、`tasks/HD-*.md`、`planning/acceptance.json` | HD-001–036；AC01–AC48 仍为 `not_run`。离线 `just ci` 不把产品 AC 标为 `passed`。 |
| 本轮工程改造 | `.trellis/`、`AGENTS.md`、`docs/harness-workflows.md` | 协议失败锁存、SDK 默认只读、五工具入口。HD-007 与 G0 仍按产品 backlog 记录。 |

`src/` 为 C# Contracts/Core；`scripts/` 为诊断工具；`tests/` 为回归与合成 fixture；`planning/` 和 `tasks/` 是活动产品状态。`docs/plan/` 保存原规划的分章正文、证据和设计草案，不重复提交合并 Markdown、HTML 或旧探针等打包副本。它不是原 ZIP 的逐字节镜像。

[首次实施记录](docs/implementation-g0.md) 保留建仓之前的历史事实；其中“未推送”“C# 未编译”不代表当前状态。最新状态见 `implementation/status.json` 和对应 SHA 的 CI。

`PUBLICATION_MANIFEST.json` 是 2026-09-07 首次导入的历史 SHA-256 清单，不是签名。默认 `python scripts/publish_github.py` 只审计该历史 bundle；工作树漂移时退出码 2（例如 `Publication file changed: .gitattributes`）是该审计的预期结果。不要刷新哈希。`--publish` 是历史空仓建仓入口，已有仓库会拒绝；不要对 `bahayonghang/HerdrDesk` 运行。后续使用 branch/PR，不 force-push。

## 安全与范围

默认观察；可写探针必须明确指定 disposable target，输入另需 `--allow-input`。不自动 takeover、停止/升级 daemon、重放输入或绕过审批。真实终端内容、凭据和私人路径不得提交到公开仓库。

当前没有 WinUI 产品窗口、生产 RPC/SSH/file bridge 或安装包；48 项产品 AC 没有被提前标记为完成。详见 [安全说明](SECURITY.md) 和 [许可登记](docs/licensing-register.md)。项目许可证由维护者决定；公开可见不自动等于 MIT/Apache 授权。
