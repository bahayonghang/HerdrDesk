# Design: workbench chrome

See parent `.trellis/tasks/09-12-herdr-workbench-gap/design.md`.

## Changes

- `ShellViewModel.UpdateChrome`: Title/status from `ShellStrings` + `StatusPresentation`, never `enum.ToString()` for product chrome.
- `ShellHost.Create`: construct `ControlLeaseCoordinator` + `TerminalControlViewModel` in-memory. No RPC.
- `ShellPage`: keep four columns; replace Welcome-as-only-center with a docked empty banner inside Zone3; add `ControlBar` UserControl.
- `DeviceSessionRail` / `WorkspacePaneTree`: section header TextBlocks; empty template with next action.
- `SettingsPage`: Connect button → `RequestConnect`; bind `PendingConnects` / error text.
- Zone2 still bound to workspace (and session children) items; hide pane-kind rows in the tree template (panes remain in coordinator for search). If filtering in the view is fragile, filter in ShellPage.SetItems.

## Non-goals in this child

No `WorkbenchLayout`. Single `TerminalHost` may stay collapsed with placeholder text (`ShellStrings.TerminalPlaceholder`).
