# herddesk-filebridge

[根索引](../CLAUDE.md) · Rust sidecar codec

HD-027 L1：filebridge protocol v1.0 的独立 codec。无文件系统、无 `main.rs`、无已发布二进制。`herddesk-filebridge serve --stdio --protocol 1.0` **未实现**。

## 职责

- 16-byte `HDFB` header、kind、双向 sequence、单 job 状态机。
- 严格 JSON（UTF-8、深度 32、拒重复键与 JSON 浮点）。
- Unix WirePath：`/` + 最短合法 padded canonical Base64 组件。
- 与 C# `HerdDesk.Infrastructure.Files.FileBridgeProtocolCodec` 各自读取 `spec/test-vectors/`。
- 零额外 Cargo crate。工具链 `filebridge/rust-toolchain.toml`（1.98.0）。

## 入口

```powershell
cargo fmt --manifest-path filebridge/Cargo.toml --all -- --check
cargo clippy --manifest-path filebridge/Cargo.toml --workspace --all-targets --locked -- -D warnings
cargo test --manifest-path filebridge/Cargo.toml --workspace --locked
```

`just ci` 含上述步骤。L2 文件系统 / SSH / TOCTOU 为 `UNVERIFIED`。AC30/AC34/G0 未通过。ADR-0008 状态为 proposed。
