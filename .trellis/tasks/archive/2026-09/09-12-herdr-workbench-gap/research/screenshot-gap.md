# Screenshot and code gap: HerdDesk vs herdr TUI

Captured 2026-09-12 from the operator's two screenshots plus the current `src/HerdDesk.App` tree. This is evidence for planning, not live herdr/WinUI write authorization.

## What the two windows actually are

| Window | Product | State in the screenshot |
|---|---|---|
| Left | HerdDesk (`just dev` → `dotnet run ... -- --ui probe-results/dev-ui`) | First-run empty shell. Title `没有设备`. Status `NoDevices · Offline · Unknown · Disconnected · unread:0`. Centered `添加设备` / `诊断`. |
| Right | Original herdr TUI (running inside herdr; the HerdDesk repo is open as a workspace) | Live multiplexer. Left `spaces` list, tab `1`, two split terminals (`git push` / `just dev`), hostname `DESKTOP-S16H74`, clock. |

They are not two skins of the same client. HerdDesk is an independent Windows client that is supposed to **connect** to herdr. The right window **is** herdr. Launching HerdDesk from a herdr pane does not embed that TUI.

Product constraint: `docs/plan/docs/02_产品定义与名称.md:9` says the core is not wrapping the full herdr TUI. Four-zone WinUI is the planned IA (`02:37-39`, `.trellis/tasks/09-08-windows-desktop-full/ui-blueprint.md`). Pixel-cloning herdrm/macOS is out of v1 (`02:17`). HD-011 PRD explicitly says do not copy herdrm layout.

## Why the left window cannot look like the right one today

Three independent blockers. Any one of them is enough to produce the empty dashboard.

### 1. No device in the `--ui` data root

`just dev` writes `probe-results/dev-ui`, not user AppData (`justfile:54-55`). `ShellViewModel.StartAsync` goes to `ShellLifecycle.NoDevices` when both settings and catalog have zero devices (`src/HerdDesk.App/ViewModels/ShellViewModel.cs:156-160`). The welcome panel is the default route (`ShellViewModel.cs:83`, `ShellPage.xaml.cs:137-139`).

### 2. Production composition never talks to herdr

`MainWindow.BindRoot` always uses `AppServices.CreateProduction` (`MainWindow.xaml.cs:43-48`). That factory registers `UnavailableAdapter` for RPC, terminal transport, and renderer (`AppServices.cs:74-76`) and also lists `WebRendererHost.Capability` as unavailable (`AppServices.cs:82-83`). `ShellHost.Create` hard-codes `DaemonAvailable = false` (`ShellHost.cs:11`). If a device existed, `StartAsync` would still land on `DaemonUnavailable` (`ShellViewModel.cs:162-166`).

This is intentional G0 observe-by-default: live herdr/SSH/WinUI writes stay unauthorized (`AGENTS.md`). It is also why `--ui` cannot populate spaces, tabs, or panes from the herdr session visible on the right.

### 3. Connect chrome is not wired

`SettingsViewModel.RequestConnect` exists (`SettingsViewModel.cs:264-269`) but `SettingsPage.xaml` / `SettingsPage.xaml.cs` have no Connect control. Save writes a local profile; it does not start RPC or `herdr terminal session`. Device editor, helper install, and connection-status ViewModels have no XAML (`.trellis/spec/frontend/component-guidelines.md`, `src/HerdDesk.App/CLAUDE.md`).

## Visual inventory: herdr TUI vs current Shell

Read from the two screenshots plus XAML, not from a live pixel audit.

| herdr TUI (right) | Current HerdDesk Shell (left / code) | Gap |
|---|---|---|
| Compact title: hostname, workspace name, tab index `1`, clock, battery | Standard WinUI caption `HerdDesk` plus three text lines (title, breadcrumb, enum dump) | No workbench chrome. Status is `Lifecycle · Connection · Agent · Access · unread:N` (`ShellPage.xaml.cs:161-172`), not a human status bar. |
| Left rail labeled `spaces` with named workspaces, `new` / `menu` / `agents` / `grouped` | `DeviceSessionRail` + `WorkspacePaneTree` as empty `ListView`s (`DeviceSessionRail.xaml`, `WorkspacePaneTree.xaml`) | Rails exist as columns (220+240) but have no items, no section labels, no New/Agents grouping, no unread/agent badges. Empty lists blend into the dark Mica background, so the screenshot looks like a blank dashboard. |
| Tab strip in the top center | None | Schema decodes `tab_id` / layouts (`RpcStateDecoder.cs:416-428`). Shell has no tab strip. |
| Split mosaic: two live terminals side by side filling the remaining window | Single `TerminalHost` WebView2, collapsed unless `ShellRoute.Pane` (`ShellPage.xaml:92-94`, `TerminalHost.xaml:6`) | Protocol already has `pane.split` / `layout.set_split_ratio` (`SchemaOperations.cs`) and `DecodedLayoutSplit` (`RpcStateDecoder.cs:441-449`). UI never renders a mosaic. Budget is 4 visible terminals (`ResourceBudgets.cs:67`, `PaneVisibilityCoordinator.MaxVisiblePanes`). |
| Terminals are the product surface (~90% of the window) | Center is a welcome `StackPanel` with two buttons (`ShellPage.xaml:78-91`) | Empty-state is a settings-app pattern, not a workbench skeleton. |
| Observe/control is implicit in the TUI focus model | `TerminalControlViewModel` has no ControlBar XAML (`CLAUDE.md` HD-016: no WinUI ControlBar) | Planned toolbar (观察/控制/释放) from `ui-blueprint.md` is missing. |
| Wallpaper / terminal-native density | Mica backdrop (`MainWindow.xaml.cs:81-89`) + large padding, default WinUI buttons | Independent brand is required; TUI wallpaper/assets must not be copied. Density and “terminal-first” still missing. |
| Details/files not a fourth empty column | Details collapsed; placeholder `文件区尚未启用` (`ShellPage.xaml:107`) | HD-029 ViewModels exist; no dual-pane file XAML. |

## XAML still missing (ViewModels exist)

From `src/HerdDesk.App/CLAUDE.md` and frontend spec. These do not appear in the screenshot because they have no page/control:

- Observe / Control / Release bar (HD-016)
- Create workspace / terminal / agent dialogs (HD-017)
- SSH EditDevice / host-key review (HD-020)
- Helper install (HD-021)
- Connection status with Cancel/Retry (HD-024)
- Dual-pane file workspace (HD-029)
- Attach-to-agent overlay (HD-030)
- Paste preview (HD-031)
- Notification list chrome (HD-012 sink `Available` is always false)

## What is already present (do not re-plan as greenfield)

- Four-zone Shell XAML: rail, tree, center host, details (`ShellPage.xaml:63-108`)
- Narrow overlay / details collapse (`ShellPage.xaml.cs:88-108`)
- Ctrl+K search overlay (`SearchPalette.xaml`)
- Settings / Diagnostics / About routes
- WebView2 + local `@xterm/xterm` 6.0.0 host (not bound in production composition)
- L1 coordinators for navigation, control lease, resources, recovery, multi-device, files, paste, attach
- Layout DTO decode in Infrastructure (unused by App UI)

## Interpretation for product planning

The operator's “差距太大” is two problems stacked:

1. **Connection/composition**: `--ui` is an offline chrome demo. It cannot show the live herdr session that is already on screen.
2. **Workbench language**: even after a future connect, the Shell is a sparse WinUI form (empty lists + welcome buttons + enum status), not a terminal-first multiplexer (spaces + tabs + split terminals).

Closing (1) without (2) would still look unlike herdr. Closing (2) without (1) would still be an empty mock. Visual-language choice (herdr-like density vs keep four-zone forms) is a user-owned product decision and is recorded as the blocking open question in `prd.md`.
