# 检查记录

日期：2026-09-08。源码基线 HEAD `8ae4797d0aa10559648d1ade5b7e017ff46c7a53`。
执行者：独立 quality_release_auditor，当前 Windows 工作区。主线程不重复运行已通过测试。

| 命令/检查 | 当前结果 | 边界 |
|---|---|---|
| rtk proxy just ci | exit 0 | 现有离线 gate |
| Python unittest | 73/73 passed | 合成/标准库测试 |
| probe_herdr.py selftest | 23/23 passed | 不执行 herdr |
| check_capture.py tests/fixtures/terminal-valid.ndjson | passed，3 frames、41 decoded bytes | 合成 capture |
| validate_repository.py | passed，30 JSON、3 projects、36 tasks | 运行时文件数量，非固定验收阈值 |
| dotnet build Release | passed，0 warnings、0 errors | 本机编译，非 GUI |
| C# smoke runner | 22/22 passed | dotnet run，不是 dotnet test |
| python scripts/publish_github.py | exit 2，FAIL | 默认离线核验，未传 --publish |
| publication 全量哈希比对 | 4 changed、0 listed missing | .gitattributes、.gitignore、AGENTS.md、README.md |

新增任务 JSON 会改变结构校验的扫描数量，不影响 36 个 HD 任务/48 AC 的契约。不要据 JSON 文件数固定写测试。

## 历史 GitHub Actions

- [34138300061](https://github.com/bahayonghang/HerdrDesk/actions/runs/34138300061)：Windows 失败，Ubuntu 通过。Windows 默认 locale 解码 planning JSON 报 `charmap codec can't decode byte 0x81`。
- [34138627135](https://github.com/bahayonghang/HerdrDesk/actions/runs/34138627135)：commit `629bb01` 修复 UTF-8 读取及 fixture LF 后双平台通过。
- `8251867` 的后续 run 也双平台通过（完整 run 信息见 [test-and-workflow-evidence.md](test-and-workflow-evidence.md)）。
- 当前 HEAD 比 origin/main 的 `8251867` 多 4 个本地提交，当前 SHA 的 hosted CI 为 UNVERIFIED；不能沿用 README 的历史通过标记。

## 未执行

未安装依赖，未运行 just setup，未修改 User DOTNET_ROOT；未启动真实 herdr/SSH/GUI/IME；未发布、推送或触发 workflow。
未运行五套客户端新会话。以上运行能力全部保持 UNVERIFIED；G0 phase_gate 仍 not_passed，48 产品 AC 仍 not_run。

定向协议复现见 [test-and-workflow-evidence.md](test-and-workflow-evidence.md)：孤立 surrogate 的值/属性名触发 InvalidOperationException，后续合法帧仍被接受，3轮稳定。既有 smoke 全绿未覆盖此异常路径。
独立计划复核发现的 terminal.closed.reason 第三路径也由主线程复现，同样异常逃逸并接受后续帧，见 [plan-review.md](plan-review.md)。
