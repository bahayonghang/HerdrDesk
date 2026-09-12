# Directory Structure

WinUI and ViewModel layout for `src/HerdDesk.App`. This is not a React `src/components` tree. `web/terminal/` is a local xterm host page, not a React app.

---

## Overview

The App project is the unique composition root. `net10.0` is the console host (`--compose-only`). Windows UI compiles only on `net10.0-windows10.0.19041.0`. BCL CI passes `-p:HerdDeskBclOnly=true` and skips XAML code-behind.

---

## Directory Layout

```
src/HerdDesk.App/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── Program.cs
├── Composition/          AppServices, ShellHost, WorkbenchControlFactory
├── Activation/           WinUI activation host, intents
├── Views/                ShellPage, Settings, Diagnostics, About
├── Controls/             TerminalHost, SearchPalette, ControlBar, rails/trees
├── ViewModels/           Shell, search, input, control, notifications
├── Devices/              EditDevice, HelperInstall, connection status
├── Files/                Dual-pane file workspace ViewModels (no XAML)
├── Recovery/             RecoveryBindings
├── Notifications/        INotificationSink; Windows toast UNVERIFIED
├── Services/             PaneVisibilityCoordinator
├── Settings/             UiPreferenceStore
├── Search/               RecentAccessStore
├── Lifetime/             AppExitCoordinator
├── Resources/            ShellStrings
├── Quality/              HD-033 collectors (gitignored probe-results)
└── web is not here       HD-014 dist lives in web/terminal/
```

---

## Module Organization

- New visible chrome: add a `Views/` page or `Controls/` UserControl plus a ViewModel. Bind; do not put protocol or SSH in code-behind.
- File, paste, attach, SSH edit, and helper-install surfaces are ViewModels first. Do not invent XAML for them unless an approved task asks.
- Terminal renderer assets: `web/terminal/` (npm lock) hosted by `Controls/TerminalHost.xaml`.
- Collectors write `probe-results/` (gitignored), not user AppData, unless the operator passes an explicit root.
- Coordinators that are not pages live next to their domain (`ViewModels/NavigationCoordinator.cs`, `Controls/TerminalFocusCoordinator.cs`, `Services/PaneVisibilityCoordinator.cs`).

---

## Naming Conventions

- XAML types use PascalCase (`ShellPage`, `TerminalHost`).
- Automation names in product UI are Chinese where the Shell already is (`设备与会话`, `搜索`, `终端`).
- Internal keys stay English (`PaneKey`, `SessionKey`).

---

## Examples

- Four-zone Shell: `src/HerdDesk.App/Views/ShellPage.xaml`
- Device rail: `src/HerdDesk.App/Controls/DeviceSessionRail.xaml`
- Control bar: `src/HerdDesk.App/Controls/ControlBar.xaml`
- Terminal host: `src/HerdDesk.App/Controls/TerminalHost.xaml`
- Search chrome: `src/HerdDesk.App/Controls/SearchPalette.xaml`
- Composition root: `src/HerdDesk.App/Composition/AppServices.cs`
- Shipped name catalog: `src/HerdDesk.App/Quality/AccessibilityNameCatalog.cs`
