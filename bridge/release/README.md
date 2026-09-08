# herddesk-bridge release matrix (HD-021)

Source is the HD-008 crate `bridge/herddesk-bridge` and `bridge/Cargo.lock`.
Runtime loads only the compile-time embedded copy of this directory. It does not accept an arbitrary filesystem path.

## Targets

| Triple | OS/arch probe | Remote deploy | Atomic publish |
|---|---|---|---|
| `x86_64-pc-windows-msvc` | windows / x86_64 | no (remote Windows blocked) | unsupported |
| `x86_64-unknown-linux-gnu` | linux / x86_64 | yes | hard-link no-clobber |
| `aarch64-unknown-linux-gnu` | linux / aarch64 | yes | hard-link no-clobber |
| `x86_64-apple-darwin` | darwin / x86_64 | yes | hard-link no-clobber |
| `aarch64-apple-darwin` | darwin / arm64 | yes | hard-link no-clobber |

A missing native runner is `not_run` for that target. Another architecture's artifact is not a substitute.

## Verify

```powershell
pwsh -NoLogo -File scripts/Build-HerdDeskBridgeRelease.ps1 -VerifyOnly
```

`-VerifyOnly` does not build, download, or deploy. It records local toolchain presence per target.

## Trust

Content trust is the embedded manifest SHA-256 bound to the application version.
HD-034 owns package signature and Publisher identity. This directory does not claim Authenticode on helpers.

L2 live remote install stays UNVERIFIED. Product AC25 is not passed.
