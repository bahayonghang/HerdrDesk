# Directory Structure

Where G0 backend code lives. There are no route or controller folders.

---

## Overview

C# product code is two class libraries under `src/`. Python protocol and probes live under `scripts/`. Tests are not nested inside `src/`.

---

## Directory Layout

```
src/HerdDesk.Contracts/     BCL types only (TerminalModels.cs, EndpointModels.cs, LeaseModels.cs, RendererModels.cs)
src/HerdDesk.Core/          TerminalFrameParser.cs, InputPolicy.cs, EndpointResolver.cs, TerminalLeaseProbe.cs, renderer L1 specimens
tests/HerdDesk.Core.SmokeTests/  console smoke runner (dotnet run)
tests/python/               stdlib unittest (sys.path → scripts/)
tests/fixtures/             synthetic NDJSON, protocol-edge-cases.json, endpoint-cases.json, lease-cases.json, renderer-cases.json; real-terminal-v082/ placeholder
docs/licensing/             register.json and candidate admission templates
docs/adr/                   0001-g0-bootstrap.md, approved-baseline.md, approved-baseline.json
scripts/herddesk_g0/        protocol.py, evidence.py, licensing.py, endpoint.py, lease.py, renderer.py, adr.py
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
| Endpoint mapping types | `src/HerdDesk.Contracts/EndpointModels.cs` |
| Terminal lease observation types | `src/HerdDesk.Contracts/LeaseModels.cs` |
| Renderer L1 host types | `src/HerdDesk.Contracts/RendererModels.cs` |
| Frame parse, input grant, endpoint mapping, lease mapping, renderer L1 | `src/HerdDesk.Core` |
| Python wire checks | `scripts/herddesk_g0/protocol.py` |
| Compatibility evidence rules | `scripts/herddesk_g0/evidence.py` |
| Endpoint matrix diagnostics | `scripts/herddesk_g0/endpoint.py` |
| Terminal lease diagnostics | `scripts/herddesk_g0/lease.py` |
| Renderer L1 diagnostics | `scripts/herddesk_g0/renderer.py` |
| Licensing register rules | `scripts/herddesk_g0/licensing.py` |
| Licensing inventory and templates | `docs/licensing/` |
| Approved G0 ADR freeze | `scripts/herddesk_g0/adr.py` |
| ADR freeze documents | `docs/adr/` |
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

- Contracts types: `src/HerdDesk.Contracts/TerminalModels.cs`, `src/HerdDesk.Contracts/EndpointModels.cs`, `src/HerdDesk.Contracts/LeaseModels.cs`, `src/HerdDesk.Contracts/RendererModels.cs`
- Fail-closed parser: `src/HerdDesk.Core/TerminalFrameParser.cs`
- Input grant: `src/HerdDesk.Core/InputPolicy.cs`
- Endpoint mapping: `src/HerdDesk.Core/EndpointResolver.cs`
- Lease mapping: `src/HerdDesk.Core/TerminalLeaseProbe.cs`
- Renderer L1: `src/HerdDesk.Core/Utf8ChunkAssembler.cs`, `RendererEpochGate.cs`, `CompositionPolicy.cs`, `RendererByteWindow.cs` (FIFO, oldest-frame ack), `WebMessagePolicy.cs`
- Python latching capture: `scripts/herddesk_g0/protocol.py` (`TerminalCaptureValidator`)
- Licensing register rules: `scripts/herddesk_g0/licensing.py`
- ADR freeze rules: `scripts/herddesk_g0/adr.py`
