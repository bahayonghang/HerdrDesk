# Candidate notes (unpublished)

This is an L1 P5 closeout candidate for HD-036. It is **not a complete 1.0**. It is **not published**. External publish was not authorized. `published=false`. `complete_1_0_claimed=false`.

Product AC01–AC48 stay `not_run` in `planning/acceptance.json`. Do not flip those rows from this document. `phase_gate=not_passed`. `g0_passed=false`.

## Verified L1 scope

Offline structure and coordinator tests only. Indexed catalogs:

| Closeout | Path | Live residual |
|---|---|---|
| HD-019 local MVP | `evidence/local-mvp/catalog.json` | live herdr / WebView2 / IME / agent TUI UNVERIFIED |
| HD-026 multi-device | `evidence/multi-device-mvp/catalog.json` | live SSH / WinUI / 3-device UNVERIFIED |
| HD-032 file fault | `evidence/files/catalog.json` | live FS / SSH / TOCTOU / attack UNVERIFIED |
| HD-033 quality | `evidence/quality/catalog.json` | soak / input-to-pixel / DPI / Narrator UNVERIFIED |
| HD-034 packaging | `evidence/packaging/catalog.json` | live install / sign / update / rollback UNVERIFIED |
| HD-035 security | `evidence/security-release/catalog.json` | live scans / renderer process / canary / signed-package UNVERIFIED |
| HD-007 graph / restore | `implementation/hd-007-packages.json` | clean-machine locked restore and GitHub required-check on HEAD UNVERIFIED |

`just ci` is the offline gate. Local `just ci` is not the hosted bar. Hosted Actions on an older SHA is not proof for HEAD. `github_required_check=UNVERIFIED`. `windows_desktop_restore=not_admitted`.

## Limitations

- No admitted WinUI shell, MSIX, Publisher, or signed hash/SBOM.
- No independent-user walkthrough.
- Promised Windows 11 x64 client and Linux x64 remote stay `not_run`. macOS/ARM64 stay unsupported or experimental.
- Core 1.0 does not impersonate 1.x extensions EP-01..EP-07.
- No screenshots. A Markdown link is not test evidence.

Machine files: `evidence/releases/catalog.json`, `evidence/releases/support-matrix.json`, `evidence/releases/ac-index.json`, `implementation/hd-036-l2.json`.
