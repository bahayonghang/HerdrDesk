# HD-035 L1 residual report

This file is an L1 residual list. It is not a release decision. It does not mark AC02, AC43, AC44, or G0 passed. It does not conclude that a candidate is releasable.

## Tested (L1 structure)

- Catalog, support matrix, inventory pointer, and live template/not-run pairs under `evidence/security-release/`.
- Inventory units match `docs/licensing/register.json` admissions (`pending` or `blocked`). No approved admissions.
- L1 artifacts exist: HD-002 register, HD-006 ADR baseline, HD-014 renderer allowlist, HD-020/024 SSH fail-closed, HD-031 clipboard/OSC52/cache, HD-032 file-fault catalog, HD-034 packaging catalog.
- Offline structure checks reject pass claims, fake zero-vuln scans, public-visibility-as-license, herdrm copy, and invented scan dates as success.

## Failed

- None recorded at L1 structure. No live scan, process, canary, or unpack run was executed, so no live failure was observed.

## Uncovered

- Maintainer project-license decision.
- herdrm reverse-audit of actual release inputs.
- NuGet advisory scan (no PackageReference lock).
- Cargo advisory scan (locks exist; live advisory database not run).
- npm audit (no npm lock).
- Live WebView/host process observation on Windows 11 x64.
- Live canary diagnostic export.
- Signed-package unpack and reverse-audit (HD-034 has no signed MSIX).

A missing scan tool or failed download stays uncovered. It is not zero vulnerabilities. A failed scan must not be overwritten by a later success.
