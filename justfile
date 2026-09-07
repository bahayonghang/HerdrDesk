# Local G0 recipes. Same steps as .github/workflows/ci.yml.
# No live herdr, SSH, WinUI, takeover, or github publish.

set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]
set dotenv-load := false

python := "python"
dotnet := "dotnet"
configuration := env_var_or_default("CONFIGURATION", "Release")
solution := "HerdDesk.slnx"
smoke_project := "tests/HerdDesk.Core.SmokeTests"
capture_file := "tests/fixtures/terminal-valid.ndjson"

export DOTNET_NOLOGO := "1"
export DOTNET_CLI_TELEMETRY_OPTOUT := "1"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE := "1"

default:
    @just --list

# Default is read-only: Python 3.10+ and the SDK pinned in global.json.
# Does not install the SDK or write User environment.
# Child pwsh PATH changes stay in that process; confirm with just build.
# Opt-in: pwsh -NoLogo -File scripts/Invoke-HerdDeskDotnetSetup.ps1 -InstallPinnedSdk
#         pwsh -NoLogo -File scripts/Invoke-HerdDeskDotnetSetup.ps1 -PersistUserEnvironment
[windows]
[group('env')]
setup:
    pwsh -NoLogo -File scripts/Invoke-HerdDeskDotnetSetup.ps1

[unix]
[group('env')]
setup:
    {{python}} -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)"
    {{dotnet}} --version

[group('env')]
sdk:
    {{dotnet}} --version
    {{dotnet}} --info

[group('dotnet')]
build:
    {{dotnet}} build {{solution}} --configuration {{configuration}}

[group('dotnet')]
smoke: build
    {{dotnet}} run --project {{smoke_project}} --configuration {{configuration}} --no-build

[group('dotnet')]
clean:
    {{dotnet}} clean {{solution}} --configuration {{configuration}}

[group('python')]
test-python:
    {{python}} -m unittest discover -s tests/python -v

[group('python')]
selftest:
    {{python}} scripts/probe_herdr.py selftest

[group('python')]
capture:
    {{python}} scripts/check_capture.py {{capture_file}}

[group('python')]
structure:
    {{python}} scripts/validate_repository.py

# Full offline G0 gate used by GitHub Actions.
[group('ci')]
ci: test-python selftest capture structure smoke

alias check := ci
