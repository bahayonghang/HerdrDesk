#requires -Version 7.0
<# Verify or record the HD-008 herddesk-bridge release matrix. Does not download,
   deploy, write /usr or /opt, or mark a missing runner as passed. #>
[CmdletBinding()]
param(
    [switch]$VerifyOnly,
    [switch]$Build
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Build -and $VerifyOnly) { throw 'Use only one of -VerifyOnly or -Build.' }
if (-not $Build) { $VerifyOnly = $true }

$root = Split-Path -Parent $PSScriptRoot
$targetsPath = Join-Path $root 'bridge/release/targets.json'
$manifestPath = Join-Path $root 'bridge/release/manifest.json'
$indexPath = Join-Path $root 'bridge/release/index.json'
$lockPath = Join-Path $root 'bridge/Cargo.lock'
$cratePath = Join-Path $root 'bridge/herddesk-bridge/Cargo.toml'
foreach ($path in @($targetsPath, $manifestPath, $indexPath, $lockPath, $cratePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $path" }
}

$targets = Get-Content -LiteralPath $targetsPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($targets.bridge_source -ne 'bridge/herddesk-bridge') { throw 'bridge_source mismatch' }
if ($targets.lockfile -ne 'bridge/Cargo.lock') { throw 'lockfile mismatch' }
if ($targets.targets.Count -ne 5) { throw 'expected five closed targets' }

$hostTriple = ''
if (Get-Command rustc -ErrorAction SilentlyContinue) {
    $hostTriple = (rustc -vV | Where-Object { $_ -like 'host:*' } | ForEach-Object { $_.Substring(5).Trim() } | Select-Object -First 1)
}

$dirty = $false
Push-Location $root
try {
    $status = git status --porcelain --untracked-files=no -- bridge/herddesk-bridge bridge/Cargo.lock bridge/Cargo.toml bridge/rust-toolchain.toml 2>$null
    if ($LASTEXITCODE -eq 0 -and $status) { $dirty = $true }
}
finally { Pop-Location }

if ($Build -and $dirty) { throw 'Refusing to build a dirty HD-008 bridge source tree.' }

$rows = @()
foreach ($target in $targets.targets) {
    $present = $false
    if ($hostTriple -and $hostTriple -eq $target.triple) { $present = $true }
    $status = 'not_run'
    if ($Build -and $present) { $status = 'local_build_requested' }
    $rows += [pscustomobject]@{
        triple            = $target.triple
        local_runner      = $present
        build_status      = $status
        remote_deploy     = [bool]$target.remote_deploy
        atomic_publish    = $target.atomic_publish
    }
}

$result = [pscustomobject]@{
    schema_version = 1
    document_kind  = 'hd021_bridge_release_verify'
    verify_only    = [bool]$VerifyOnly
    build          = [bool]$Build
    dirty_source   = $dirty
    rustc_host     = $hostTriple
    source         = 'bridge/herddesk-bridge'
    lockfile       = 'bridge/Cargo.lock'
    targets        = $rows
    ac25_passed    = $false
    g0_passed      = $false
}
$result | ConvertTo-Json -Depth 6
if ($rows.Count -ne 5) { throw 'target row count mismatch' }
foreach ($row in $rows) {
    if ($row.build_status -eq 'passed' -and -not $row.local_runner) {
        throw "Refusing to record a pass without a local runner for $($row.triple)."
    }
}
