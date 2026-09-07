# tests/python

[根索引](../../CLAUDE.md) · [tests](../CLAUDE.md) · python tests

生成日期：2026-09-08。标准库 `unittest`。规模：73 项（以 `implementation/status.json` 与 README 为准）。

## 职责

回归 Python 协议、探针安全门、发布清单、仓库可移植性。不执行 herdr，不启动 GUI。

## 入口

```powershell
python -m unittest discover -s tests/python -v
```

每个文件将 `ROOT/scripts` 插入 `sys.path`。无 `pyproject.toml`、无 pytest。

## 测试文件

`def test_` 共 73 个，与 README / `status.json` 一致。

| 文件 | 类 | 覆盖 |
|---|---|---|
| `test_protocol.py` | `StrictJsonTests`, `FramerTests`, `FrameTests`, `InputTests`, `CaptureTests` | 重复键、非有限数字、深度脱敏、分块 NDJSON、无分隔符 EOF 失败、字段名 `bytes` 而非 `data`、非 canonical Base64、跨帧 UTF-8 不解码、关闭后锁存、新实例重置 seq、input text/bytes 互斥、surrogate、scroll/resize 整数类型、100 轮随机分块、`check_capture.py` 拒绝覆盖且路径脱敏 |
| `test_probe_safety.py` | `ProbeSafetyTests` | 无 disposable 时 control 不 `Popen`；observe 不能带 input；input 需单独 `--allow-input`；非法 target/session；`stop_owned` 不杀进程树；terminate 超时只 kill 自有 handle；默认报告去掉输出与 argv |
| `test_publish.py` | `PublisherTests` | 清单 hash、路径穿越、未列出的 `.env` 不入库、错误 GitHub 身份、create 为 public 且非 force、dry-run 不触网、已有 checkout 拒绝 |
| `test_repository.py` | `RepositoryPortabilityTests` | `validate()` 通过；元数据 `read_text` 必须 `encoding='utf-8'`；`terminal-valid.ndjson` 无 CRLF，SHA-256 `d206d2ad30aac1814193b2f0423bbf30405d113e6d172adb2a9f5bed34f6f599` |

## 依赖

- 代码：`scripts/herddesk_g0`、`probe_herdr.py`、`publish_github.py`、`validate_repository.py`。
- 数据：[../fixtures](../fixtures/CLAUDE.md)。

与 C# smoke 的 22 项、probe selftest 的 23 项分开报告，不合并为覆盖率。

## 约束

- 断言错误消息时核对脱敏：载荷中的合成私钥/终端文本不得出现在 `ProtocolError` 字符串里。
- 新增回归优先使用合成 fixture。需要 herdr 的检查放探针 live 路径，默认 gitignore。
