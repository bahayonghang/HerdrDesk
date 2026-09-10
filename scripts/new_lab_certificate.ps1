#requires -Version 7.0
<# HD-034 L2 lab certificate overlay. Not a release install.

   Creates a CurrentUser code-signing cert with subject exactly
   CN=HerdDesk Lab (not release). Exports PFX to a caller-supplied
   path (default gitignored artifacts/certs/). Prints one JSON object.

   Never writes PFX into packaging/, tests/fixtures, a package layout,
   or the git tree. Never uses the machine store. Lab identity is not a
   Store or production Publisher. This overlay is not AC41/AC42. #>
[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$CertificatePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:LabPublisher = 'CN=HerdDesk Lab (not release)'
$script:CurrentUserStore = 'Cert:\CurrentUser\My'

function Get-DefaultCertificatePath {
    return (Join-Path $script:RepoRoot 'artifacts\certs\HerdDesk.Lab.pfx')
}

function Test-PathPrefix([string]$child, [string]$parent) {
    if ([string]::IsNullOrWhiteSpace($child) -or [string]::IsNullOrWhiteSpace($parent)) {
        return $false
    }
    $childFull = [IO.Path]::GetFullPath($child)
    $parentFull = [IO.Path]::GetFullPath($parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($childFull.Equals([IO.Path]::GetFullPath($parent), [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    $childCmp = $childFull.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    return $childCmp.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)
}

function New-CertReport {
    param(
        [string]$PathValue,
        [bool]$Ok,
        [bool]$Written,
        $ErrorText
    )
    return [ordered]@{
        schema_version                 = 1
        document_kind                  = 'hd034_lab_certificate'
        ok                             = $Ok
        subject                        = $script:LabPublisher
        lab_identity_not_release       = $true
        lab_identity_not_store         = $true
        is_release_install             = $false
        signed_msix_built              = $false
        publisher_identity_confirmed   = $false
        ac41_passed                    = $false
        ac42_passed                    = $false
        g0_passed                      = $false
        pfx_written                    = $Written
        pfx_in_git                     = $false
        certificate_path               = $PathValue
        error                          = $ErrorText
    }
}

function Write-CertReport {
    param(
        [System.Collections.IDictionary]$Report,
        [int]$Code
    )
    $json = [pscustomobject]$Report | ConvertTo-Json -Depth 8
    Write-Output $json
    exit $Code
}

function Get-ForbiddenPfxReason([string]$fullPath) {
    $packaging = Join-Path $script:RepoRoot 'packaging'
    if (Test-PathPrefix -child $fullPath -parent $packaging) {
        return 'CertificatePath must not live under packaging/.'
    }
    $fixtures = Join-Path $script:RepoRoot 'tests\fixtures'
    if (Test-PathPrefix -child $fullPath -parent $fixtures) {
        return 'CertificatePath must not live under tests/fixtures.'
    }
    $layout = Join-Path $script:RepoRoot 'artifacts\packaging\layout'
    if (Test-PathPrefix -child $fullPath -parent $layout) {
        return 'CertificatePath must not live inside the package layout.'
    }
    $outputRoot = Join-Path $script:RepoRoot 'artifacts\packaging'
    if (Test-PathPrefix -child $fullPath -parent $outputRoot) {
        return 'CertificatePath must not live under the packaging output root.'
    }
    $repoPrefix = $script:RepoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $artifactsPrefix = (Join-Path $script:RepoRoot 'artifacts').TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -and
        -not $fullPath.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        return 'CertificatePath must not be written into the git tree.'
    }
    return $null
}

try {
    $resolved = $CertificatePath
    if ([string]::IsNullOrWhiteSpace($resolved)) {
        $resolved = Get-DefaultCertificatePath
    }
    elseif (-not [IO.Path]::IsPathRooted($resolved)) {
        $resolved = Join-Path $script:RepoRoot $resolved
    }
    $resolved = [IO.Path]::GetFullPath($resolved)
    $forbidden = Get-ForbiddenPfxReason -fullPath $resolved
    if ($forbidden) {
        Write-CertReport -Report (New-CertReport -PathValue $resolved -Ok $false -Written $false -ErrorText $forbidden) -Code 1
    }
    if (-not $IsWindows) {
        Write-CertReport -Report (New-CertReport -PathValue $resolved -Ok $false -Written $false -ErrorText 'New-SelfSignedCertificate requires Windows. Lab certificate overlay is not AC41.') -Code 1
    }
    $dir = Split-Path -Parent $resolved
    if (-not [string]::IsNullOrWhiteSpace($dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $script:LabPublisher -CertStoreLocation $script:CurrentUserStore -HashAlgorithm SHA256
    try {
        $password = [Security.SecureString]::new()
        Export-PfxCertificate -Cert $cert -FilePath $resolved -Password $password | Out-Null
    }
    finally {
        Remove-Item -LiteralPath ("{0}\{1}" -f $script:CurrentUserStore, $cert.Thumbprint) -Force -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw 'PFX export did not write CertificatePath.'
    }
    Write-CertReport -Report (New-CertReport -PathValue $resolved -Ok $true -Written $true -ErrorText $null) -Code 0
}
catch {
    $pathValue = if ($resolved) { $resolved } else { $CertificatePath }
    $written = $false
    if (-not [string]::IsNullOrWhiteSpace($pathValue) -and (Test-Path -LiteralPath $pathValue -PathType Leaf)) {
        $written = $true
    }
    Write-CertReport -Report (New-CertReport -PathValue $pathValue -Ok $false -Written $written -ErrorText ([string]$_.Exception.Message)) -Code 1
}
