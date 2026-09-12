# Implement plan (parent)

Do not run `task.py start` on this parent. After the user approves this summary, start **child** `workbench-chrome` first.

## Order

1. **workbench-chrome** — Shell chrome, Chinese status, control bar, Settings connect intent, empty four-zone skeleton. No live adapters. Validation: App unit tests + `dotnet format` on touched files; `just ci` before claiming the child done. `just dev` is optional operator preview, not CI, and still must not pretend online.
2. **tab-mosaic** — `WorkbenchLayout` + tab strip + N `TerminalHost` slots. Synthetic layouts in `AppTestHost`. Depends on chrome's WorkbenchHost. No live herdr.
3. **observe-opt-in** — blocked until the user explicitly grants live herdr observe. Then wire factories behind an opt-in that default `--ui` does not enable. CI still must not launch herdr or `--ui`.

Parent archive waits until chrome + mosaic are done **or** the user stops after chrome. observe-opt-in may remain planning.

## Validation (each child)

```powershell
dotnet run --project tests/Unit/HerdDesk.App.Tests --configuration Release
dotnet format HerdDesk.slnx --verify-no-changes --no-restore
just ci
```

Do not start Narrator, soak, or live SSH. Do not mark product ACs passed.

## Risky files

- `src/HerdDesk.App/Views/ShellPage.xaml(.cs)` — layout regressions, IME overlay, focus.
- `src/HerdDesk.App/Composition/AppServices.cs` / `ShellHost.cs` — fake-success / always-unavailable mistakes.
- `src/HerdDesk.App/Services/PaneVisibilityCoordinator.cs` — accidental BeginObserve side effects without host.
- `src/HerdDesk.App/ViewModels/ShellViewModel.cs` — enum dump title; ControlVerified leaks.
- `tests/Unit/HerdDesk.App.Tests/AppTestHost.cs` — SessionState signature; keep empty tabs/layouts valid.

## Before any `task.py start`

- User approved the latest planning summary in chat (not merely “A” or “create a task”).
- Child `prd.md` / `design.md` / `implement.md` exist for the child being started.
- That child's `implement.jsonl` and `check.jsonl` have real spec/research entries.
- No live herdr in chrome or mosaic.
