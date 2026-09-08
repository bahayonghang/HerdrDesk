# herddesk-bridge

[根索引](../CLAUDE.md) · Rust sidecar

HD-008 L1：stdio ↔ 平台 local socket 的纯字节转发。不是 herdr 已有命令。不解析业务 JSON，不创建 shell，不监听 TCP。

## 职责

- CLI：`herddesk-bridge rpc --socket-path <path>`；`--version` 是独立命令。
- stdout 只含 peer bytes；稳定诊断在 stderr（`bridge_endpoint_invalid` / `bridge_connect_denied` / `bridge_connect_failed` / `bridge_relay_failed` / `bridge_usage`）。
- Windows 映射：`Path` → `to_string_lossy` → `to_ns_name::<GenericNamespaced>()`。不把 marker 文件当 RPC 流。
- Unix 映射：`to_fs_name::<GenericFilePath>()`。stdin EOF 后对 socket `Shutdown::Write`，继续把 peer 尾数据写到 stdout。
- Windows 不宣称可移植 half-close。
- 拒绝 empty/NUL、远程 SMB pipe、`herdr-client.sock` / `*-client.sock` 二进制 client socket。
- 每向 64 KiB pump；禁止 `ReadToEnd`、无界 channel、协议数据转 String。

## 入口

```powershell
cargo fmt --manifest-path bridge/Cargo.toml --all -- --check
cargo clippy --manifest-path bridge/Cargo.toml --workspace --all-targets --locked -- -D warnings
cargo test --manifest-path bridge/Cargo.toml --workspace --locked
```

`just ci` 含上述步骤。L2 named-pipe ACL / 真机 endpoint 为 `UNVERIFIED`。

## 依赖

`interprocess` 精确版本见 `bridge/Cargo.lock` 与 `implementation/hd-008-packages.json`。工具链钉 `bridge/rust-toolchain.toml`（1.98.0），不是 herdr 的 1.96.1。
