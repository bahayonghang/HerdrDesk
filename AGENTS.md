# HerdDesk shared project facts

This file is the canonical tracked copy of shared working rules, gate, and authorization for Claude Code, Codex, Grok Build, Kimi Code, and OMP (can1357/oh-my-pi).

Module indexes live in [CLAUDE.md](CLAUDE.md) and each nested `CLAUDE.md`. Tool dispatch, permission checks, and CLI fallback live in [docs/harness-workflows.md](docs/harness-workflows.md). Coding rules for current G0 C# and Python live in [.trellis/spec/backend/](.trellis/spec/backend/index.md). Frontend WinUI/React templates are deferred; see [.trellis/spec/frontend/index.md](.trellis/spec/frontend/index.md).

Do not put durable project facts only inside the Trellis managed block below. A `trellis update` may overwrite that block.

## Working rules

These rules apply at the repository root and when the working directory is `src/HerdDesk.Core` or any nested path. Walk up to this file. Then read the `CLAUDE.md` in the module you edit.

1. The product phase is **G0**. `implementation/status.json` records `phase_gate=not_passed`. Do not mark G0 or AC01–AC48 as passed.
2. The offline gate is `just ci`. A green gate is not live herdr, SSH, WinUI, IME, or takeover evidence.
3. Do not run live herdr writes, SSH sessions, WinUI product UI, takeover, input replay, or `python scripts/publish_github.py --publish`.
4. Implement product or spec changes only after the user approves the plan for that task. Planning text is not authorization to expand G0.

Default observe. herdr owns the agent and PTY. HerdDesk owns the connection. Control, takeover, input, and upload need an explicit grant. After disconnect, do not replay input. Closing the GUI must release only this application's child processes.

## Architecture

GitHub repository name: `HerdrDesk`. Application, solution, and C# namespace: `HerdDesk`. Chinese work name: 牧台.

**Current G0 in `src/`:** `HerdDesk.Contracts` (BCL-only types), `HerdDesk.Core` (one-epoch frame parser, input policy, HD-016 L1 ControlLeaseCoordinator, HD-017 L1 ResourceCommandCoordinator, and HD-018 L1 RecoveryPolicy), `HerdDesk.Infrastructure` (config store, diagnostics, owned process, RPC stdio), `HerdDesk.Terminal.Web` (HD-014 L1 message validator and BCL renderer adapter plus HD-015 L1 IME/keyboard/selection coordinators; WebView2/xterm packages unverified), and `HerdDesk.App` (composition-root host stub plus HD-011 L1 ViewModels, HD-015 L1 focus/input ViewModels, HD-016 L1 control ViewModel, HD-017 L1 resource command ViewModel, and HD-018 L1 RecoveryBindings, not WinUI). HD-019 ships an L1 scenario catalog plus composition tests over those coordinators; live local MVP is UNVERIFIED. Rust `bridge/herddesk-bridge` is an L1 byte relay. Python diagnostics live under `scripts/herddesk_g0` and `scripts/probe_herdr.py`. Tests live under `tests/`.

**Not in the tree:** `HerdDesk.Terminal.Native`, `herddesk-filebridge`, WinUI `App.xaml` content. Do not add those modules unless a later approved task asks for them. Windows App SDK / test-framework packages are pending and not in the product lock. L2 named-pipe ACL remains UNVERIFIED. HD-011 L2 visual/activation and L3 IME/DPI remain UNVERIFIED. HD-014 L2 WebView process and L3 DPI remain UNVERIFIED. HD-015 L3 real IME desktop remains UNVERIFIED. HD-016 L2 live lease remains UNVERIFIED. HD-017 L2 live mutation remains UNVERIFIED. HD-018 L2 live disconnect remains UNVERIFIED. HD-019 L2 live local MVP and L3 IME/agent TUI remain UNVERIFIED.

Dependency direction: Contracts depends on BCL only. Core depends on Contracts only. Core must not reference WinUI, WebView2, SSH, or OS credentials. The draft `docs/plan/contracts/HerdDesk.Contracts.cs` must not overwrite `src/HerdDesk.Contracts`.

Keep two communication planes separate. JSON RPC (snapshot / event subscribe) uses an API socket or `herddesk-bridge`. Terminal frames use `herdr terminal session` stdio. Do not send JSON RPC to the herdr binary client socket.

Identity names: `DeviceId`, `SessionKey`, `PaneKey`, `ConnectionEpoch`, `TerminalAccess`, `ControlVerified`. Pane id, window title, and agent type are not global keys. Compare terminal `seq` only inside the current connection epoch. The full name table is in [CLAUDE.md](CLAUDE.md).

## Commands and SDK

Offline gate (same steps as `.github/workflows/ci.yml`):

```powershell
just ci
```

Equivalent steps: Python unittest under `tests/python`, `python scripts/probe_herdr.py selftest`, `python scripts/check_capture.py tests/fixtures/terminal-valid.ndjson`, `python scripts/validate_repository.py`, `dotnet build HerdDesk.slnx --configuration Release`, `dotnet format HerdDesk.slnx --verify-no-changes --no-restore`, C# smoke plus `tests/Unit` and `tests/Contract` console runners, and `python scripts/run_windows_desktop_gate.py` on Windows (skip is not full-application green).

`just setup` is read-only. The pin is `global.json` (`10.0.400`, `rollForward=disable`). Default setup does not call winget and does not write User environment. Opt-in only:

```powershell
pwsh -NoLogo -File scripts/Invoke-HerdDeskDotnetSetup.ps1 -InstallPinnedSdk
pwsh -NoLogo -File scripts/Invoke-HerdDeskDotnetSetup.ps1 -PersistUserEnvironment
```

Writable probes need a disposable target. Input also needs `--allow-input`. Stop only the probe's own direct child processes.

## Status authority

| Kind | Tracked source |
|---|---|
| Product backlog and AC | `planning/`, `tasks/HD-*.md`, `planning/acceptance.json` |
| Last recorded check JSON | `implementation/status.json` (counts may lag later commits; run `just ci`) |
| Planning archive | `docs/plan/` (not compiled `src` contracts) |
| Trellis workflow | `.trellis/workflow.md` and this file |

Do not treat a hosted Actions pass on an older SHA as proof for the current HEAD. Do not treat `PUBLICATION_MANIFEST.json` as the daily gate.

## Local adapters

`.agents/`, `.codex/`, `.grok/`, `.kimi-code/`, `.omp/`, and `.claude/` are gitignored generated adapters. A fresh clone does not ship them. Do not blanket-unignore those directories. Do not copy private agent settings, credentials, or session memory into git. Do not edit user-global skill sources for this task.

If a harness CLI is missing, follow this file, [CLAUDE.md](CLAUDE.md), [docs/harness-workflows.md](docs/harness-workflows.md), and the active task artifacts. Do not invent a session-load result.

<!-- TRELLIS:START -->
# Trellis Instructions

These instructions are for AI assistants working in this project.

This project is managed by Trellis. The working knowledge you need lives under `.trellis/`:

- `.trellis/workflow.md` — development phases, when to create tasks, skill routing
- `.trellis/spec/` — package- and layer-scoped coding guidelines (read before writing code in a given layer)
- `.trellis/workspace/` — per-developer journals and session traces
- `.trellis/tasks/` — active and archived tasks (PRDs, research, jsonl context)

If a Trellis command is available on your platform (e.g., `/trellis:finish-work`, `/trellis:continue`), prefer it over manual steps. Not every platform exposes every command.

If you're using Codex or another agent-capable tool, additional project-scoped helpers may live in:
- `.agents/skills/` — reusable Trellis skills
- `.codex/agents/` — optional custom subagents

Managed by Trellis. Edits outside this block are preserved; edits inside may be overwritten by a future `trellis update`.

<!-- TRELLIS:END -->
