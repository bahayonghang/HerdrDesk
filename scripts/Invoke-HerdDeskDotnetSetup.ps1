#requires -Version 7.0
<# Verify this Windows session against global.json.

   Default is read-only: Python 3.10+, an SDK that satisfies the
   global.json floor version and rollForward policy, and the
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

function ConvertTo-HerdDeskSdkNumber([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $t = $text.Trim()
    if ($t -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?<pre>[-+].+)?$') {
        return $null
    }
    $patch = [int]$Matches.patch
    $pre = ''
    if ($Matches.ContainsKey('pre') -and $null -ne $Matches.pre) {
        $pre = [string]$Matches.pre
    }
    return [pscustomobject]@{
        Major       = [int]$Matches.major
        Minor       = [int]$Matches.minor
        Patch       = $patch
        FeatureBand = [int][Math]::Floor($patch / 100)
        Prerelease  = -not [string]::IsNullOrWhiteSpace($pre)
        Text        = $t
    }
}

function Test-HerdDeskSdkAtLeast($installed, $pin) {
    if ($installed.Major -ne $pin.Major) { return $installed.Major -gt $pin.Major }
    if ($installed.Minor -ne $pin.Minor) { return $installed.Minor -gt $pin.Minor }
    return $installed.Patch -ge $pin.Patch
}

function Test-HerdDeskSdkSatisfiesPin($installed, $pin, [string]$rollForward, [bool]$allowPrerelease) {
    if ($installed.Prerelease -and -not $allowPrerelease) { return $false }
    $policy = $rollForward.Trim()
    switch ($policy) {
        'disable' {
            return (-not $installed.Prerelease) -and
            $installed.Major -eq $pin.Major -and
            $installed.Minor -eq $pin.Minor -and
            $installed.Patch -eq $pin.Patch
        }
        { $_ -in @('patch', 'latestPatch') } {
            return $installed.Major -eq $pin.Major -and
            $installed.Minor -eq $pin.Minor -and
            $installed.FeatureBand -eq $pin.FeatureBand -and
            $installed.Patch -ge $pin.Patch
        }
        { $_ -in @('feature', 'latestFeature') } {
            return $installed.Major -eq $pin.Major -and
            $installed.Minor -eq $pin.Minor -and
            (Test-HerdDeskSdkAtLeast $installed $pin)
        }
        { $_ -in @('minor', 'latestMinor') } {
            return $installed.Major -eq $pin.Major -and (Test-HerdDeskSdkAtLeast $installed $pin)
        }
        { $_ -in @('major', 'latestMajor') } {
            return Test-HerdDeskSdkAtLeast $installed $pin
        }
        default { throw "Unsupported global.json sdk.rollForward '$rollForward'." }
    }
}

function Get-SdkPin([string]$RepoRoot) {
    $path = Join-Path $RepoRoot 'global.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $path" }
    $globalJson = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json
    $version = [string]$globalJson.sdk.version
    if ([string]::IsNullOrWhiteSpace($version)) { throw 'global.json sdk.version is missing.' }
    $rollForward = 'latestPatch'
    $rollProp = $globalJson.sdk.PSObject.Properties['rollForward']
    if ($null -ne $rollProp -and -not [string]::IsNullOrWhiteSpace([string]$rollProp.Value)) {
        $rollForward = [string]$rollProp.Value
    }
    $allowPrerelease = $false
    $preProp = $globalJson.sdk.PSObject.Properties['allowPrerelease']
    if ($null -ne $preProp) {
        $allowPrerelease = [bool]$preProp.Value
    }
    $parsed = ConvertTo-HerdDeskSdkNumber $version
    if ($null -eq $parsed) { throw "global.json sdk.version is not a release SDK version: $version" }
    return [pscustomobject]@{
        Version         = $version
        RollForward     = $rollForward
        AllowPrerelease = $allowPrerelease
        Parsed          = $parsed
    }
}

function Get-PinnedSdkVersion([string]$RepoRoot) {
    return (Get-SdkPin -RepoRoot $RepoRoot).Version
}

function Test-HostHasCompatibleSdk([string]$hostDir, $pin) {
    $sdkRoot = Join-Path $hostDir 'sdk'
    if (-not (Test-Path -LiteralPath $sdkRoot -PathType Container)) { return $false }
    foreach ($dir in (Get-ChildItem -LiteralPath $sdkRoot -Directory)) {
        $parsed = ConvertTo-HerdDeskSdkNumber $dir.Name
        if ($null -eq $parsed) { continue }
        if (Test-HerdDeskSdkSatisfiesPin $parsed $pin.Parsed $pin.RollForward $pin.AllowPrerelease) {
            return $true
        }
    }
    return $false
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
    Write-Host "Installing a .NET SDK compatible with $SdkVersion with winget (Microsoft.DotNet.SDK.10)."
    & $winget.Source install --id Microsoft.DotNet.SDK.10 --disable-interactivity --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget failed to install an SDK compatible with $SdkVersion (exit $LASTEXITCODE). Install it from https://dotnet.microsoft.com/en-us/download/dotnet/10.0"
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
        Write-Host 'Set User DOTNET_MULTILEVEL_LOOKUP=0 so this machine host is the only SDK lookup.'
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
    $pin = Get-SdkPin -RepoRoot $script:RepoRoot
    $sdkVersion = $pin.Version
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSdk) -and $ExpectedSdk -ne $sdkVersion) {
        throw "global.json sdk.version is $sdkVersion; this script expects $ExpectedSdk."
    }
    if ([string]::IsNullOrWhiteSpace($MachineHost)) { throw 'MachineHost is required.' }

    Assert-Python

    if (-not (Test-HostHasCompatibleSdk $MachineHost $pin)) {
        if (-not $InstallPinnedSdk) {
            throw "SDK $sdkVersion is missing under $MachineHost. Install it from https://dotnet.microsoft.com/en-us/download/dotnet/10.0 or re-run with -InstallPinnedSdk."
        }
        Install-PinnedSdk -SdkVersion $sdkVersion -MachineHost $MachineHost
        if (-not (Test-HostHasCompatibleSdk $MachineHost $pin)) {
            throw "SDK $sdkVersion still missing under $MachineHost after install."
        }
    }

    $env:DOTNET_ROOT = $MachineHost
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:PATH = "$MachineHost;$env:PATH"

    $dotnet = Resolve-DotnetHost $MachineHost
    $version = (& $dotnet --version).Trim()
    $resolved = ConvertTo-HerdDeskSdkNumber $version
    if ($null -eq $resolved -or -not (Test-HerdDeskSdkSatisfiesPin $resolved $pin.Parsed $pin.RollForward $pin.AllowPrerelease)) {
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
