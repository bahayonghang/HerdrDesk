# Harness workflows

Tracked adapter notes for five tools: Claude Code, Codex, Grok Build, Kimi Code, and OMP (can1357/oh-my-pi). This file is the durable tracked source for the five-tool matrix, dispatch, permission checks, and CLI fallback. Do not depend on a live parent-task research path; those directories move under `.trellis/tasks/archive/` when the parent is archived. This file does not copy private agent settings, credentials, or session memory.

Shared project facts live only in [AGENTS.md](../AGENTS.md). [CLAUDE.md](../CLAUDE.md) is the module index and cites that file. A fresh clone can run a manual workflow from those tracked files plus `.trellis/` task artifacts. Gitignored `.agents/`, `.codex/`, `.grok/`, `.kimi-code/`, `.omp/`, and `.claude/` are local generated adapters, not a distribution source.

This document does not claim runtime alignment of the five CLIs. CLI presence is not session-load proof.

## Manual workflow (no local adapter dirs)

1. Read root [AGENTS.md](../AGENTS.md) (walk up from `src/HerdDesk.Core` if that is the working directory).
2. Read [CLAUDE.md](../CLAUDE.md), then the nested `CLAUDE.md` for the module you edit.
3. Read the active task `prd.md`, `design.md` if present, `implement.md` if present, and every `file` in `implement.jsonl` / `check.jsonl`.
4. Follow `.trellis/spec/backend/` for G0 C#/Python and `.trellis/spec/frontend/` for WinUI/XAML/ViewModels. Do not implement leftover Trellis React/Next/Vue templates.
5. Run `just ci` for the offline gate. Do not run live herdr, SSH, WinUI, takeover, or `--publish`.
6. Implement only after the user approves the plan for that task.

If a CLI is missing, stop at this list. Record `UNVERIFIED` for session load. Do not invent loaded files or the resolved model.

## Five tools

OMP in this project means can1357/oh-my-pi (`omp`), not a different product named Pi.

| Tool | Entry file (tracked) | Dispatch | How to check actual model and permissions | Fallback if the CLI is missing |
|---|---|---|---|---|
| Claude Code | [CLAUDE.md](../CLAUDE.md) cites [AGENTS.md](../AGENTS.md). Nested `CLAUDE.md` files are module indexes. | Main session may spawn `trellis-implement` / `trellis-check` / `trellis-research` as sub-agent types. Prompt must start with `Active task: <path>`. Do not spawn those types from inside an implement/check sub-agent. | `claude --version`. Session: `--model`, `--permission-mode`, `--agent`. Do not treat a role name as the billed model. Fresh session load: `UNVERIFIED` unless a new session records the loaded files. | Read the tracked files in "Manual workflow". Do not require `.claude/`. |
| Codex | [AGENTS.md](../AGENTS.md). Codex does not auto-load a sibling `CLAUDE.md` as a substitute. | Default: native `SubagentStart` may inject context when trusted; child-side pull is the fallback. Inline mode (`codex.dispatch_mode=inline`) edits in the main session. Prompt must start with `Active task: <path>`. | `codex --version`. Session: `--model` / `-m`, `--sandbox` / `-s`, `--ask-for-approval` / `-a`. Project `.codex/config.toml` is gitignored; do not claim hooks work because that file exists on one machine. Fresh session load: `UNVERIFIED`. | Same manual workflow. Do not require `.codex/` or `.agents/skills/`. |
| Grok Build | [AGENTS.md](../AGENTS.md) and [CLAUDE.md](../CLAUDE.md). Nested `CLAUDE.md` also loads when the cwd is under `src/`. Gitignored rule files are skipped. | `spawn_subagent` with `subagent_type` set to the Trellis name (`trellis-implement`, `trellis-check`, `trellis-research`). Built-in `explore` / `plan` have no shell; do not send `just ci` to those built-ins. Custom check agents are not automatically read-only. | `grok --version`. `grok inspect` lists discovered project instructions, permission mode sources, and agents for the cwd. `grok models`, `--model`, `--permission-mode`, `--sandbox`. Official sandbox docs describe Linux Landlock / macOS Seatbelt and default off; that is not proof of Windows OS isolation. | Same manual workflow. `grok inspect` is optional discovery, not a substitute for [AGENTS.md](../AGENTS.md). |
| Kimi Code | [AGENTS.md](../AGENTS.md) plus [CLAUDE.md](../CLAUDE.md) as the module index. Official Kimi supports custom agents, skills, and hooks. | There is no Claude-style sub-agent type named `trellis-implement`. Dispatch built-in `coder` / `explore` (plan has no shell). Pass `Active task: <path>` and the file list in the prompt. Do not copy Claude `model` frontmatter onto a Kimi agent and expect a model lock; extra fields are ignored. Local `.kimi-code/skills/` are gitignored generated adapters. | `kimi --version`. Session: `--model`, `--agent` / `--agent-file`, `--yolo`, `--auto`. Record the resolved model from the session, not the role name. Fresh session load: `UNVERIFIED`. | Same manual workflow. Do not assume project-level Kimi agents exist on a fresh clone. Built-in `coder` plus explicit task context is the tracked fallback. |
| OMP (oh-my-pi) | [AGENTS.md](../AGENTS.md). | Model roles include plan / slow / smol. `task` launches sub-agents. Advisor is a review role. Role names do not prove lower price or read-only tools. Plan/no-shell style roles must not run `just ci`. Builds write `bin/` and `obj/`; source-only review is not a no-write sandbox. | `omp --version`. Session: `--model`, `--smol`, `--slow`, `--plan`, `omp models`. Official Agent Hub documents observed model, status, and usage. Fresh session load: `UNVERIFIED`. | Same manual workflow. Do not require `.omp/`. |

Cheap-model work (after a strong model has fixed the contract): entry links, module-index text, fixture transcription, running a named test and returning the exit code. Strong models keep: protocol fail-latch, control/epoch/SSH authorization, failure root cause, AC mapping, and final acceptance. On execution failure or a cross-layer behavior change, return to a strong model. Do not let an execution model widen tests or the approved file list.

This table is an engineering split. It is not a quality ranking. This repository has no measured cost or latency dataset for the five tools.

Official vendor docs used for the capability claims (2026-09-08). They are not a five-CLI runtime certification:

- [Claude Code subagents](https://code.claude.com/docs/en/sub-agents)
- [Codex AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md)
- [Codex subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents)
- [Codex skills](https://learn.chatgpt.com/docs/build-skills)
- [Grok rules](https://docs.x.ai/build/features/project-rules)
- [Grok subagents](https://docs.x.ai/build/features/subagents)
- [Grok sandbox](https://docs.x.ai/build/features/sandbox)
- [Kimi agents](https://moonshotai.github.io/kimi-code/en/customization/agents)
- [Kimi skills](https://moonshotai.github.io/kimi-code/en/customization/skills.html)
- [Kimi hooks](https://moonshotai.github.io/kimi-code/en/customization/hooks.html)
- [OMP repository](https://github.com/can1357/oh-my-pi)
- [OMP Agent Hub](https://github.com/can1357/oh-my-pi/blob/main/docs/agent-hub.md)

## Native injection versus child-side pull

| Mechanism | Where it applies | What it does | What it does not do |
|---|---|---|---|
| Trellis UserPromptSubmit / SessionStart hook | Platforms that install `inject-workflow-state.py` or the OpenCode plugin, when that hook is present, trusted, and enabled | May inject a `<workflow-state>` breadcrumb from `.trellis/workflow.md` | Does not run on every supported AI product. Grok Build class-2 agents must pull. Missing, untrusted, or disabled hooks mean no breadcrumb. |
| Codex `SubagentStart` | Codex, when trusted | May inline `implement.jsonl` / `check.jsonl` files plus `prd.md` / `design.md` / `implement.md` | Untrusted or unavailable injection falls back to child-side pull. A local `.codex/config.toml` is not proof. |
| Child-side pull | Grok Build, Kimi Code, and any agent whose prompt says it is already `trellis-implement` / `trellis-check` | The agent reads `task.py current --source` or the `Active task:` path, then jsonl, then artifacts | Does not auto-happen because a gitignored skill file exists on one developer machine. |
| Built-in plan / explore | Grok and Kimi built-ins; OMP plan role | Read-only planning if the tool truly has no shell | Not a `trellis-check` substitute. Writable check/implement work needs a shell-capable agent. |

`.trellis/workflow.md` Phase 2.1 text that says a platform hook "auto-handles" injection is conditional. If the hook is missing, untrusted, disabled, or gitignored, the spawned agent must load jsonl and artifacts itself.

## Trellis template origin and upstream-sync handoff

| Field | Value |
|---|---|
| Project file | `.trellis/.version` |
| Installed CLI (this machine, 2026-09-08) | `@mindfoldhq/trellis@0.7.0-beta.3` (`npm` dist-tag `beta`) |
| `npm view @mindfoldhq/trellis` dist-tags at probe | `latest=0.6.16`, `beta=0.7.0-beta.3`, `rc=0.6.0-rc.0` |
| Template hash index | `.trellis/.template-hashes.json` (`__version` 2), includes `.trellis/workflow.md` |

This task edited `.trellis/workflow.md` in the repository. The edits qualify three overstatements:

- Breadcrumb comment (~line 104): not every platform has a UserPromptSubmit hook.
- Dispatch versus skill names (~lines 223 and 226): Kimi uses built-in `coder` / `explore` with explicit task context; `trellis-implement` is not a Skill name on Claude/Codex/Grok.
- Phase 2.1 hook paragraph (~line 483): injection is conditional.

`trellis update` may restore the packaged template and drop these qualifications. The upstream Trellis template repository was **not** patched in this task. Do not claim the upstream template repo was synced. Handoff: keep the qualifications in this file; re-apply them after a future `trellis update` if the template still overstates hooks or Kimi skills.

Generated adapters under gitignored tool directories must be regenerated from Trellis sources after an upstream fix. This task does not authorize edits to user-global skill trees.

## CLI probe (2026-09-08)

The table below is the durable record. This is not a five-session load test. `oh-my-pi` was not a PATH binary on the probe host; OMP is `omp`. A different `pi` CLI may be on PATH; this project does not treat `pi` as OMP.

| CLI | On PATH | Version reported | Session load |
|---|---|---|---|
| `claude` | yes | 2.1.263 | `UNVERIFIED` |
| `codex` | yes | 0.153.4 | `UNVERIFIED` |
| `grok` | yes | 1.0.22 | `grok inspect` discovery only; coding-session load `UNVERIFIED` |
| `kimi` | yes | 0.41.0 | `UNVERIFIED` |
| `omp` | yes | 18.1.12 | `UNVERIFIED` |

`grok inspect` from the repo root listed tracked project instructions `AGENTS.md` and `CLAUDE.md`. From `src/HerdDesk.Core` the same command also listed `src/CLAUDE.md` and `src/HerdDesk.Core/CLAUDE.md`. That is configuration discovery, not a five-harness runtime-alignment claim. Do not commit user-global rule paths or permission dumps.
