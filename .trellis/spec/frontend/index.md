# Frontend Development Guidelines

G0 product UI is WinUI 3 on the admitted App windows TFM, not React. These files describe the XAML/ViewModel surface in `src/HerdDesk.App`. Do not implement features from leftover Trellis React/Next/Vue templates.

Shared facts: [AGENTS.md](../../../AGENTS.md). Module index: [src/HerdDesk.App/CLAUDE.md](../../../src/HerdDesk.App/CLAUDE.md). Renderer baseline: `docs/spikes/renderer-decision.md` (WebView2/xterm; native `UNVERIFIED`).

**Language**: English.

---

## Overview

- Four-zone Shell: `App.xaml` / `MainWindow` / `Views/ShellPage.xaml` on `Microsoft.WindowsAppSDK.WinUI` 2.3.6.
- Terminal surface: `Controls/TerminalHost.xaml` (WinUI WebView2) plus `web/terminal/` (`@xterm/xterm` 6.0.0).
- ViewModels and coordinators live in the App project and bind to Core Store projections. Many ViewModels still have no XAML page (Devices, Files, paste, attach).
- `just ci` does not launch a WinUI window. `--ui <temp-root>` is opt-in (`just dev` on Windows).
- L2 visual/activation, L2 WebView process, and L3 IME/Narrator/DPI stay `UNVERIFIED`. Shipped `AutomationProperties.Name` values are not AC37.

---

## Guidelines Index

| Guide | Description | Status |
|-------|-------------|--------|
| [Directory Structure](./directory-structure.md) | App WinUI/ViewModel layout | Filled from G0 App tree |
| [Component Guidelines](./component-guidelines.md) | XAML pages/controls, automation names | Filled from G0 App tree |
| [Hook Guidelines](./hook-guidelines.md) | No React hooks; coordinators instead | N/A |
| [State Management](./state-management.md) | ViewModels + Core Store | Filled from G0 App tree |
| [Type Safety](./type-safety.md) | C# nullable + Contracts identity | Filled from G0 App tree |
| [Quality Guidelines](./quality-guidelines.md) | Offline gate, no live WinUI in CI | Filled from G0 App tree |

---

## Pre-Development Checklist

- [ ] Read [AGENTS.md](../../../AGENTS.md). Phase is G0. Do not launch live herdr/SSH/WinUI writes unless the user grants them.
- [ ] Keep UI identity as `DeviceId` / `SessionKey` / `PaneKey` / `ConnectionEpoch`. Pane id, window title, and agent type are not global keys.
- [ ] Selection, focus, first frame, and process-alive must not set `ControlVerified`.
- [ ] Do not PackageReference the WASDK 2.4.0 umbrella. Admitted lock is WinUI 2.3.6 on the windows TFM only.
- [ ] Do not add React, Next.js, Vue, or CSS from Trellis templates.

---

## Quality Check

- [ ] No React/TypeScript UI was added from these templates.
- [ ] CI did not start `--ui` or Narrator.
- [ ] New XAML has `AutomationProperties.Name`; that is not a Narrator/AC37 pass.
- [ ] Do not mark G0 or AC01–AC48 passed.
