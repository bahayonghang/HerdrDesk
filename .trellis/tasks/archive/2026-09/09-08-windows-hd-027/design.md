# HD-027 · 技术设计

## 拟建文件

- `filebridge/spec/protocol-v1.md`：规范性 wire/state/path/limits 文本。
- `filebridge/spec/control-message.schema.json`：JSON payload schema；不描述 binary header。
- `filebridge/spec/error-codes.md`、`filebridge/spec/threat-model.md`。
- `filebridge/spec/test-vectors/manifest.json` 及 `valid-list.hdfb`、`valid-empty-write.hdfb`、
  `invalid-oversize-json.hdfb`、`invalid-sequence-gap.hdfb`、`invalid-unknown-kind.hdfb`。
- `filebridge/Cargo.toml`、`Cargo.lock`、`src/lib.rs`、`src/protocol.rs`：只实现 codec，
  本任务不建 `main.rs` 或文件系统 operation。
- `src/HerdDesk.Infrastructure/Files/FileBridgeProtocolCodec.cs`：产品 host 的增量
  header/JSON/data codec，不含文件系统动作。
- `filebridge/tests/protocol_vectors.rs` 与
  `tests/Contract/Files/FileBridgeProtocolVectorTests.cs`：Rust/C# 两端独立读取固定 vectors。
- `docs/adr/0008-filebridge-protocol-v1.md`：锁定决定、兼容性与撤销条件。

这些是未来路径且当前不存在；精确 Cargo 依赖版本须在获批实施时核验并锁入 lockfile。

## 候选 wire v1.0

每帧 16-byte header，整数均为 network byte order：

| offset | bytes | 含义 |
|---:|---:|---|
| 0 | 4 | ASCII magic `HDFB` |
| 4 | 1 | major=`1` |
| 5 | 1 | minor=`0` |
| 6 | 1 | kind |
| 7 | 1 | flags；v1 必须为 0 |
| 8 | 4 | payload length `u32` |
| 12 | 4 | 单方向 sequence，从 0 严格递增 |

magic/version/flags/sequence 不符时立即关闭，不扫描重同步。两方向独立计 seq。首个 client
frame 必须是 RequestJson，之后不得出现第二 request；sequence 不允许回绕，耗尽前必须
结束。v1.0 只接受 major=1/minor=0；进程在一个 Complete/Error 后退出。

| kind | 方向 | payload |
|---|---|---|
| `0x01 RequestJson` | client→helper | job/version/op/path/options |
| `0x02 Data` | 双向、按 op 限制 | 原始文件 bytes |
| `0x03 EndData` | client→helper | 空 payload |
| `0x04 CancelJson` | client→helper | job id/reason code |
| `0x11 AcceptedJson` | helper→client | accepted op/identity/size |
| `0x12 EntryJson` | helper→client | 一个 list entry |
| `0x13 ProgressJson` | helper→client | bytes accepted/read |
| `0x14 CompleteJson` | helper→client | final path/length/SHA-256/commit result |
| `0x7f ErrorJson` | helper→client | stable code/stage/retryable |

list 为 Request→Accepted→Entry*→Complete；stat/rename 无 Data；read 为 Accepted→Data*→
Complete；write 为 Request→Accepted→Data*→EndData，再由 helper 发 Complete/Error。
Cancel 可在 terminal result 前出现；关闭 stdin 是强制取消信号。EOF 未见 terminal result
一律失败，exit 0 也不能补造 Complete。

## operation 与结果 schema

所有 RequestJson 共有 `protocol="1.0"`、canonical UUID `job` 和 `op`。`list` 含目录
`WirePath`、可选 cursor、limit；`stat/read` 含 `WirePath` 与可选 observation。`write` 含
destination parent、单一 raw component、expected parent/target observation、create/replace
模式、十进制字符串 length 和 64 字符 lowercase SHA-256。`rename` 含 source path/source
observation、destination parent/raw component、expected parent/target observation 与模式。
Replace 没有精确 target observation 必须拒绝；KeepBoth 由 HD-028 选候选名后仍发送
exclusive create，不能降成先查后覆盖。

Observation 是 helper 对规范 Unix wire path、existence、稳定 file identity、size、
mtime/precision 与 type 的 canonical bytes 计算的 SHA-256。它只是 stale/conflict 检测值，
不是授权或 sandbox；HD-028 仍须在同一已验证父目录 handle 上 no-follow 地线性化操作。
EntryJson 返回 raw component、独立 display name、metadata 和 observation；CompleteJson 只返
实际 WirePath、length、hash、result observation 与 commit 状态，不返回 shell/display path。

## 限额和解析

- control/entry/error JSON 单 frame 最大 1 MiB，data 最大 1 MiB；先验 header 再分配。
- JSON UTF-8 严格、深度 32、拒绝重复键与 JSON 浮点；job id 为 canonical UUID。可能超过
  53-bit 的 length/size/mtime/precision 一律用无前导零的 canonical 十进制字符串；hash
  为 lowercase hex，opaque bytes/token/cursor 为 padded canonical Base64。
- list `limit` 为 1–1000，控制 payload 累计最多 16 MiB；Complete 必须返回是否还有页。
- cursor 最大 4096 bytes，绑定 directory identity 与最后 raw name；客户端视为 opaque。
- declared transfer length 解码为 `u64`，另受用户批准的产品限额；SHA-256 解码后固定 32 bytes。
- v1.0 未知/缺失字段、kind、version 一律拒绝；未知 error code 保留 bounded raw code，
  只映射为通用远端失败，不把原始详情写日志。

## 路径、元数据与信任

`WirePath` v1 固定为 Unix absolute root `/` 加 Base64 raw byte components。decoded component
禁止 NUL、`/`、空、`.`、`..`；helper 只在 Linux/macOS 启动。list 返回 raw component、
单独的安全 display name、type、size、mtime、file identity 和 symlink 标志；后续请求只
回传 raw component/identity，绝不用 display name。

本地 Windows 列表/读写由 HD-028 的 C# native adapter 直接处理，不经过 filebridge wire。
远端 raw name 下载到 Windows 时，adapter 检出保留名、尾随点/空格、非法 code unit 或
名称冲突并向 UI 返回 mapping-required；不能静默清洗、截断或自动加后缀。

协议不把“隐藏 ..”称为 sandbox。实际 OS 权限仍是权威；write/rename 的目录遍历、
no-follow、同目录 temp 和原子 commit 由 HD-028 实现。冲突 token 包含目标 identity 与
观测版本；改变后必须报 conflict，不允许 UI 的一次确认永久授权未来对象。

helper 只继承 SSH 登录用户权限，无凭据字段、网络 listener、shell command 或 renderer
通道。stdout 任何非 `HDFB` byte 都是污染；stderr 有独立有界读取且默认不落原文日志。

## 取消、提交与进程退出

helper 在 final rename/replace 前设唯一 commit 线性化点。此前收到 Cancel/EOF，必须停止
消费、删除且仅删除该 job 独占创建的 temp，再发 `ErrorJson(code=cancelled)`（若 stdout
仍可用）；线性化后到达的 Cancel 不得把已可见结果报告为 cancelled，必须返回 Complete。
Complete+exit 0 表示成功，Error+非零表示已知失败；frame/exit 不匹配、终帧缺失、连接在
commit 回执前丢失均为 `outcome_unknown`，客户端只能重新 stat/hash/identity 后决定成功、
冲突或重试，不能盲重放 Replace/rename。协议 vector 固定这些 race 与恢复语义。

## 协议锁定门

必须同时满足：规范与 JSON schema 无歧义；Rust/C# 对 golden vectors 字节一致；
随机 partial read 不改变结果；oversize 在 allocation 前拒绝；state transition corpus
覆盖所有 kind、cancel/commit/exit 竞态；安全审查确认没有路径进 argv、generic exec、
未界定 follow-link；HD-028 维护者书面确认能在目标 OS 实现 commit/identity 语义。否则
ADR 状态保持 proposed。
