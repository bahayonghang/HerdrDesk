# HerdDesk（牧台）

Independent Windows client for herdr. **G0 实施中；尚不是可运行的桌面产品。**

仓库：[bahayonghang/HerdrDesk](https://github.com/bahayonghang/HerdrDesk)。仓库名采用你创建的 `HerdrDesk`；应用名、solution 和 C# namespace 保留规划中的 `HerdDesk`，本次导入不做无关重命名。

目标路线：WinUI 原生外壳、.NET 10 LTS、独立 Core、可替换终端 renderer、RPC bridge 与 OpenSSH。herdr 拥有 agent/PTY，HerdDesk 只拥有连接。本项目独立于 herdr/herdrm，不复制未经授权的源码和素材。

## 已实现与已验证

| 范围 | 当前结果 |
|---|---|
| Python 协议与安全回归 | 73 项测试通过 |
| 只读探针 selftest | 23 项合成检查通过，不执行 herdr |
| C# Contracts/Core | GitHub Actions 的 Windows、Linux 构建通过 |
| C# smoke runner | 两个平台的 22 项检查通过 |
| 实施规划 | 12 个专题、36 项任务、48 项验收及架构图源文件 |
| 产品门禁 | G0 尚未通过；真实 herdr/SSH/GUI/中文 IME 未验收 |

可复查的首个双平台通过记录：[CI run 34138627135](https://github.com/bahayonghang/HerdrDesk/actions/runs/34138627135)，对应 commit `629bb01bd6bdb76e8576fd29668aa84a4894e59b`。更新提交的结果以 [Actions](https://github.com/bahayonghang/HerdrDesk/actions) 中对应 SHA 为准，不能沿用旧提交的通过状态。

本轮发现并修复 Windows 默认编码与 checkout 换行问题：元数据显式按 UTF-8 读取，fixture 固定 LF，并补充 3 项回归。Hosted Windows runner 的构建成功不等于交互桌面验收。

## 获取与检查

Windows 本机：`just setup` 核对 Python 3.10+ 与 `global.json` 固定的 SDK 10.0.400，并把用户级 `DOTNET_ROOT` 指到 `C:\Program Files\dotnet`。离线 G0 全套与 GitHub Actions 相同：

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

[分章实施规划](docs/plan/README.md) · [活动任务](planning/backlog.json) · [验收标准](planning/acceptance.json) · [本次发布记录](docs/publication.md) · [执行约束](AGENTS.md)

`src/` 为 C# Contracts/Core；`scripts/` 为诊断工具；`tests/` 为回归与合成 fixture；`planning/` 和 `tasks/` 是活动状态。`docs/plan/` 保存原规划的分章正文、证据和设计草案，不重复提交合并 Markdown、HTML 或旧探针等打包副本。它不是原 ZIP 的逐字节镜像。

[首次实施记录](docs/implementation-g0.md) 保留建仓之前的历史事实；其中“未推送”“C# 未编译”不代表当前状态。最新状态见 `implementation/status.json` 和对应 CI。

`PUBLICATION_MANIFEST.json` 是本次源文件的 SHA-256 清单，不是签名。`scripts/publish_github.py` 默认可用于离线核验，但 `--publish` 是历史新建仓库入口，**不要对已有仓库运行**。后续正常使用 branch/PR，不 force-push。

## 安全与范围

默认观察；可写探针必须明确指定 disposable target，输入另需 `--allow-input`。不自动 takeover、停止/升级 daemon、重放输入或绕过审批。真实终端内容、凭据和私人路径不得提交到公开仓库。

当前没有 WinUI 产品窗口、生产 RPC/SSH/file bridge 或安装包；48 项产品 AC 没有被提前标记为完成。详见 [安全说明](SECURITY.md) 和 [许可登记](docs/licensing-register.md)。项目许可证由维护者决定；公开可见不自动等于 MIT/Apache 授权。
