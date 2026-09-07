# .github

[根索引](../CLAUDE.md) · CI

生成日期：2026-09-08。当前唯一工作流：`workflows/ci.yml`，名称 `G0 validation`。

## 触发与权限

`push` 到 `main`、`pull_request`、`workflow_dispatch`。`permissions.contents: read`。concurrency 组 `g0-${{ github.workflow }}-${{ github.ref }}`，取消进行中的 run。矩阵：`ubuntu-latest`、`windows-latest`，`fail-fast: false`，15 分钟超时。

## 步骤

1. checkout（pin 到 2026-09-07 解析的 v6 SHA），`persist-credentials: false`
2. setup-python 3.12（pin v6）
3. `python -m unittest discover -s tests/python -v`
4. `python scripts/probe_herdr.py selftest`
5. `python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson`
6. `python scripts/validate_repository.py`
7. setup-dotnet，读取根 `global.json`（pin v5）
8. `dotnet build HerdDesk.slnx --configuration Release`
9. `dotnet run --project tests/HerdDesk.Core.SmokeTests --configuration Release --no-build`

注释写明：本 job 不含 daemon 安装、SSH、takeover、发布、UI 验收或凭据。

## 约束

- Action 使用 commit SHA pin，不跟浮动 major tag。
- Hosted Windows runner 成功不等于 IME 或真实 herdr 验收（L2/L3）。
- HD-007 仍为 `planned`：现有流水线是 G0 建仓准备，不是完整 format/analyzer/contract 产品门禁。
- 可复查通过记录见 `docs/publication.md` 与 `implementation/status.json`。后续提交以该 SHA 的 Actions 页为准。
