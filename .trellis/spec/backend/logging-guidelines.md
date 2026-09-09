# Logging Guidelines

G0 has no logging framework (`ILogger`, Serilog, Python `logging` config). Diagnostics are stable exception codes, bounded JSON reports, and the HD-007 `JsonlDiagnosticSink` restricted event stream.

---

## Overview

Do not add a log package. App windows TFM may PackageReference only the admitted WinUI lock; other C# projects stay without PackageReference. Python stays in the standard library; the protocol module raises `ProtocolError` instead of logging payloads. `DiagnosticEvent` has no free-text message, exception, or payload field. `JsonlDiagnosticSink` writes UTF-8 JSONL without a BOM. Writer failures increment `DroppedCount` and must not crash the host.

---

## Log Levels

There is no level schema. Use:

- Exception / `ProtocolError` codes for protocol and policy failures.
- `Write-Host` on the setup script for host and SDK version lines.
- Probe JSON objects for preflight/observe/control summaries.

Do not invent debug traces that print terminal bytes.

---

## Structured Logging

Smoke tests print `PASS <name>` or `FAIL <name>: <ExceptionType>` and a final `N/N smoke tests passed; no live Windows/daemon validation.`

`probe_herdr.py` `safe_summary` keeps `returncode`, `timed_out`, `overflow`, `duration_ms`, `errors`, and byte lengths. Stderr text is added only with `--include-diagnostics`.

Real probe reports go to gitignored `probe-results/`. The public tree holds synthetic fixtures only.

---

## What to Log

- Stable error code (`malformed_terminal_record`, `terminal_stream_not_active`, `control_not_verified`, `explicit_configuration_required`, `remote_unc_rejected`, …).
- Frame counts, decoded byte lengths, hashes of payloads when a capture report needs them (`sha256` of decoded bytes in Python frame summary).
- SDK version and `dotnet` host path on setup (not User profile tool dirs as a pin).

---

## What NOT to Log

- Terminal payload text, ANSI bytes as strings, or JSON record bodies on the failure path.
- Credentials, tokens, private keys, SSH host details, personal filesystem paths.
- Unpaired surrogate characters in exception messages (C# smoke asserts the message has no `\\`, `/`, or `\uD800`).
- Full `argv` in default probe summaries.

Public issues follow `SECURITY.md`: synthetic data or an isolated disposable session only.
