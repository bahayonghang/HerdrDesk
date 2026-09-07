# scripts

[根索引](../CLAUDE.md) · scripts

生成日期：2026-09-08。G0 诊断工具与 Python 协议库。生产 Windows 运行时不在本目录。

## 职责

- 有界 NDJSON / 终端帧 / 输入命令校验。
- 只读或显式授权的 herdr 探针。
- 仓库结构与发布清单核验。

Python 3.10+，仅标准库。将本目录加入 `sys.path` 后 `import herddesk_g0` 与 `import probe_herdr`。

## 包：`herddesk_g0`

| 符号 | 作用 |
|---|---|
| `Limits` | 行 16MiB、帧 8MiB、输入 64KiB |
| `ProtocolError` | 稳定错误类别；消息不含终端正文 |
| `strict_json_loads` | 拒重复键、非有限数字、非法 UTF-8、深度 >64 |
| `validate_frame` | `terminal.frame` / `terminal.closed`；返回 `(seq, summary)`，summary 含 `decoded_bytes` 与 payload `sha256` |
| `validate_input` | `terminal.input` / `resize` / `scroll` / `release` |
| `NdjsonDecoder` | 按 LF 切行；失败锁存；无 LF 的 EOF 失败，即使字节碰巧是合法 JSON |
| `TerminalCaptureValidator` | 单 epoch 有序帧；重连必须新实例 |
| `analyze_capture` | 总字节上限默认 256MiB；`daemon_or_pane_exit_verified` 等恒为 false |

`validate_input`：`text` 与 `bytes` 必须恰好一个；空载荷拒绝。Python `bool` 不得当作 JSON 整数。Base64 必须与 `b64encode` 回比一致。终端 payload 不做 UTF-8 解码。

`__init__.py` 仅模块说明，不导出符号。

## `probe_herdr.py`

子命令：`selftest`、`preflight`、`observe`、`control`。`shell=False`。`executable()` 经 `shutil.which` 后 `Path.resolve()`，不从 cwd 猜同名二进制。

### `capture`

启动本探针的直接子进程。stdout/stderr 分线程有界读取。超时或溢出时 `stop_owned`：`terminate`，1s 后 `kill` 同一 handle。返回码、是否超时、是否溢出、时长、原始字节。默认摘要只含长度，不含正文。`--include-diagnostics` 才附加 stderr 文本。

### `preflight`

只读命令：`--version`、`api schema --json`、`api snapshot`、`terminal session observe --help`、`control --help`。记录二进制 SHA-256。schema 校验 `protocol`/`schema_version` 为整数，并与参考头 `20`/`1` 比较。schema 导出到 `--output` 同名 `.schema.json`，默认不覆盖。snapshot 默认只记 `root_keys` 与 `has_error`；`--include-snapshot` 才会写入完整对象。范围声明：不测事件订阅、GUI、IME、control。

### `stream_probe`

argv：`herdr [--session S] terminal session {observe|control} TARGET --cols --rows`。target 拒绝空、前导 `-`、NUL、CR/LF。control 必须 `--disposable-target`。输入必须同时具备 control、`--allow-input`、`--disposable-target`，且文件 ≤64KiB UTF-8；发送 `terminal.input` 的 `text` 字段，不附加 Enter。stdout 按行、每行 `MAX_LINE+1` 读取。帧摘要最多保留 200 条。control 结束时尽力写 `terminal.release`，`release_acknowledged` 恒为 false。`takeover_used` 恒为 false。`wire_shape_checks_passed` 要求 `frame_count>0` 且无 errors。EOF 与 `terminal.closed` 分开记录。

### `selftest`

读取 `tests/fixtures` 的 valid/invalid 案例，再对本机 `sys.executable` 做正常/超时/溢出三次 `capture`。`herdr_executed=false`。

### 其他 CLI

`--output` 默认 `xb` 独占创建。`--session` 作为单一 argv。`--seconds`/`--timeout` 区间 `(0,120]`。`--cols`/`--rows` 为正 u16。

## 其余入口

| 文件 | 行为 |
|---|---|
| `check_capture.py` | `analyze_capture`；`--output` 独占创建 |
| `validate_repository.py` | UTF-8 JSON；无 `PackageReference`；36 任务无环；48 AC；baseline `api_protocol==20` 且 `default_write_capability is False` |
| `publish_github.py` | 核验清单 SHA-256、路径不越界、login=`bahayonghang`。默认 dry-run。`--publish` 仅历史空仓建仓 |
| `Invoke-HerdDeskPreflight.ps1` | pwsh 7 包装 `preflight`；仓库内无 Windows 执行证据 |

## 测试

[../tests/python](../tests/python/CLAUDE.md)。CI 另跑 `selftest` 与 `check_capture.py`。

## 约束

- 默认观察。不自动 takeover、不停止/升级 daemon、不重放输入、不绕过审批。
- 真实报告放 `probe-results/`（gitignore）。
- `safe_summary` 默认去掉 stdout/stderr 正文与 argv。
- 已有 GitHub 仓库禁止运行 `--publish`。
