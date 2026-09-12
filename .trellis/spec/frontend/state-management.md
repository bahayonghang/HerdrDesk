# State Management

ViewModels over Core Store projections. Not Redux, Zustand, or Vuex.

---

## Overview

UI state is in-process. There is no server-state library. herdr snapshots travel JSON RPC on the API/bridge plane; terminal frames stay on `herdr terminal session` stdio.

---

## State Categories

| Kind | Where | Notes |
|------|-------|-------|
| Identity / projection | `HerdDesk.Core` Store | `DeviceId`, `SessionKey`, `PaneKey`, `ConnectionEpoch` |
| Shell chrome | `ShellViewModel`, `ShellChrome`, `NavigationCoordinator` | Selection does not grant control; chrome uses Chinese `ShellStrings` |
| Preferences | `Settings/UiPreferenceStore.cs` | Not user AppData unless an explicit root is passed |
| Input / lease | `TerminalInputViewModel`, `TerminalControlViewModel` | `RequestControl` does not grant a lease |
| Notifications | `NotificationCenterViewModel` | Clicks locate `PaneKey` only |
| Visibility / capacity | `Services/PaneVisibilityCoordinator.cs` | Hidden panes release terminal/renderer |
| File / paste / attach | `Files/*ViewModel`, `AttachToAgentViewModel`, `PastePreviewViewModel` | No XAML yet |
| Exit | `Lifetime/AppExitCoordinator.cs` | Kill ledger is owned children only |

---

## Rules

- Compare terminal `seq` only inside the current `ConnectionEpoch`.
- After disconnect, do not replay input.
- Closing the GUI releases only this application's child processes (`AppExitCoordinator`).
- `WindowsNotificationSink.Available` stays false until a later approved toast task.
- `RequestControl` on `TerminalControlViewModel` does not set `ControlVerified`.
- `ShellHost` may attach an in-memory `TerminalControlViewModel` via `WorkbenchControlFactory` so the control bar renders without RPC. That factory is not live observe. `AppServices.CreateProduction` stays on `UnavailableAdapter`.
- `SettingsViewModel.RequestConnect` only appends `PendingConnects`. Do not treat it as a started session.
