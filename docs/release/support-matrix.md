# Support matrix (L1 candidate)

Human projection of `evidence/releases/support-matrix.json`. This page is not product AC45. Live platform evidence was not run.

## Promised range (both `not_run`)

| Id | Role | Promise | Support | Live |
|---|---|---|---|---|
| windows-11-x64-client | client | promised | promised_not_run | not_run |
| linux-x64-remote | remote OS | promised | promised_not_run | not_run |

Do not copy Windows client fields onto Linux. Linux x64 is a remote OS, not an MSIX client. Do not claim Linux MSIX.

## Out of promised range

| Id | Support |
|---|---|
| macos-x64 | unsupported |
| macos-arm64 | experimental |
| linux-arm64 | experimental |
| windows-arm64 | experimental |

Do not mark macOS or ARM64 supported without independent evidence.

## Core 1.0 vs 1.x extensions

EP-01..EP-07 stay outside core 1.0. This candidate does not impersonate standalone shell, SSH password/MFA, native renderer replacement, ARM64 client, graphics protocols, or SFTP provider.

`compatible_by_default` is empty. Unknown combinations stay unknown.
