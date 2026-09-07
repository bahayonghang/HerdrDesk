# tests

[根索引](../CLAUDE.md) · 测试

生成日期：2026-09-08。三套检查分开计数，不合并覆盖率。

| 套件 | 索引 | 规模 | 运行方式 |
|---|---|---|---|
| Python unittest | [python](python/CLAUDE.md) | 73 | `python -m unittest discover -s tests/python -v` |
| C# smoke | [HerdDesk.Core.SmokeTests](HerdDesk.Core.SmokeTests/CLAUDE.md) | 22 | `dotnet run --project tests/HerdDesk.Core.SmokeTests` |
| 合成 fixture | [fixtures](fixtures/CLAUDE.md) | NDJSON/JSON | `check_capture.py`、probe `selftest` |

probe `selftest` 另有 23 项合成检查，入口在 [../scripts](../scripts/CLAUDE.md)。

规划层级 L0–L4 见 `docs/plan/docs/10_测试与验收.md`。当前 CI 只跑 L0/L1 离线部分。L2 真机目录拟建 `tests/fixtures/real-terminal-v082/`（HD-004），仓库中尚不存在。

无 xUnit、无 `dotnet test`、无 pytest。
