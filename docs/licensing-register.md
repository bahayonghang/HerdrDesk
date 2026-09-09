# HD-002 · License and naming register

Engineering inventory, not legal clearance. This file does not select a project
license and does not mark AC02, G0, or `phase_gate` passed.

Machine-readable units: [licensing/register.json](licensing/register.json).
Candidate templates: [licensing/candidates/](licensing/candidates/). Validator:
`scripts/herddesk_g0/licensing.py`.

| Child AC | This task | Final AC02 |
|---|---|---|
| AC02-C1 | Existing brand, code, docs, and fixture sources recorded | HD-035 reverse-audit of real release inputs; `not_run` |
| AC02-C2 | Future dependency/asset admission templates | same |
| AC02 | not claimed | HD-035 |

Project-wide license remains pending the maintainer. Public visibility is not an
MIT, Apache-2.0, or other license grant. See [LICENSE-STATUS.md](../LICENSE-STATUS.md).

Admission (`approved` / `blocked` / `pending`) is the shippable conclusion.
`technical` and `security` are separate fields. A candidate may be technically
feasible and still `pending` or `blocked`. HD-007, HD-014, HD-021, and HD-034
may use only `approved` items in package lock, WebView bundle, MSIX, or release
manifests. Unknown or conflict stays `blocked`.

Source, prebuilt binary, and runtime download stay on separate records.

## Names

| Name | Kind | Usage | Affiliation | Trademark / package identity |
|---|---|---|---|---|
| HerdDesk | application_solution_namespace | Application, solution, C# namespace | Independent; not an official herdr/herdrm label | not_run |
| HerdrDesk | github_repository | GitHub repository `bahayonghang/HerdrDesk` | Independent; slug differs from the application name | not_run |
| 牧台 | chinese_work_name | Chinese work name | Independent | not_run |
| herdr | upstream_protocol | Upstream daemon and terminal-session protocol | Upstream project | not_run |
| herdrm | product_reference | Behaviour reference only; no copied files | Separate product | not_run |

Owner: HD-002. Final review: HD-035. Evidence date: 2026-09-08.

HD-007 L2 admits `Microsoft.WindowsAppSDK.WinUI` 2.3.6 and its required
nupkg transitives (`Base` 2.0.4, `Foundation` 2.3.9,
`InteractiveExperiences` 2.1.3, `Microsoft.Web.WebView2` 1.0.3719.77) to
the App windows TFM lock. The WASDK umbrella 2.4.0 package and
`Microsoft.NET.Test.Sdk` stay **pending** and out of lock. The WebView2
Evergreen runtime is a separate `runtime_download` record and is not a
lock input. Project-wide license stays pending. This is not AC02 pass.

HD-007 recorded NuGet metadata for `Microsoft.WindowsAppSDK` 2.4.0 and
`Microsoft.NET.Test.Sdk` 18.9.0 as **pending** units. They are not approved,
`lock_allowed` stays false, and they are not PackageReference inputs.

About-box intent from planning: “Independent Windows client for herdr; not an
official herdr/herdrm release.” That sentence is a naming rule, not a license.

## Existing units (AC02-C1)

First-party, host, CI, blocked, and pending probe units below keep
`lock_allowed` and all `enters_*` flags **false**. They are not `approved`.
HD-007 L2 WinUI nupkg units at the end of this list are `approved` for the
App windows lock only. Final review task is HD-035. Evidence date is
2026-09-08 unless a later row says otherwise.

### herddesk-csharp — first_party_source / source

| Field | Value |
|---|---|
| source | Written for HerdDesk in this repository |
| version / hash | G0 working tree; no release tag / null |
| license / notice | not selected; project-wide license pending maintainer / none until selected |
| notice_status | pending |
| modification | original |
| distribution | `src/HerdDesk.Contracts`; `src/HerdDesk.Core`; `src/HerdDesk.Infrastructure`; `src/HerdDesk.Terminal.Web`; `src/HerdDesk.App`; `tests/HerdDesk.Core.SmokeTests`; `tests/Unit`; `tests/Contract`; `HerdDesk.slnx`; `Directory.Build.props`; `global.json`; `NuGet.Config` |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### herddesk-python-scripts — first_party_source / source

| Field | Value |
|---|---|
| source | Written for HerdDesk in this repository |
| version / hash | G0 working tree; no release tag / null |
| license / notice | not selected; pending maintainer / none until selected |
| notice_status | pending |
| modification | original |
| distribution | `scripts/`; `scripts/herddesk_g0`; `tests/python` |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### herddesk-docs-planning — first_party_documentation / source

| Field | Value |
|---|---|
| source | Written for HerdDesk; planning archive includes user-supplied implementation bundle text |
| version / hash | G0 working tree / null |
| license / notice | not selected; pending maintainer / none until selected |
| notice_status | pending |
| modification | original; `docs/plan` archived intact; no third-party application sources bundled |
| distribution | `docs/`; `planning/`; `tasks/`; `evidence/`; `implementation/`; `AGENTS.md`; `README.md`; `LICENSE-STATUS.md`; `SECURITY.md`; `PUBLICATION_MANIFEST.json` |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### herddesk-g0-ci-workflow — first_party_source / source

| Field | Value |
|---|---|
| source | Written for HerdDesk |
| version / hash | G0 working tree / null |
| license / notice | not selected; pending maintainer / none until selected |
| notice_status | pending |
| modification | original |
| distribution | `.github/workflows/ci.yml` |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### synthetic-fixtures-active — synthetic_fixture / source

| Field | Value |
|---|---|
| source | Independently constructed; not an upstream capture |
| version / hash | G0 working tree / `tests/fixtures/terminal-valid.ndjson` SHA-256 `d206d2ad30aac1814193b2f0423bbf30405d113e6d172adb2a9f5bed34f6f599` |
| license / notice | not selected; pending maintainer / synthetic protocol bytes; no real session content |
| notice_status | pending |
| modification | independent construction; field layout follows herdr v0.8.2 client source reading; not a herdrm asset copy |
| distribution | `tests/fixtures` |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

Independent fixtures record test-asset provenance. They do not replace upstream
distribution permission.

### synthetic-fixtures-plan-archive — synthetic_fixture / source

| Field | Value |
|---|---|
| source | Planning-archive copy of independently constructed fixtures |
| version / hash | planning archive 2026-09-07 / same `terminal-valid.ndjson` SHA-256 as the active copy |
| license / notice | not selected; pending maintainer / archive copy; tests load `tests/fixtures` |
| notice_status | pending |
| modification | independent construction archived intact |
| distribution | `docs/plan/fixtures` |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### herdr — upstream_source_reference / source

| Field | Value |
|---|---|
| source | https://github.com/herdrdev/herdr |
| version | v0.8.2 / commit `9eb521456ac0d19d3ab3d9d7cea3cca10baa8a4c` |
| hash | git blob SHA: `src/client/mod.rs` `c33157fb0d9b7c1632562fced6bd9e439de80856`; `src/ipc.rs` `36e69ea2a571de096e3150779eb9cae6e803d7a8`; `src/server/render_stream.rs` `f14deb5e7e2f3183410020e5f36f7fb11dda6780`; `docs/next/api/herdr-api.schema.json` `f9642ffa0deb4dc87052a5247d700e1dcd50a753`; `Cargo.toml` `55ee3d036a39aca5ecbd16f6f6b120136ae872e7`. distribution/runtime SHA-256 null |
| license | Cargo.toml at v0.8.2 declares Apache-2.0 (planning E01). This row records that declaration. It is not a redistribution grant for HerdDesk. |
| notice / notice_status | LICENSE/NOTICE and vendored-dependency review deferred until packaging (HD-034/HD-035) / pending |
| modification | none; source is not vendored in this repository |
| distribution | not in this repository; protocol reference only |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### herdr — upstream_prebuilt_binary / prebuilt_binary

| Field | Value |
|---|---|
| source | https://github.com/herdrdev/herdr/releases/tag/v0.8.2 |
| version / hash | v0.8.2 / all binary SHA-256 null |
| license | Upstream binary redistributability is not recorded here. The Cargo.toml Apache-2.0 declaration does not admit this binary into a HerdDesk package. |
| notice / notice_status | Distribution/NOTICE review deferred until packaging / pending |
| modification | none; binary is not present |
| distribution | not in this repository |
| technical / security / admission | not_evaluated / not_evaluated / pending |
| conflict | false |

### herdr — upstream_runtime_download / runtime_download

| Field | Value |
|---|---|
| source | Not vendored; isolated Windows preview observed, not admitted |
| version / hash | source v0.8.2; runtime CLI `0.9.0-preview.2026-09-08-62431dbd033b` protocol 22 / SHA-256 stay in evidence, not a license grant |
| license | Runtime bits are not in this repository and are not a G0 product input |
| notice / notice_status | HD-001 recorded isolated preview protocol 22; not compatible with source 20; binary not a G0 product input / pending |
| modification | none; runtime download is not present |
| distribution | not in this repository |
| technical / security / admission | not_evaluated / not_evaluated / blocked |
| conflict | false |

### herdrm — product_reference / source

| Field | Value |
|---|---|
| source | https://github.com/missuo/herdrm (reference only; not a dependency) |
| version / hash | v0.5.3 planning evidence tag; not vendored / null |
| license | Root LICENSE not observed in planning evidence E13. No copy authorization is recorded. |
| notice / notice_status | Do not copy source, binaries, icons, fonts, screenshots, or layout resources. / pending |
| modification | none; no files copied |
| distribution | not in this repository |
| vendored / copied_files | false / [] |
| technical / security / admission | not_evaluated / not_evaluated / **blocked** |
| conflict | false |

### cpython-host — host_toolchain / host

| Field | Value |
|---|---|
| source | CPython host; not a product dependency package |
| version / hash | 3.10+ required; CI uses 3.12 / null |
| license / notice | PSF license remains upstream; this repository does not ship CPython / not redistributed |
| notice_status | not_required |
| modification | none |
| distribution | not in this repository; host toolchain |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### dotnet-sdk-host — host_toolchain / host

| Field | Value |
|---|---|
| source | Microsoft .NET SDK; pin in `global.json` |
| version / hash | 10.0.400; `rollForward=disable` / null |
| license / notice | SDK/runtime licenses remain upstream; this repository does not ship the SDK / not redistributed |
| notice_status | not_required |
| modification | none |
| distribution | not in this repository; host toolchain |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### actions-checkout — ci_action / ci

| Field | Value |
|---|---|
| source | https://github.com/actions/checkout |
| version / hash | v6; resolved 2026-09-07 / `d23441a48e516b6c34aea4fa41551a30e30af803` |
| license / notice | Action source remains upstream; not vendored / hosted execution is not product redistribution |
| notice_status | not_required |
| modification | none; commit-pinned in `.github/workflows/ci.yml` |
| distribution | not vendored; CI reference only |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### actions-setup-python — ci_action / ci

| Field | Value |
|---|---|
| source | https://github.com/actions/setup-python |
| version / hash | v6; resolved 2026-09-07 / `ece7cb06caefa5fff74198d8649806c4678c61a1` |
| license / notice | Action source remains upstream; not vendored / hosted execution is not product redistribution |
| notice_status | not_required |
| modification | none; commit-pinned |
| distribution | not vendored; CI reference only |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### actions-setup-dotnet — ci_action / ci

| Field | Value |
|---|---|
| source | https://github.com/actions/setup-dotnet |
| version / hash | v5; resolved 2026-09-07 / `26b0ec14cb23fa6904739307f278c14f94c95bf1` |
| license / notice | Action source remains upstream; not vendored / hosted execution is not product redistribution |
| notice_status | not_required |
| modification | none; commit-pinned |
| distribution | not vendored; CI reference only |
| technical / security / admission | feasible / not_evaluated / pending |
| conflict | false |

### EasyWindowsTerminalControl — third_party_not_introduced / source

| Field | Value |
|---|---|
| source | https://github.com/mitchcapper/EasyWindowsTerminalControl (planning E10) |
| version / hash | master README inspection 2026-09-07 / git blob `62c9073f7f34104e521c023a828643296b8e8f84` (README.md) |
| license / notice | Redistribution terms for a HerdDesk product bundle are not recorded / unofficial WinUI alpha; not introduced |
| notice_status | pending |
| modification | none; not in this repository |
| distribution | not in this repository |
| technical / security / admission | not_evaluated / not_evaluated / **blocked** |
| conflict | false |

The App windows TFM lock admits WinUI 2.3.6 nupkgs listed in
`docs/licensing/register.json`. xterm, fonts, icons, and a published
`herddesk-filebridge` install stay out of lock. Use the candidate templates
for remaining assets. Do not treat planning mentions as admission. The WASDK
2.4.0 umbrella package stays pending.

## HD-007 L2 admitted nupkgs (App windows lock only)

These units have `admission=approved`, `lock_allowed=true`, and
`enters_package_lock=true`. They do not enter MSIX or the release manifest.
Project license stays pending. HD-035 remains the reverse-audit. AC02 stays
`not_run`.

| Unit | Version | artifact_kind | Notes |
|---|---|---|---|
| Microsoft.WindowsAppSDK.WinUI | 2.3.6 | prebuilt_binary | Direct App windows PackageReference |
| Microsoft.WindowsAppSDK.Base | 2.0.4 | prebuilt_binary | Transitive |
| Microsoft.WindowsAppSDK.Foundation | 2.3.9 | prebuilt_binary | Transitive |
| Microsoft.WindowsAppSDK.InteractiveExperiences | 2.1.3 | prebuilt_binary | Transitive; depends on Base 2.0.4 |
| Microsoft.Web.WebView2 | 1.0.3719.77 | prebuilt_binary | Transitive nupkg |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.4654 | prebuilt_binary | Transitive; required by restore |
| Microsoft.Windows.SDK.BuildTools.MSIX | 1.7.251221100 | prebuilt_binary | Transitive; restore mapping only, not product MSIX |
| Microsoft.Web.WebView2 | Evergreen runtime | runtime_download | Separate record; pending; not a lock input |

The `Microsoft.WindowsAppSDK` 2.4.0 umbrella stays pending and must not appear
as a PackageReference.

## Candidate templates (AC02-C2)

| Template | Category | artifact_kind | Fill owner | Final review | Current admission |
|---|---|---|---|---|---|
| [candidates/nuget.template.json](licensing/candidates/nuget.template.json) | nuget_package | prebuilt_binary | HD-007 | HD-035 | pending |
| [candidates/npm.template.json](licensing/candidates/npm.template.json) | npm_package | source | HD-007 | HD-035 | pending |
| [candidates/cargo.template.json](licensing/candidates/cargo.template.json) | cargo_package | source | HD-007 | HD-035 | pending |
| [candidates/renderer-asset.template.json](licensing/candidates/renderer-asset.template.json) | renderer_asset (xterm / WebView / fonts / icons) | source | HD-014 | HD-035 | pending |
| [candidates/bridge-binary.template.json](licensing/candidates/bridge-binary.template.json) | bridge_binary | prebuilt_binary | HD-021 | HD-035 | pending |
| [candidates/fixture.template.json](licensing/candidates/fixture.template.json) | test_fixture | source | HD-019 (HD-026 redaction) | HD-035 | pending |

Each template has `template=true`, null identity/version/hash/license,
`notice_status=pending`, `conflict=false`, and all release flags false. A
template is not a successful admission. Copy a template to a new file to fill
it. Do not set `admission=approved` on the template.

Bridge/filebridge fills must add **separate** records for source, prebuilt
binary, and runtime download.

Replacement path for a blocked candidate: keep it out of build inputs; implement
an independent substitute or drop the feature. Do not copy herdrm files.

## AC02 status

AC02-C1 and AC02-C2 are inventory/template contributions in this register.
AC02 final remains `not_run`. Owner: HD-035.

HD-035 ships an L1 security/license closeout catalog under
`evidence/security-release/`. That catalog does not close AC02. Project license
remains unselected. Public visibility is not a license grant.
