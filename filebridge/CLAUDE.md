# herddesk-filebridge

[根索引](../CLAUDE.md) · Rust sidecar codec and L1 serve

HD-027 L1 protocol codec plus HD-028 L1 `herddesk-filebridge serve --stdio --protocol 1.0`. One job per process. stdout is protocol only. Working directory is the sandbox root. Direct crate pin: `sha2` 0.10.8. Toolchain `filebridge/rust-toolchain.toml` (1.98.0).

## 职责

- 16-byte `HDFB` header、kind、双向 sequence、单 job 状态机。
- 严格 JSON（UTF-8、深度 32、拒重复键与 JSON 浮点）。
- Unix WirePath：`/` + 最短合法 padded canonical Base64 组件。
- L1 local FS：exclusive same-directory temp、no-replace rename、SHA-256/length。Replace 在 helper 上 unsupported。
- 与 C# `HerdDesk.Infrastructure.Files.FileBridgeProtocolCodec` 各自读取 `spec/test-vectors/`。

## 入口

```powershell
cargo fmt --manifest-path filebridge/Cargo.toml --all -- --check
cargo clippy --manifest-path filebridge/Cargo.toml --workspace --all-targets --locked -- -D warnings
cargo test --manifest-path filebridge/Cargo.toml --workspace --locked
```

L2 文件系统 / SSH / TOCTOU 为 `UNVERIFIED`。AC30/AC31/AC32/AC34/G0 未通过。ADR-0008 状态为 accepted（wire only）。
