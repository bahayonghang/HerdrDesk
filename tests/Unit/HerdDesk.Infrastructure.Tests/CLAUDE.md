# HerdDesk.Infrastructure.Tests

BCL console runner for configuration atomic write/backup/restore, diagnostic privacy, owned child process, RPC request/subscription (fake `--fake-bridge` child), and HD-013 `TerminalCliTransport` (fake `--fake-terminal` child). Uses temp directories. L2 named-pipe ACL and live herdr remain UNVERIFIED. `--fake-bridge` / `--fake-terminal` are test-host modes, not product CLIs. SchemaV1 decoder fail-closed cases live in Contract tests.
