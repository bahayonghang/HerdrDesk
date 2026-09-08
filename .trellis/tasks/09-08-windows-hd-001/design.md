# HD-001 设计

## 证据记录模型

每条记录固定 `subject`、`environment`、`observed_at`、`evidence_level`、
`version`、`hashes`、`result`、`redaction`、`limitations` 与原始附件索引。来源
hash、二进制 hash、运行 schema hash 为不同字段；现有 baseline 已说明不可混同。

## 采集线与依赖

Windows 线在隔离 pane 采集 CLI version、daemon ping、server protocol/schema、
显式 endpoint 和 named-pipe ACL。Remote 线在独立可授权测试设备采集对应事实。
两线共用格式、独立结论。HD-003 只消费 endpoint/ACL 事实；HD-004 只消费
terminal CLI/schema 事实；两者不反向更改版本结论。

## 拟建产物

- `evidence/compatibility-baseline.json` 的扩展字段或相邻证据索引。
- `evidence/runtime/` 下的脱敏 Windows/remote 采样及差异说明。
- 对下游的 version-support matrix；未知版本不进入默认兼容范围。

## 撤销点

发现漂移时关闭自动判断；需要无授权 live 操作时不执行；remote 证据缺失时保持
独立未知，不阻塞 Windows G0 证据闭合。

## 可信来源与冲突矩阵

| 字段 | 首选可信来源 | 交叉来源 | 冲突处置 |
|---|---|---|---|
| CLI version | 隔离主机上实际 `--version` 输出 | 分发 binary hash | 记录 runtime 优先，停止版本假设 |
| daemon version/ping | 同一 user/session 的实际 CLI/API 输出 | 已启动 daemon 的脱敏诊断 | 不以 client version 替代 |
| server protocol/schema | daemon 返回的实际 schema/version | 固定 runtime schema hash | 不以 repo schema 替代 |
| binary hash | 实际执行文件的 SHA-256 | 受信发布清单 | blob SHA 只作 source 关联 |
| endpoint/ACL | Windows 当前用户实际映射和 ACL | 显式配置 | 不从路径约定推断 |

冲突表的行键是 `(OS, arch, CLI binary hash, daemon version, protocol, schema hash)`；
列为 source、Windows runtime、remote runtime。任一关键字段未知的组合不得标为
compatible。若 client/schema 和 daemon/schema 不一致，HD-003/008 必须改为显式
阻断并给出诊断，而非尝试降级写入。

## 采集格式

运行附件采用 UTF-8 JSON，包含固定 `capture_id`、`kind`、`captured_at_utc`、
`operator_scope`、`host_fingerprint_redacted`、`command_redacted`、`exit_code`、
`stdout_sha256`、`stderr_sha256`、`evidence_level`、`limitations`。原始 stdout/stderr
若含路径、token、terminal 内容，只保留 hash、红线版本和可复查的本地受限索引。

版本采集的默认行动是只读。后续实施可候选使用 `probe_herdr.py preflight` 的
`--herdr`、`--session`、`--output`、`--target`、`--disposable-target` 参数；实际 argv
必须以当时 `--help` 输出和获批范围为准，不能把本计划固定为可执行授权。
