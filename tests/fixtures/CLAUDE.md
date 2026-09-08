# tests/fixtures

[根索引](../../CLAUDE.md) · [tests](../CLAUDE.md) · fixtures

生成日期：2026-09-08。合成数据。字段依据 herdr v0.8.2 `src/client/mod.rs:850–1220` 的源码阅读。仓库内没有真实会话抓包。

## 职责

为 Python 协议校验、probe `selftest`、C# 意图对齐提供稳定输入。活动副本在本目录。`docs/plan/fixtures/` 是规划档案中的同类文件，运行测试不要指向那边。`implementation/synthetic-capture.json` 是对本目录 `terminal-valid.ndjson` 的一次离线校验快照。`real-terminal-v082/` 是 HD-004 占位索引：captures 为空，live matrix 为 blocked，不是成功抓包。

## 文件

| 文件 | 用途 |
|---|---|
| `terminal-valid.ndjson` | 合法帧流。故意把一个中文 UTF-8 字符拆到两帧。LF 结尾。SHA-256 `d206d2ad30aac1814193b2f0423bbf30405d113e6d172adb2a9f5bed34f6f599` |
| `input-valid.ndjson` | 合法 stdin 命令。含 Ctrl+C 的 Base64 示例。selftest 不会把它发到真实终端 |
| `invalid-cases.json` | 应拒绝的对象。含故意伪造的 `terminal.granted`（上游无此消息） |
| `endpoint-cases.json` | HD-003 七行模拟矩阵：explicit / default / named / Unicode / ACL denied / cross-user / remote UNC。`simulation=true`，`runtime_pass=false`，`ac03_passed=false`，`all_live_checks=blocked`。不是 Windows 真机连接 |
| `lease-cases.json` | HD-004 十四行 lease 映射模拟。`simulation=true`，`runtime_pass=false`，`ac05_passed=false`，`fixture_origin=synthetic`。不是 observe/control 真机证据 |
| `renderer-cases.json` | HD-005 十八行 renderer L1 模拟。`simulation=true`，`runtime_pass=false`，`ac08_passed=false`，`ac09_passed=false`。不是 WinUI/WebView2/IME 真机证据 |
| `rpc-schema-v1/` | HD-009 合成 snapshot/event。钉 v0.9.0 protocol 22 / schema_version 1 源码 schema 字段。`runtime_schema_sha256=UNVERIFIED`。不是 daemon 抓包，不能把 AC04/AC11 标通过 |
| `real-terminal-v082/` | 真机 capture 占位。`index.json` 的 `captures=[]`，`herdr_executed=false`。禁止把合成帧标成 runtime pass |
| `README.md` | 生成说明与门禁边界 |

## 入口

```powershell
python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson
```

CI 运行上述命令。`probe_herdr.py selftest` 读取 invalid/valid 案例。

## 约束

- 客户端门禁（首帧 full、连续 seq、非空输入、大小限制）属于 HerdDesk。与真实桥差异要记证据，不能改写成“上游违规”。
- 保持 LF。`test_repository.py` 会拒绝 CRLF。
- 禁止提交真实终端内容、凭据、私人路径。
