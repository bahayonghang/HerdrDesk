# CLI probe (2026-09-08)

Host: Windows. Purpose: record whether the five harness CLIs are on PATH, and their `--version` strings. This is not a five-session load test. Do not copy user-global settings, credentials, skill lists, MCP configs, or permission dumps into git.

## PATH and version

| Command | Path (this machine) | `--version` |
|---|---|---|
| `claude` | present | 2.1.263 (Claude Code) |
| `codex` | present | codex-cli 0.153.4 |
| `grok` | present | grok 1.0.22 |
| `kimi` | present | 0.41.0 |
| `omp` | present | omp/18.1.12 |
| `trellis` | present (not one of the five) | 0.7.0-beta.3 (`@mindfoldhq/trellis`) |

`oh-my-pi` as a binary name was missing. OMP is the `omp` command (can1357/oh-my-pi). `pi` 0.85.1 is a different CLI on PATH; this task does not treat `pi` as OMP.

npm dist-tags for `@mindfoldhq/trellis` at probe time: `latest=0.6.16`, `beta=0.7.0-beta.3`.

## Session load

| Tool | Result |
|---|---|
| Claude Code | `UNVERIFIED` (no new session) |
| Codex | `UNVERIFIED` (no new session) |
| Grok Build | `grok inspect` configuration discovery only. Coding-session load `UNVERIFIED`. |
| Kimi Code | `UNVERIFIED` (no new session) |
| OMP | `UNVERIFIED` (no new session) |

`grok inspect` from the repository root listed tracked project instructions `AGENTS.md` and `CLAUDE.md`. From `src/HerdDesk.Core` it also listed `src/CLAUDE.md` and `src/HerdDesk.Core/CLAUDE.md`. Inspect also listed user-global rules and plugins; those paths are not project facts and are not recorded here.

Presence of gitignored `.claude/` / `.grok/` on this machine is not proof that a fresh clone loads the same adapters.

## Commands used

```powershell
Get-Command claude, codex, grok, kimi, omp, trellis
claude --version
codex --version
grok --version
kimi --version
omp --version
trellis --version
npm view @mindfoldhq/trellis dist-tags --json
grok inspect   # repo root, then cwd src\HerdDesk.Core
```
