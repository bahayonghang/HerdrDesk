#requires -Version 7.0
<# Read-only wrapper. Requires Python 3.10+ and an existing herdr installation.
   Does not install software, start/stop daemons, take over, or send terminal input.
   This wrapper has not been executed in a Windows/PowerShell environment here. #>
[CmdletBinding()]
param(
    [string]$Python = 'python',
    [string]$Herdr = 'herdr',
    [string]$Session,
    [Parameter(Mandatory = $true)][string]$Output,
    [switch]$IncludeDiagnostics,
    [switch]$Overwrite
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'probe_herdr.py'
if (-not (Test-Path -LiteralPath $script -PathType Leaf)) { throw 'Missing probe_herdr.py.' }
$arguments = @($script, 'preflight', '--herdr', $Herdr, '--output', $Output)
if ($Session) { $arguments += @('--session', $Session) }
if ($IncludeDiagnostics) { $arguments += '--include-diagnostics' }
if ($Overwrite) { $arguments += '--overwrite' }
& $Python @arguments
if ($LASTEXITCODE -ne 0) { throw "Read-only probe did not pass (exit $LASTEXITCODE). Inspect the report." }
