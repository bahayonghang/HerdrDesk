# Directory Structure

Where G0 backend code lives. There are no route or controller folders.

---

## Overview

C# product code is two class libraries under `src/`. Python protocol and probes live under `scripts/`. Tests are not nested inside `src/`.

---

## Directory Layout

```
src/HerdDesk.Contracts/     BCL types only (TerminalModels.cs)
src/HerdDesk.Core/          TerminalFrameParser.cs, InputPolicy.cs
tests/HerdDesk.Core.SmokeTests/  console smoke runner (dotnet run)
tests/python/               stdlib unittest (sys.path → scripts/)
tests/fixtures/             synthetic NDJSON and protocol-edge-cases.json
scripts/herddesk_g0/        protocol.py (strict JSON, frames, NDJSON)
scripts/probe_herdr.py      herdr probe
scripts/Invoke-HerdDeskDotnetSetup.ps1
scripts/validate_repository.py
scripts/check_capture.py
HerdDesk.slnx
Directory.Build.props       net10.0, nullable, TreatWarningsAsErrors
global.json                 SDK 10.0.400, rollForward=disable
NuGet.Config                empty package sources
```

Planned and **not** present: `src/HerdDesk.App`, `src/HerdDesk.Infrastructure`, `src/HerdDesk.Terminal.*`, `bridge/`, `herddesk-filebridge`.

---

## Module Organization

| New work | Location |
|---|---|
| Identity / envelope types | `src/HerdDesk.Contracts/TerminalModels.cs` unless an approved task adds a file |
| Frame parse and input grant | `src/HerdDesk.Core` |
| Python wire checks | `scripts/herddesk_g0/protocol.py` |
| Shared synthetic records | `tests/fixtures/` (LF, UTF-8) |
| C# assertions | `tests/HerdDesk.Core.SmokeTests/Program.cs` |
| Python assertions | `tests/python/test_*.py` |

Do not place domain policy in a probe script. Do not place WinUI or SSH types in Core.

---

## Naming Conventions

- C# namespaces match project names: `HerdDesk.Contracts`, `HerdDesk.Core`.
- Python package directory is `scripts/herddesk_g0`. Tests import via `sys.path`; there is no `pyproject.toml`.
- Protocol error strings are stable snake_case codes (`malformed_terminal_record`, `terminal_stream_not_active`).
- Fixture files keep LF even on Windows (`.gitattributes` `eol=lf`).

---

## Examples

- Contracts types: `src/HerdDesk.Contracts/TerminalModels.cs`
- Fail-closed parser: `src/HerdDesk.Core/TerminalFrameParser.cs`
- Input grant: `src/HerdDesk.Core/InputPolicy.cs`
- Python latching capture: `scripts/herddesk_g0/protocol.py` (`TerminalCaptureValidator`)
