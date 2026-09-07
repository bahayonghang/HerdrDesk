# 测试与失败工作流审查证据

日期：2026-09-08。范围：只读运行现有离线测试、读取 GitHub Actions 历史、执行合成协议重现和默认发布清单核验。未运行 `just setup`、live herdr、SSH、GUI、IME、takeover 或发布；未修改产品源码。

## 1. 本机 `just ci`

环境：Windows `10.0.26200.0`，PowerShell `7.6.5`，Python `3.14.7`，.NET SDK `10.0.400`，just `1.58.0`。`global.json` 固定 SDK `10.0.400`。本机 Python 与 Actions 的 Python `3.12` 不同。

命令：

```powershell
rtk proxy just ci
```

退出码：`0`。信号原文（构建路径已替换为 `<REPO>`；73 个 unittest 和 22 个 smoke 的逐用例输出均为 `ok` / `PASS`，没有省略失败）：

```text
python -m unittest discover -s tests/python -v
----------------------------------------------------------------------
Ran 73 tests in 0.577s

OK
python scripts/probe_herdr.py selftest
{
  "selftest_passed": true,
  "checks": 23,
  "platform": "win32",
  "uses_synthetic_fixtures": true,
  "herdr_executed": false,
  "windows_gui_tested": false
}
python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson
{
  "kind": "offline_terminal_capture_validation",
  "validated": true,
  "frame_count": 3,
  "decoded_bytes": 41,
  "capture_bytes": 442,
  "capture_sha256": "d206d2ad30aac1814193b2f0423bbf30405d113e6d172adb2a9f5bed34f6f599",
  "last_sequence": 3,
  "saw_terminal_closed": true,
  "daemon_or_pane_exit_verified": false,
  "windows_verified": false,
  "ime_verified": false,
  "input_execution_verified": false
}
python scripts/validate_repository.py
{
  "structural_validation": "passed",
  "json_files": 30,
  "projects": 3,
  "tasks": 36,
  "csharp_compiled": false,
  "windows_verified": false
}
dotnet build HerdDesk.slnx --configuration Release
  HerdDesk.Contracts -> <REPO>\src\HerdDesk.Contracts\bin\Release\net10.0\HerdDesk.Contracts.dll
  HerdDesk.Core -> <REPO>\src\HerdDesk.Core\bin\Release\net10.0\HerdDesk.Core.dll
  HerdDesk.Core.SmokeTests -> <REPO>\tests\HerdDesk.Core.SmokeTests\bin\Release\net10.0\HerdDesk.Core.SmokeTests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build
PASS parse full frame
PASS ordered delta
PASS initial delta rejected
PASS gap rejected
PASS failure latched
PASS replay rejected
PASS duplicate key rejected
PASS invalid utf8 rejected
PASS utf8 frame split preserved
PASS closed envelope
PASS frame after close rejected
PASS max uint64 sequence
PASS verified exact input allowed
PASS unverified control denied
PASS observe denied
PASS old epoch denied
PASS other pane denied
PASS emulator reply denied
PASS empty input denied
PASS oversize input denied
PASS unknown origin denied
PASS default identity denied
22/22 smoke tests passed; no live Windows/daemon validation.
```

结论：现有 `just ci` 全绿，但它只证明本机 Windows 离线 G0。live herdr、daemon/pane 退出、SSH、GUI、IME、真实输入执行均为 `UNVERIFIED`。

## 2. GitHub Actions 历史失败及当前 SHA 状态

仓库仅有一个 workflow：`.github/workflows/ci.yml` 的 `G0 validation`。读取最近 20 次 run 实际只返回三次：

| Run | Commit | Windows | Ubuntu | 总结论 |
|---|---|---|---|---|
| `34138300061` | `e0670114b66c303ea10ece80f645df2105597a81` | FAIL | PASS | failure |
| `34138627135` | `629bb01bd6bdb76e8576fd29668aa84a4894e59b` | PASS | PASS | success |
| `34140811051` | `825186713ccfe6e2e00041f83d0b49975f4ea3c1` | PASS | PASS | success |

失败 run `34138300061` 的 Windows job 在 `Validate repository structure` 失败；此前 Python tests、probe selftest、capture 均通过，后续 setup-dotnet、build、smoke 被跳过。Ubuntu 同一 SHA 的所有步骤通过。失败原文：

```text
Run python scripts/validate_repository.py
Structure failed: 'charmap' codec can't decode byte 0x81 in position 127: character maps to <undefined>
Error: Process completed with exit code 1.
```

根因是 `e0670114` 的 `scripts/validate_repository.py` 对 `planning/backlog.json`、`planning/acceptance.json`、`evidence/compatibility-baseline.json` 使用无 `encoding` 的 `Path.read_text()`，从而依赖 Windows runner locale。修复 commit `629bb01` 为三处显式 `encoding='utf-8'`，并把 `.gitattributes` 默认文本固定为 LF；`tests/python/test_repository.py` 增加显式 UTF-8 和 fixture hash/LF 回归。该 commit 及后继 `8251867` 均取得 Windows/Linux 双平台 success。

当前本地 `HEAD=8ae4797d0aa10559648d1ade5b7e017ff46c7a53`，`origin/main=8251867`，本地前进 4 个 commit。命令：

```powershell
rtk gh run list --repo bahayonghang/HerdrDesk --commit 8ae4797d0aa10559648d1ade5b7e017ff46c7a53 --json databaseId,status,conclusion,headSha,url
```

输出为 `[]`。因此当前 SHA 的 hosted Windows/Linux 状态是 `UNVERIFIED`，不能继承 `8251867` 的通过结论。

## 3. C# isolated-surrogate 红回路

`TerminalFrameParser` 对不可信 JSON 的公开契约要求：协议失败转成稳定、脱敏的 `TerminalProtocolException`，并锁存 parser。现有 smoke 覆盖普通 JSON 错误、重复键和非法 UTF-8，但没有覆盖 JSON escape 解码后形成的孤立 UTF-16 surrogate。

在 `just ci` 已编译的 DLL 上使用下列只读 PowerShell 重现；每种输入使用新 parser，异常后立即向同一 parser 输入合法首帧，循环三次：

```powershell
$null = [System.Reflection.Assembly]::LoadFrom(
  (Resolve-Path 'src\HerdDesk.Contracts\bin\Release\net10.0\HerdDesk.Contracts.dll'))
$null = [System.Reflection.Assembly]::LoadFrom(
  (Resolve-Path 'src\HerdDesk.Core\bin\Release\net10.0\HerdDesk.Core.dll'))

$validBytes = [System.Text.Encoding]::UTF8.GetBytes(
  '{"type":"terminal.frame","seq":1,"encoding":"ansi","width":80,"height":24,"full":true,"bytes":"aGVsbG8="}')
$cases = @(
  @{ name = 'unpaired_surrogate_string_value'; json = '{"type":"\uD800"}' },
  @{ name = 'unpaired_surrogate_property_name'; json = '{"\uD800":1,"type":"terminal.frame","seq":1,"encoding":"ansi","width":80,"height":24,"full":true,"bytes":"aGVsbG8="}' }
)

foreach ($iteration in 1..3) {
  foreach ($case in $cases) {
    $parser = [HerdDesk.Core.TerminalFrameParser]::new()
    try {
      $null = $parser.Parse([System.ReadOnlyMemory[byte]]::new(
        [System.Text.Encoding]::UTF8.GetBytes($case.json)))
      $first = 'accepted'
    } catch {
      $first = $_.Exception.InnerException.GetType().FullName
    }
    try {
      $null = $parser.Parse([System.ReadOnlyMemory[byte]]::new($validBytes))
      $second = 'accepted'
    } catch {
      $second = $_.Exception.InnerException.GetType().FullName
    }
    [pscustomobject]@{ iteration=$iteration; case=$case.name; first=$first; then_valid=$second }
  }
}
```

结果六行一致：

```text
iteration case                               first                            then_valid
1         unpaired_surrogate_string_value   System.InvalidOperationException accepted
1         unpaired_surrogate_property_name  System.InvalidOperationException accepted
2         unpaired_surrogate_string_value   System.InvalidOperationException accepted
2         unpaired_surrogate_property_name  System.InvalidOperationException accepted
3         unpaired_surrogate_string_value   System.InvalidOperationException accepted
3         unpaired_surrogate_property_name  System.InvalidOperationException accepted
```

PowerShell 的外层反射调用包装为 `MethodInvocationException`；内层实际异常为 `System.InvalidOperationException`。string-value 的消息为：

```text
Cannot read incomplete UTF-16 JSON text as string with missing low surrogate.
```

根因链：

1. `TerminalFrameParser.cs:101` 的 `JsonElement.GetString()` 与 `:119` 的 `JsonProperty.Name` 会对孤立 surrogate 抛 `InvalidOperationException`。
2. `TerminalFrameParser.cs:89` 的 catch filter 仅接 `JsonException`、`DecoderFallbackException`、`FormatException`。
3. `failed=true` 只在 `TerminalProtocolException` 或上述 filter 的 catch 中执行（`:86`、`:91`）；逃逸异常不锁存，所以同一 parser 随后接受合法帧。
4. 这违反 `src/HerdDesk.Core/CLAUDE.md:11` 的失败锁存和 `:25` 的 malformed/脱敏统一契约。当前项目仍是 G0，无生产 transport；本证据只证明协议边界与 smoke 覆盖缺口，不声称已形成生产攻击。

批准后该项的最小验收信号应为：两种 surrogate 都只抛 `TerminalProtocolException("malformed_terminal_record")`，同一 parser 随后对合法帧抛 `TerminalProtocolException("terminal_stream_not_active")`，并由 C# smoke 回归覆盖；然后 `just ci` 全绿。

## 4. 默认发布清单核验漂移

直接运行（没有 `--publish`，不触网、不写 GitHub）：

```powershell
python scripts/publish_github.py
```

PowerShell 捕获的 Python 真实退出码为 `2`：

```text
Publish stopped: Publication file changed: .gitattributes; review it before publishing
PYTHON_SCRIPT_EXIT=2
```

全量只读 SHA-256 比对显示，manifest 所列文件无缺失，但有四项 hash 漂移：

| 路径 | manifest SHA-256 | 当前 SHA-256 |
|---|---|---|
| `.gitattributes` | `ec17ff5a...` | `97b01bda...` |
| `.gitignore` | `38d925c4...` | `f90caa30...` |
| `AGENTS.md` | `14a73c93...` | `6cacfe99...` |
| `README.md` | `6d418015...` | `761952a8...` |

manifest 有 154 个条目；当前 Git 跟踪文件（排除 manifest 自身）为 231。主线程使用 `rtk proxy git ls-files -z` 以 NUL 分隔并按 UTF-8 解码复核：77 个 tracked path 未列出，0 个 listed path 未被跟踪。其中包括后加的 Trellis 文件、`CLAUDE.md` 索引、`justfile` 和 setup 脚本。早期普通 `git ls-files` 的 Unicode quoted 路径比较得到90，已排除这一计数错误；77也不代表应追加数，集合应由历史清单用途决定。

根因不是 hash 函数：`PUBLICATION_MANIFEST.json:5` 把自己定义为 reviewed source import，`docs/publication.md:1` 也定义为首次导入记录；后续维护提交改变了树。与此同时，`README.md:56` 仍称默认脚本可用于离线核验。`just ci` 只运行 `tests/python/test_publish.py` 的临时目录 synthetic manifest 用例，不执行仓库根 manifest，所以主门禁全绿而文档推荐的默认核验失败。

审查代理提出的候选方向是先确定历史快照或当前发布集合语义。父计划现选择保留历史清单：不要求对已演进当前工作区的历史核验无条件返回成功；应核验预期退出码/诊断与文档一致，日常 `just ci` 继续通过。只刷新四个 hash 会让命令变绿，却仍忽略后加 tracked 文件，不能证明当前源树完整性。最终验收以父 design.md 和 evidence 子任务为准，不将两种互斥语义同时设为目标。

## 5. 汇总

- PASS：本机 Windows 离线 `just ci`；73 Python、23 probe、capture、structure、0-warning C# build、22 C# smoke。
- PASS：历史 commit `629bb01` 与 `8251867` 的 hosted Windows/Linux G0 workflow。
- FAIL：历史 `e0670114` Windows structure step；根因为 locale-dependent `read_text()`，已由 `629bb01` 修复并回归。
- FAIL：当前默认离线 publication manifest 核验；四项 listed hash 漂移，集合语义未对齐。
- FAIL：C# isolated-surrogate 合成红回路；异常类型、脱敏边界与 fail-latch 契约均未满足，现有 `just ci` 漏检。
- SKIPPED：`just setup`（会安装 SDK/写用户环境）、`--publish`、live herdr/SSH/GUI/IME/takeover。
- UNVERIFIED：当前本地 HEAD 的 hosted Windows/Linux CI；真实 daemon、pane exit、输入、桌面和 IME。
