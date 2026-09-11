#requires -Version 7.0
<# HD-034 L2 unsigned local layout. Not a release install.

   -Action Build  publishes the unpackaged App windows TFM (win-x64) to a layout
                  and stamps packaging/Package.appxmanifest. Signature skipped.
   -Action Verify checks lab identity, layout hash, and no private key material.
   -Action Sign   requires -CertificatePath. Fails closed without it.

   Does not invent a production Publisher. Does not write certs into git.
   Does not install, update, or publish to Store/WinGet. #>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Build', 'Verify', 'Sign')]
    [string]$Action,

    [string]$OutputRoot = '',
    [string]$LayoutPath = '',
    [string]$PackagePath = '',
    [string]$CertificatePath = '',
    [switch]$Restore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:LabName = 'HerdDesk.Lab'
$script:LabPublisher = 'CN=HerdDesk Lab (not release)'
$script:LabPublisherDisplay = 'HerdDesk Lab (not release)'
$script:TargetFramework = 'net10.0-windows10.0.19041.0'
$script:RuntimeIdentifier = 'win-x64'
$script:MinOs = '10.0.17763.0'
$script:WinUiVersion = '2.3.6'
$script:PrivateKeyGlobs = @('*.pfx', '*.p12', '*.pem', '*.key', '*.snk', 'id_rsa*', 'id_ed25519*')

function Get-DefaultOutputRoot {
    return (Join-Path $script:RepoRoot 'artifacts\packaging')
}

function Resolve-OutputRoot([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return Get-DefaultOutputRoot }
    if ([System.IO.Path]::IsPathRooted($value)) { return $value }
    return Join-Path $script:RepoRoot $value
}

function Resolve-LayoutPath([string]$outputRoot, [string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return (Join-Path $outputRoot 'layout') }
    if ([System.IO.Path]::IsPathRooted($value)) { return $value }
    return Join-Path $script:RepoRoot $value
}

function Write-ReportObject([System.Collections.IDictionary]$report) {
    $json = [pscustomobject]$report | ConvertTo-Json -Depth 8
    Write-Output $json
    $outDir = [string]$report['output_root']
    if (-not [string]::IsNullOrWhiteSpace($outDir)) {
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        $path = Join-Path $outDir 'package-report.json'
        Set-Content -LiteralPath $path -Value $json -Encoding utf8NoBOM
    }
}

function Test-PathUnder([string]$child, [string]$parent) {
    if ([string]::IsNullOrWhiteSpace($child) -or [string]::IsNullOrWhiteSpace($parent)) { return $false }
    if (-not (Test-Path -LiteralPath $parent)) { return $false }
    $parentFull = (Resolve-Path -LiteralPath $parent).Path.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    return $child.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$ArgumentList
    )
    & $FilePath @ArgumentList 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw ("{0} failed with exit {1}" -f ([IO.Path]::GetFileName($FilePath)), $LASTEXITCODE)
    }
}

function New-BaseReport([string]$actionName, [string]$outputRoot, [string]$layout) {
    return [ordered]@{
        schema_version                              = 1
        document_kind                               = 'hd034_package_release_report'
        action                                      = $actionName
        method                                      = 'dotnet_publish_layout'
        packaging_project                           = $false
        windows_package_type                        = 'None'
        enable_msix_tooling                         = $false
        runtime_identifier                          = $script:RuntimeIdentifier
        target_framework                            = $script:TargetFramework
        target_platform_min_version                 = $script:MinOs
        winui_version                               = $script:WinUiVersion
        windows_app_sdk_umbrella_version            = $null
        webview2_kind                               = 'evergreen_runtime'
        webview2_nupkg_is_not_runtime_claim         = $true
        identity_name                               = $null
        publisher                                   = $null
        processor_architecture                      = $null
        version                                     = $null
        signed                                      = $false
        signature                                   = 'skipped'
        unsigned_local_build_is_release             = $false
        is_release_install                          = $false
        lab_identity_not_store                      = $true
        lab_identity_not_release                    = $true
        publisher_identity_confirmed                = $false
        signed_msix_built                           = $false
        private_key_found                           = $false
        layout_sha256                               = $null
        msix_sha256                                 = $null
        layout_path                                 = $layout
        package_path                                = $null
        output_root                                 = $outputRoot
        ac41_passed                                 = $false
        ac42_passed                                 = $false
        g0_passed                                   = $false
        phase_gate                                  = 'not_passed'
        ok                                          = $false
        error                                       = $null
    }
}

function Assert-AppCsprojMethod {
    $csprojPath = Join-Path $script:RepoRoot 'src\HerdDesk.App\HerdDesk.App.csproj'
    if (-not (Test-Path -LiteralPath $csprojPath -PathType Leaf)) {
        throw "Missing $csprojPath"
    }
    $text = Get-Content -LiteralPath $csprojPath -Raw -Encoding utf8
    if ($text -notmatch '<WindowsPackageType>None</WindowsPackageType>') {
        throw 'App csproj must stay WindowsPackageType=None; packaging is a publish layout overlay.'
    }
    if ($text -notmatch '<EnableMsixTooling>false</EnableMsixTooling>') {
        throw 'App csproj must keep EnableMsixTooling=false.'
    }
    if ($text -notmatch '<RuntimeIdentifier>win-x64</RuntimeIdentifier>') {
        throw 'App csproj RuntimeIdentifier must be win-x64.'
    }
    if ($text -notmatch 'net10\.0-windows10\.0\.19041\.0') {
        throw 'App csproj windows TFM must be net10.0-windows10.0.19041.0.'
    }
    if ($text -notmatch '<TargetPlatformMinVersion>10\.0\.17763\.0</TargetPlatformMinVersion>') {
        throw 'App csproj TargetPlatformMinVersion must be 10.0.17763.0.'
    }
    if ($text -match 'Microsoft\.WindowsAppSDK["'']?\s*Version') {
        throw 'Do not pin the WASDK umbrella package.'
    }
    $cpm = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'Directory.Packages.props') -Raw -Encoding utf8
    if ($cpm -notmatch 'Microsoft\.WindowsAppSDK\.WinUI"\s+Version="2\.3\.6"') {
        throw 'WinUI pin must stay 2.3.6.'
    }
    if ($cpm -match '2\.4\.0') {
        throw 'Do not copy WASDK 2.4.0 display version into the package lock.'
    }
}

function Get-SourceManifestPath {
    $path = Join-Path $script:RepoRoot 'packaging\Package.appxmanifest'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing $path" }
    return $path
}

function Read-AppxIdentity([string]$manifestPath) {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Missing AppxManifest: $manifestPath"
    }
    [xml]$xml = Get-Content -LiteralPath $manifestPath -Encoding utf8
    $identity = $xml.Package.Identity
    if ($null -eq $identity) { throw "AppxManifest Identity missing: $manifestPath" }
    $props = $xml.Package.Properties
    return [ordered]@{
        name                     = [string]$identity.Name
        publisher                = [string]$identity.Publisher
        version                  = [string]$identity.Version
        processor_architecture   = [string]$identity.ProcessorArchitecture
        publisher_display_name   = if ($null -ne $props) { [string]$props.PublisherDisplayName } else { '' }
        display_name             = if ($null -ne $props) { [string]$props.DisplayName } else { '' }
    }
}

function Assert-LabIdentity($identity) {
    if ($identity.name -ne $script:LabName) {
        throw "Identity Name must be $($script:LabName); Store identity is rejected. Found: $($identity.name)"
    }
    if ($identity.publisher -ne $script:LabPublisher) {
        throw "Publisher must be lab identity '$($script:LabPublisher)'. Production/Store Publisher is rejected. Found: $($identity.publisher)"
    }
    if ($identity.processor_architecture -ne 'x64') {
        throw "ProcessorArchitecture must be x64 to match RID win-x64. Found: $($identity.processor_architecture)"
    }
    if ($identity.publisher_display_name -and $identity.publisher_display_name -ne $script:LabPublisherDisplay) {
        throw "PublisherDisplayName must be '$($script:LabPublisherDisplay)'."
    }
    $lower = ("$($identity.name) $($identity.publisher) $($identity.publisher_display_name)").ToLowerInvariant()
    foreach ($token in @('windows store', 'cn=microsoft', 'cn=contoso', 'storepublisher')) {
        if ($lower.Contains($token)) {
            throw "Forbidden identity token '$token'."
        }
    }
}

function Find-PrivateKeyFiles([string]$root) {
    $hits = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $root)) { return @() }
    if ((Get-Item -LiteralPath $root).PSIsContainer) {
        foreach ($glob in $script:PrivateKeyGlobs) {
            Get-ChildItem -LiteralPath $root -Recurse -File -Filter $glob -ErrorAction SilentlyContinue |
                ForEach-Object { $hits.Add($_.FullName) }
        }
        return @($hits)
    }
    $name = [IO.Path]::GetFileName($root)
    foreach ($glob in $script:PrivateKeyGlobs) {
        if ($name -like $glob) { $hits.Add($root) }
    }
    if ($name -like '*.msix' -or $name -like '*.appx') {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($root)
        try {
            foreach ($entry in $zip.Entries) {
                $entryName = $entry.FullName.Replace('/', '\')
                $leaf = [IO.Path]::GetFileName($entryName)
                foreach ($glob in $script:PrivateKeyGlobs) {
                    if ($leaf -like $glob) { $hits.Add($entry.FullName) }
                }
            }
        }
        finally { $zip.Dispose() }
    }
    return @($hits)
}

function Get-TreeSha256([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) {
        throw "Layout directory missing: $dir"
    }
    $rootFull = (Resolve-Path -LiteralPath $dir).Path.TrimEnd('\', '/')
    $files = @(Get-ChildItem -LiteralPath $rootFull -Recurse -File | Sort-Object FullName)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($file in $files) {
        $rel = $file.FullName.Substring($rootFull.Length).TrimStart('\', '/').Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        [void]$lines.Add("$rel=$hash")
    }
    $text = (($lines -join "`n") + "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-WindowsKitsBinRoots {
    $candidates = @(
        [Environment]::GetFolderPath('ProgramFilesX86'),
        [Environment]::GetFolderPath('ProgramFiles'),
        ${env:ProgramFiles(x86)},
        ${env:ProgramFiles},
        ${env:ProgramW6432}
    )
    $roots = New-Object System.Collections.Generic.List[string]
    foreach ($pf in $candidates) {
        if ([string]::IsNullOrWhiteSpace($pf)) { continue }
        $kits = Join-Path $pf 'Windows Kits\10\bin'
        if (Test-Path -LiteralPath $kits) { [void]$roots.Add($kits) }
    }
    return @($roots | Select-Object -Unique)
}

function Find-SdkTool([string]$exeName) {
    $commandName = [IO.Path]::GetFileNameWithoutExtension($exeName)
    $cmd = Get-Command $commandName -ErrorAction SilentlyContinue
    if ($cmd -and -not [string]::IsNullOrWhiteSpace([string]$cmd.Source)) {
        return [string]$cmd.Source
    }
    foreach ($kits in @(Get-WindowsKitsBinRoots)) {
        $found = Get-ChildItem -LiteralPath $kits -Recurse -Filter $exeName -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'x64' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($found) { return [string]$found.FullName }
    }
    return $null
}

function Find-MakeAppx {
    return Find-SdkTool -exeName 'makeappx.exe'
}

function Find-SignTool {
    return Find-SdkTool -exeName 'signtool.exe'
}

function Copy-PackagingOverlay([string]$layout) {
    New-Item -ItemType Directory -Force -Path $layout | Out-Null
    $destManifest = Join-Path $layout 'AppxManifest.xml'
    Copy-Item -LiteralPath (Get-SourceManifestPath) -Destination $destManifest -Force
    $assetsSrc = Join-Path $script:RepoRoot 'packaging\Assets'
    $assetsDst = Join-Path $layout 'Assets'
    if (Test-Path -LiteralPath $assetsSrc -PathType Container) {
        New-Item -ItemType Directory -Force -Path $assetsDst | Out-Null
        Copy-Item -Path (Join-Path $assetsSrc '*') -Destination $assetsDst -Force
    }
}

function Invoke-PublishLayout([string]$layout) {
    Assert-AppCsprojMethod
    if (-not $IsWindows) {
        throw 'Build publish requires Windows. Use -Action Verify on a fixture from this script on other OS.'
    }
    $dotnet = Get-Command dotnet -ErrorAction Stop
    $csproj = Join-Path $script:RepoRoot 'src\HerdDesk.App\HerdDesk.App.csproj'
    New-Item -ItemType Directory -Force -Path $layout | Out-Null
    $publishArgs = @(
        'publish', $csproj,
        '--configuration', 'Release',
        '--framework', $script:TargetFramework,
        '--runtime', $script:RuntimeIdentifier,
        '--output', $layout,
        '-p:HerdDeskBclOnly=false',
        '-p:WindowsPackageType=None',
        '-p:EnableMsixTooling=false'
    )
    if ($Restore) {
        $publishArgs += '-p:RestoreLockedMode=true'
    }
    else {
        $publishArgs += '--no-restore'
    }
    Invoke-External -FilePath $dotnet.Source -ArgumentList $publishArgs
    Copy-PackagingOverlay -layout $layout
}

function Get-LayoutManifestPath([string]$layout) {
    $direct = Join-Path $layout 'AppxManifest.xml'
    if (Test-Path -LiteralPath $direct -PathType Leaf) { return $direct }
    $alt = Join-Path $layout 'Package.appxmanifest'
    if (Test-Path -LiteralPath $alt -PathType Leaf) { return $alt }
    throw "Layout is missing AppxManifest.xml: $layout"
}

function Invoke-VerifyLayout {
    param(
        [string]$layout,
        [System.Collections.IDictionary]$report,
        [string]$MsixPath = ''
    )
    $manifest = Get-LayoutManifestPath -layout $layout
    $identity = Read-AppxIdentity -manifestPath $manifest
    $report['identity_name'] = $identity.name
    $report['publisher'] = $identity.publisher
    $report['version'] = $identity.version
    $report['processor_architecture'] = $identity.processor_architecture
    Assert-LabIdentity -identity $identity
    $keys = @(Find-PrivateKeyFiles -root $layout)
    $resolvedMsix = $MsixPath
    if ([string]::IsNullOrWhiteSpace($resolvedMsix)) { $resolvedMsix = $PackagePath }
    if ($resolvedMsix -and (Test-Path -LiteralPath $resolvedMsix -PathType Leaf)) {
        $keys += @(Find-PrivateKeyFiles -root $resolvedMsix)
        $report['package_path'] = $resolvedMsix
        $report['msix_sha256'] = (Get-FileHash -LiteralPath $resolvedMsix -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    if ($keys.Count -gt 0) {
        $report['private_key_found'] = $true
        throw 'Private key material is not allowed inside the package or layout.'
    }
    $report['private_key_found'] = $false
    $report['layout_sha256'] = Get-TreeSha256 -dir $layout
    $report['ok'] = $true
    $report['error'] = $null
}

function Write-SignatureSkipped([string]$reason) {
    Write-Host "Signature is skipped. $reason Unsigned local layout is not a release install."
}

function Invoke-ActionBuild([string]$outputRoot, [string]$layout) {
    $report = New-BaseReport -actionName 'Build' -outputRoot $outputRoot -layout $layout
    try {
        Invoke-PublishLayout -layout $layout
        $makeAppx = Find-MakeAppx
        $msix = Join-Path $outputRoot 'HerdDesk.Lab.msix'
        if ($makeAppx) {
            New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
            if (Test-Path -LiteralPath $msix) { Remove-Item -LiteralPath $msix -Force }
            Invoke-External -FilePath $makeAppx -ArgumentList @('pack', '/d', $layout, '/p', $msix, '/o')
            $report['package_path'] = $msix
        }
        else {
            Write-Host 'MakeAppx not found. Layout is the unsigned local artifact.'
            $msix = ''
        }
        Invoke-VerifyLayout -layout $layout -report $report -MsixPath $msix
        $report['signature'] = 'skipped'
        $report['signed'] = $false
        Write-SignatureSkipped 'Build does not sign. Pass -Action Sign -CertificatePath <file> after Build.'
        Write-ReportObject $report
        if (-not $report['ok']) { exit 1 }
        exit 0
    }
    catch {
        $report['ok'] = $false
        $report['error'] = [string]$_.Exception.Message
        Write-ReportObject $report
        exit 1
    }
}

function Invoke-ActionVerify([string]$outputRoot, [string]$layout) {
    $report = New-BaseReport -actionName 'Verify' -outputRoot $outputRoot -layout $layout
    try {
        if (-not (Test-Path -LiteralPath $layout -PathType Container)) {
            throw "Layout directory missing: $layout"
        }
        Invoke-VerifyLayout -layout $layout -report $report
        $report['signature'] = 'skipped'
        $report['signed'] = $false
        Write-SignatureSkipped 'Verify does not sign.'
        Write-ReportObject $report
        if (-not $report['ok']) { exit 1 }
        exit 0
    }
    catch {
        $report['ok'] = $false
        $report['error'] = [string]$_.Exception.Message
        Write-ReportObject $report
        exit 1
    }
}

function Invoke-ActionSign([string]$outputRoot, [string]$layout) {
    $report = New-BaseReport -actionName 'Sign' -outputRoot $outputRoot -layout $layout
    try {
        if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
            $report['signature'] = 'skipped'
            $report['error'] = 'Signature is skipped: -CertificatePath is required for -Action Sign.'
            $report['ok'] = $false
            Write-Host $report['error']
            Write-ReportObject $report
            exit 1
        }
        if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
            throw "CertificatePath not found: $CertificatePath"
        }
        $certFull = (Resolve-Path -LiteralPath $CertificatePath).Path
        if (Test-PathUnder -child $certFull -parent $layout) {
            throw 'CertificatePath must not live inside the package layout.'
        }
        if (Test-PathUnder -child $certFull -parent $outputRoot) {
            throw 'CertificatePath must not live under the packaging output root.'
        }
        $packagingDir = Join-Path $script:RepoRoot 'packaging'
        if (Test-PathUnder -child $certFull -parent $packagingDir) {
            throw 'CertificatePath must not live under packaging/.'
        }
        $fixturesDir = Join-Path $script:RepoRoot 'tests\fixtures'
        if (Test-PathUnder -child $certFull -parent $fixturesDir) {
            throw 'CertificatePath must not live under tests/fixtures.'
        }
        $msix = $PackagePath
        if ([string]::IsNullOrWhiteSpace($msix)) {
            $candidate = Join-Path $outputRoot 'HerdDesk.Lab.msix'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $msix = $candidate }
        }
        if ([string]::IsNullOrWhiteSpace($msix) -or -not (Test-Path -LiteralPath $msix -PathType Leaf)) {
            throw 'Sign requires an MSIX at -PackagePath or artifacts/packaging/HerdDesk.Lab.msix. Layout-only Build is unsigned and not a release install.'
        }
        $signTool = Find-SignTool
        if (-not $signTool) { throw 'signtool.exe not found. Sign failed closed.' }
        $msixFull = (Resolve-Path -LiteralPath $msix).Path
        # Empty-password lab PFX from new_lab_certificate.ps1. /p with empty
        # string fails closed without prompting. Not a production timestamp.
        Invoke-External -FilePath $signTool -ArgumentList @(
            'sign', '/fd', 'SHA256', '/f', $certFull, '/p', [string]::Empty, $msixFull
        )
        $report['package_path'] = $msixFull
        $report['signed'] = $true
        $report['signature'] = 'lab_cert'
        $report['signed_msix_built'] = $false
        $report['publisher_identity_confirmed'] = $false
        $report['is_release_install'] = $false
        $report['unsigned_local_build_is_release'] = $false
        if (Test-Path -LiteralPath $layout -PathType Container) {
            Invoke-VerifyLayout -layout $layout -report $report
        }
        $report['msix_sha256'] = (Get-FileHash -LiteralPath $msixFull -Algorithm SHA256).Hash.ToLowerInvariant()
        $report['ok'] = $true
        Write-Host 'Lab signature applied. This is not a production Publisher and not AC41/AC42.'
        Write-ReportObject $report
        exit 0
    }
    catch {
        $report['ok'] = $false
        $report['signed'] = $false
        $report['signed_msix_built'] = $false
        $report['is_release_install'] = $false
        $report['publisher_identity_confirmed'] = $false
        $report['ac41_passed'] = $false
        $report['ac42_passed'] = $false
        $report['g0_passed'] = $false
        $report['error'] = [string]$_.Exception.Message
        Write-ReportObject $report
        exit 1
    }
}

$outputRoot = Resolve-OutputRoot -value $OutputRoot
$layout = Resolve-LayoutPath -outputRoot $outputRoot -value $LayoutPath
if ([string]::IsNullOrWhiteSpace($PackagePath) -eq $false -and
    -not [System.IO.Path]::IsPathRooted($PackagePath)) {
    $PackagePath = Join-Path $script:RepoRoot $PackagePath
}

switch ($Action) {
    'Build' { Invoke-ActionBuild -outputRoot $outputRoot -layout $layout }
    'Verify' { Invoke-ActionVerify -outputRoot $outputRoot -layout $layout }
    'Sign' { Invoke-ActionSign -outputRoot $outputRoot -layout $layout }
    default { throw "Unsupported -Action $Action" }
}
