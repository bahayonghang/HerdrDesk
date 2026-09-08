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

[group('dotnet')]
format-check: build
    {{dotnet}} format {{solution}} --verify-no-changes --no-restore --include src/HerdDesk.App --include src/HerdDesk.Infrastructure --include src/HerdDesk.Terminal.Web --include src/HerdDesk.Contracts/ConfigurationModels.cs --include src/HerdDesk.Contracts/SshConnectionContracts.cs --include src/HerdDesk.Contracts/DiagnosticModels.cs --include src/HerdDesk.Contracts/HostModels.cs --include src/HerdDesk.Contracts/RendererModels.cs --include src/HerdDesk.Contracts/ResourceOperationPorts.cs --include src/HerdDesk.Contracts/Rpc --include src/HerdDesk.Contracts/State --include src/HerdDesk.Contracts/Terminal --include src/HerdDesk.Core/Store --include src/HerdDesk.Core/DeviceSessions --include src/HerdDesk.Core/Attention --include src/HerdDesk.Core/Commands --include src/HerdDesk.Core/Recovery --include src/HerdDesk.Core/RenderFlowController.cs --include src/HerdDesk.Core/WebMessagePolicy.cs --include tests/Unit --include tests/Contract --include tests/HerdDesk.TestSupport

[group('dotnet')]
unit-tests: build
    {{dotnet}} run --project tests/Unit/HerdDesk.Core.Tests --configuration {{configuration}} --no-build
    {{dotnet}} run --project tests/Unit/HerdDesk.Infrastructure.Tests --configuration {{configuration}} --no-build
    {{dotnet}} run --project tests/Unit/HerdDesk.App.Tests --configuration {{configuration}} --no-build
    {{dotnet}} run --project tests/Unit/HerdDesk.Terminal.Web.Tests --configuration {{configuration}} --no-build

[group('dotnet')]
contract-tests: build
    {{dotnet}} run --project tests/Contract/HerdDesk.ContractTests.csproj --configuration {{configuration}} --no-build

[windows]
[group('ci')]
desktop:
    {{python}} scripts/run_windows_desktop_gate.py

[unix]
[group('ci')]
desktop:
    {{python}} -c "print('windows_desktop_restore skipped on this platform; not full-application green')"

[group('rust')]
bridge-fmt:
    cargo fmt --manifest-path bridge/Cargo.toml --all -- --check

[group('rust')]
bridge-clippy:
    cargo clippy --manifest-path bridge/Cargo.toml --workspace --all-targets --locked -- -D warnings

[group('rust')]
bridge-test:
    cargo test --manifest-path bridge/Cargo.toml --workspace --locked

# Offline gate used by GitHub Actions. G0 BCL/Python/Rust on every OS.
# Windows desktop restore runs only the skip/admit helper; it is not live WinUI.
# Cargo recipes belong to HD-008. npm/xterm and WebView2 stay not admitted.
[group('ci')]
ci: test-python selftest capture structure build format-check smoke unit-tests contract-tests desktop bridge-fmt bridge-clippy bridge-test

alias check := ci
