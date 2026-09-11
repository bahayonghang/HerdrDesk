# Quality Guidelines

Offline gate and honesty rules for WinUI/ViewModel work.

---

## Overview

`just ci` is the offline gate. It is not live herdr, SSH, WinUI, IME, Narrator, DPI-matrix, or soak evidence. `just dev` starts `--ui` into gitignored `probe-results/dev-ui` and is not part of CI.

---

## Forbidden Patterns

- Starting `--ui`, Narrator, DisplayConfig SET, theme/high-contrast writes, or soak `--record` from CI/`just ci`.
- Claiming AC08/AC09/AC10/AC19/AC37/AC38 from XAML, AutomationProperties, or overlay JSON.
- Adding React/Next/Vue from Trellis templates.
- Core referencing WinUI, WebView2, SSH, or OS credentials.

---

## Required Patterns

- Windows TFM PackageReference stays `Microsoft.WindowsAppSDK.WinUI` 2.3.6 via CPM + `src/HerdDesk.App/packages.lock.json`.
- New Shell chrome needs `AutomationProperties.Name`.
- Tests: App unit runner is BCL (`tests/Unit/HerdDesk.App.Tests`). `tests/Integration.Windows` is a net10.0 console runner and does not launch a window.

---

## Testing Requirements

- `dotnet run --project tests/Unit/HerdDesk.App.Tests --configuration Release --no-build`
- `dotnet run --project tests/Integration.Windows --configuration Release --no-build`
- HD-033 overlays: `collect_narrator_overlay.py`, `collect_dpi_overlay.py`, `collect_theme_overlay.py` record current system samples. They do not pass AC37/AC38.

---

## Code Review Checklist

- [ ] Identity names are the Contracts table, not titles.
- [ ] Focus/Ready/first frame did not set `ControlVerified`.
- [ ] No live window was required for the offline gate.
- [ ] G0 and product ACs stay not passed.
