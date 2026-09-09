# .github

[根索引](../CLAUDE.md) · CI

生成日期：2026-09-08。当前唯一工作流：`workflows/ci.yml`，名称 `G0 validation`。

## 触发与权限

`push` 到 `main`、`pull_request`、`workflow_dispatch`。`permissions.contents: read`。concurrency 组 `g0-${{ github.workflow }}-${{ github.ref }}`，取消进行中的 run。矩阵：`ubuntu-latest`、`windows-latest`，`fail-fast: false`，25 分钟超时。

## 步骤

1. checkout（pin 到 2026-09-07 解析的 v6 SHA），`persist-credentials: false`
2. setup-python 3.12（pin v6）
3. setup-node 22 LTS（pin v7）；`npm --prefix web/terminal ci` / typecheck / build / test
4. `python -m unittest discover -s tests/python -v`
5. `python scripts/probe_herdr.py selftest`
6. `python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson`
7. `python scripts/validate_repository.py`
8. setup-dotnet，读取根 `global.json`（pin v5）
8. `dotnet build HerdDesk.slnx --configuration Release`
9. `dotnet format` on HD-007/HD-008 paths (`src/HerdDesk.Contracts/Rpc`、`tests/HerdDesk.TestSupport` 已纳入；full-solution format 仍被既有 SmokeTests 空白挡住)
10. C# smoke + Core/Infrastructure unit + contract（均为 `dotnet run`）
11. `working-directory: bridge`：`rustup show`，`cargo fmt --check`，`clippy -D warnings`，`cargo test --locked`
12. `working-directory: filebridge`：同样的 fmt/clippy/test。无 `main.rs`。L2 FS/SSH 仍 UNVERIFIED。

另有 `windows-desktop` job：只跑 `scripts/run_windows_desktop_gate.py`。WinUI 未准入时跳过 restore。Job 成功不是 merge-blocking 证据（`github_required_check=UNVERIFIED`）。

注释写明：本 job 不含 daemon 安装、SSH、takeover、发布、UI 验收或凭据。Linux 不编译 WinUI。

## 约束

- Action 使用 commit SHA pin，不跟浮动 major tag。
- Hosted Windows runner 成功不等于 IME 或真实 herdr 验收（L2/L3）。
- HD-007 已接入 format/unit/contract 与 Windows desktop skip helper。AC39/AC40/AC47 与 G0 仍未通过。
- 可复查通过记录见 `docs/publication.md` 与 `implementation/status.json`。后续提交以该 SHA 的 Actions 页为准。
