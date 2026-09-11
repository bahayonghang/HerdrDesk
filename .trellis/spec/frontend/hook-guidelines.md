# Hook Guidelines

**N/A.** HerdDesk has no React hooks.

Shared stateful UI logic is C# coordinators and ViewModels:

- `src/HerdDesk.App/Services/PaneVisibilityCoordinator.cs`
- `src/HerdDesk.App/Controls/TerminalFocusCoordinator.cs`
- `src/HerdDesk.App/ViewModels/TerminalDisplayCoordinator.cs`
- `src/HerdDesk.App/ViewModels/NavigationCoordinator.cs`

Do not add `use*` hooks, React Query, or SWR.

If a later approved task introduces a web UI beyond the xterm host page, that is a new design. The current `web/terminal/` bundle is a local renderer, not a React app.
