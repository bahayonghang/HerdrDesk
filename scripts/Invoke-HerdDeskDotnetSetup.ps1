#requires -Version 7.0
<# Align this Windows session and the user environment with global.json.

   Pins the machine-wide SDK host, not C:\Users\...\Tools\dotnet.
   Does not install WinUI workloads, NuGet packages, herdr, or SSH. #>
[CmdletBinding()]
param(
    [string]$ExpectedSdk = '10.0.400',
    [string]$MachineHost = 'C:\Program Files\dotnet'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

function Assert-Python {
    $python = Get-Command python -ErrorAction Stop
    & $python.Source -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)"
    if ($LASTEXITCODE -ne 0) { throw "Python 3.10+ is required. Found: $(& $python.Source --version 2>&1)" }
    Write-Host "Python: $(& $python.Source --version 2>&1)"
}

function Get-SdkDir([string]$hostDir) {
    return Join-Path $hostDir "sdk\$ExpectedSdk"
}

function Install-PinnedSdk {
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "SDK $ExpectedSdk is missing under $MachineHost. Install it from https://dotnet.microsoft.com/en-us/download/dotnet/10.0"
    }
    Write-Host "Installing .NET SDK $ExpectedSdk with winget (Microsoft.DotNet.SDK.10)."
    & $winget.Source install --id Microsoft.DotNet.SDK.10 --version $ExpectedSdk --exact --disable-interactivity --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget failed to install SDK $ExpectedSdk (exit $LASTEXITCODE). Install it from https://dotnet.microsoft.com/en-us/download/dotnet/10.0"
    }
}

Assert-Python

$sdkDir = Get-SdkDir $MachineHost
if (-not (Test-Path -LiteralPath $sdkDir)) {
    Install-PinnedSdk
    if (-not (Test-Path -LiteralPath $sdkDir)) {
        throw "SDK $ExpectedSdk still missing at $sdkDir after install."
    }
}

$userRoot = [Environment]::GetEnvironmentVariable('DOTNET_ROOT', 'User')
if ($userRoot -ne $MachineHost) {
    [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $MachineHost, 'User')
    Write-Host "Set User DOTNET_ROOT to $MachineHost (was: $(if ($userRoot) { $userRoot } else { '<empty>' }))"
}
$lookup = [Environment]::GetEnvironmentVariable('DOTNET_MULTILEVEL_LOOKUP', 'User')
if ($lookup -ne '0') {
    [Environment]::SetEnvironmentVariable('DOTNET_MULTILEVEL_LOOKUP', '0', 'User')
    Write-Host 'Set User DOTNET_MULTILEVEL_LOOKUP=0 so global.json is exact.'
}

$env:DOTNET_ROOT = $MachineHost
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:PATH = "$MachineHost;$env:PATH"

$globalJson = Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw -Encoding utf8 | ConvertFrom-Json
if ($globalJson.sdk.version -ne $ExpectedSdk) {
    throw "global.json sdk.version is $($globalJson.sdk.version); this script expects $ExpectedSdk."
}

$dotnet = Join-Path $MachineHost 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw "Missing $dotnet" }

$version = (& $dotnet --version).Trim()
if ($version -ne $ExpectedSdk) {
    throw "dotnet --version is '$version' with host $dotnet; expected $ExpectedSdk from global.json."
}

Write-Host "dotnet host: $dotnet"
Write-Host "dotnet SDK:  $version"
Write-Host "just:        $(if (Get-Command just -ErrorAction SilentlyContinue) { just --version } else { 'missing (install Casey.Just)' })"
Write-Host 'G0 BCL-only setup is ready. WinUI workloads are not installed.'
