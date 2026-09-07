#requires -Version 7.0
<# Verify this Windows session against global.json.

   Default is read-only: Python 3.10+, the SDK version in global.json, and the
   resolved host. Does not install the SDK or write User environment.

   Opt-in: -InstallPinnedSdk uses winget. -PersistUserEnvironment writes User
   DOTNET_ROOT and DOTNET_MULTILEVEL_LOOKUP.

   Child pwsh process PATH/DOTNET_ROOT changes do not return to the parent
   shell. The final check is the host that the next just build will use.

   Pins the machine-wide SDK host, not C:\Users\...\Tools\dotnet.
   Does not install WinUI workloads, NuGet packages, herdr, or SSH. #>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$ExpectedSdk = '',
    [string]$MachineHost = 'C:\Program Files\dotnet',
    [switch]$InstallPinnedSdk,
    [switch]$PersistUserEnvironment
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:HerdDeskDotnetSetupDirect = $MyInvocation.InvocationName -ne '.'
$script:RepoRoot = Split-Path -Parent $PSScriptRoot

function Assert-Python {
    $python = Get-Command python -ErrorAction Stop
    & $python.Source -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)"
    if ($LASTEXITCODE -ne 0) { throw "Python 3.10+ is required. Found: $(& $python.Source --version 2>&1)" }
    Write-Host "Python: $(& $python.Source --version 2>&1)"
}

function Get-PinnedSdkVersion([string]$RepoRoot) {
    $path = Join-Path $RepoRoot 'global.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $path" }
    $globalJson = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
    $version = [string]$globalJson.sdk.version
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'global.json sdk.version is missing.' }
    return $version
}

function Get-SdkDir([string]$hostDir, [string]$sdkVersion) {
    return Join-Path $hostDir "sdk\$sdkVersion"
}

function Resolve-DotnetHost([string]$hostDir) {
    $exe = Join-Path $hostDir 'dotnet.exe'
    if (Test-Path -LiteralPath $exe -PathType Leaf) { return $exe }
    foreach ($name in @('dotnet.cmd', 'dotnet.bat')) {
        $candidate = Join-Path $hostDir $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw "Missing $exe"
}

function Install-PinnedSdk {
    param(
        [Parameter(Mandatory = $true)][string]$SdkVersion,
        [Parameter(Mandatory = $true)][string]$MachineHost
    )
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "SDK $SdkVersion is missing under $MachineHost. Install it from https://dotnet.microsoft.com/en-us/download/dotnet/10.0"
    }
    Write-Host "Installing .NET SDK $SdkVersion with winget (Microsoft.DotNet.SDK.10)."
    & $winget.Source install --id Microsoft.DotNet.SDK.10 --version $SdkVersion --exact --disable-interactivity --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget failed to install SDK $SdkVersion (exit $LASTEXITCODE). Install it from https://dotnet.microsoft.com/en-us/download/dotnet/10.0"
    }
}

function Set-UserDotnetEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$MachineHost
    )
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
}

function Invoke-HerdDeskDotnetSetup {
    [CmdletBinding(PositionalBinding = $false)]
    param(
        [string]$ExpectedSdk = '',
        [string]$MachineHost = 'C:\Program Files\dotnet',
        [switch]$InstallPinnedSdk,
        [switch]$PersistUserEnvironment
    )

    Set-Location -LiteralPath $script:RepoRoot
    $sdkVersion = Get-PinnedSdkVersion -RepoRoot $script:RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSdk) -and $ExpectedSdk -ne $sdkVersion) {
        throw "global.json sdk.version is $sdkVersion; this script expects $ExpectedSdk."
    }
    if ([string]::IsNullOrWhiteSpace($MachineHost)) { throw 'MachineHost is required.' }

    Assert-Python

    $sdkDir = Get-SdkDir $MachineHost $sdkVersion
    if (-not (Test-Path -LiteralPath $sdkDir)) {
        if (-not $InstallPinnedSdk) {
            throw "SDK $sdkVersion is missing under $MachineHost. Install it from https://dotnet.microsoft.com/en-us/download/dotnet/10.0 or re-run with -InstallPinnedSdk."
        }
        Install-PinnedSdk -SdkVersion $sdkVersion -MachineHost $MachineHost
        if (-not (Test-Path -LiteralPath $sdkDir)) {
            throw "SDK $sdkVersion still missing at $sdkDir after install."
        }
    }

    $env:DOTNET_ROOT = $MachineHost
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:PATH = "$MachineHost;$env:PATH"

    $dotnet = Resolve-DotnetHost $MachineHost
    $version = (& $dotnet --version).Trim()
    if ($version -ne $sdkVersion) {
        throw "dotnet --version is '$version' with host $dotnet; expected $sdkVersion from global.json."
    }

    if ($PersistUserEnvironment) {
        Set-UserDotnetEnvironment -MachineHost $MachineHost
    }

    Write-Host "dotnet host: $dotnet"
    Write-Host "dotnet SDK:  $version"
    Write-Host "just:        $(if (Get-Command just -ErrorAction SilentlyContinue) { just --version } else { 'missing (install Casey.Just)' })"
    if (-not $InstallPinnedSdk -and -not $PersistUserEnvironment) {
        Write-Host 'Read-only check complete. SDK was not installed. User environment was not written.'
    }
    Write-Host 'Child pwsh process PATH/DOTNET_ROOT changes do not return to the parent shell.'
    Write-Host 'The final check is the host that the next just build will use.'
    Write-Host 'G0 BCL-only setup is ready. WinUI workloads are not installed.'
}

if ($script:HerdDeskDotnetSetupDirect) {
    Invoke-HerdDeskDotnetSetup @PSBoundParameters
}
