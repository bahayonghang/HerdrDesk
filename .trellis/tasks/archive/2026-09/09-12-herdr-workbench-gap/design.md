# Design: terminal-first four-zone workbench

Parent of chrome / mosaic / observe-opt-in. Product code stays unchanged until a child is started after plan approval.

## Architecture and boundaries

HerdDesk remains a WinUI client over Core projections. herdr owns panes and PTYs. This work only changes how `HerdDesk.App` presents `DeviceProjectionSnapshot` and how the production composition root *may* later attach adapters.

```text
MainWindow (Mica)
└ ShellPage
   ├ Title/status (中文 StatusPresentation; not enum dump)
   ├ Zone1 DeviceSessionRail     设备 / 会话
   ├ Zone2 WorkspacePaneTree    工作区 (spaces); panes not a peer flat list
   ├ Zone3 WorkbenchHost
   │   ├ TabStrip (TabProjection of selected workspace)
   │   ├ ControlBar (TerminalControlViewModel)
   │   ├ MosaicHost (N TerminalHost ≤ 4)
   │   └ Route overlay: Settings / Diagnostics / About / empty banner
   ├ Zone4 Details (default collapsed; 文件区尚未启用 until HD-029)
   └ SearchPalette overlay
```

Do not add React. Do not PackageReference WASDK 2.4.0. Core still has no WinUI/SSH/WebView2 reference.

## Data flow

1. `ProjectionCatalog.Snapshot` (tests inject; live path later writes from RPC snapshot mapper already in Core/Infrastructure).
2. `NavigationCoordinator` still builds Device → Session → Workspace → Pane identities. Shell chrome **filters** Zone2 to workspace nodes of the selected session. Pane rows stay in the coordinator for search/keyboard, but Zone2 template does not show them as a second equal ListView.
3. `WorkbenchLayout` (new App ViewModel, BCL-safe) reads:
   - selected `SessionKey` + `WorkspaceId`
   - `SessionProjection.Tabs` filtered by workspace
   - `Layouts` matching that tab
   - `PaneVisibilityCoordinator` for Visible / Hidden / WaitingForCapacity
4. `ShellPage` binds tab strip + mosaic slots. Each visible slot is a `TerminalHost` bound with `PaneKey` + epoch + `readOnly: true` until `ControlVerified`.
5. Control bar calls existing `TerminalControlViewModel`. Selection change already invalidates takeover handles.

JSON RPC and `herdr terminal session` stdio stay separate planes. Mosaic does not send JSON RPC on the terminal pipe.

## Contracts already present

| Need | Source |
|---|---|
| Tabs | `TabProjection` (`ProjectionModels.cs:20-28`) |
| Mosaic rects | `LayoutProjection` / `LayoutPaneProjection` (`ProjectionModels.cs:42-50`) |
| Splits on wire | `DecodedLayoutSplit` — **not** on `LayoutProjection`; do not require splits for MVP mosaic |
| Capacity | `ResourceBudgets.ProductGlobalTerminals = 4` |
| Observe host | `PaneVisibilityCoordinator.Show` → `IPaneVisibilityHost.BeginObserve` |
| Status copy | `ShellStrings` + `StatusPresentation` |
| Connect intent | `SettingsViewModel.PendingConnects` |

Extend `LayoutProjection` only if mosaic cannot be built from pane rects. Do not invent a second layout type in Core.

## Mosaic algorithm

For the selected tab:

1. If `zoomed`, one slot: `FocusedPaneId` (or selection) fills the host.
2. Else take `layout.Panes`. Compute bounding box of rects. Map each rect to a proportional `Grid` (star rows/cols from unique X/Y edges) or a `Canvas` with normalized width/height. Cell units are upstream cells, not CSS pixels.
3. Ask `Show(pane, focused)` in layout order. Rejected panes render as a labeled WaitingForCapacity tile, not a live WebView.
4. Missing layout: one slot for selected pane or workspace's focused pane; overlay uses `ShellStrings.TerminalPlaceholder` until renderer ready.
5. Tab switch: `Hide` panes that left the visible set; `Show` the new set. New epoch per Show; do not replay input.

`WorkbenchLayout` is unit-tested with synthetic `SessionProjection` that includes `Tabs` and `Layouts`. `AppTestHost.SessionState` should gain an overload that fills those lists; existing callers keeping empty tabs/layouts must still compile (single-pane fallback).

## Composition

Current production (`AppServices.CreateProduction` + `ShellHost.Create`):

- Keep Unavailable adapters and `DaemonAvailable = false` as the **default**.
- `ShellHost` should still construct `TerminalControlViewModel` / `ControlLeaseCoordinator` with in-memory coordinators so the control bar can render without a daemon (access = Disconnected / Observing from catalog).
- Do not bind `RpcStdioConnectionFactory` unless the observe-opt-in child is started **and** the user granted live herdr.

`IPaneVisibilityHost` in `--ui` without transport: `BeginObserve` is a no-op that does not set renderer Ready or `ControlVerified`. Catalog access stays Observing only after a real baseline (observe-opt-in). Chrome/mosaic tests set `RendererReadyDefault` only in App unit tests, never in CreateProduction.

## Settings connect

Child chrome: Settings grows a 「连接」button that calls `RequestConnect`. Status text explains adapter unavailable / 未授权观察. Do not start a process.

Child observe-opt-in (after grant): consume `PendingConnects`, resolve local herdr path from the saved `DeviceProfile`, open RPC on the API plane, map snapshot into `ProjectionCatalog`, set `DaemonAvailable` from a real ping/snapshot success. Failure → `DaemonUnavailable` with existing retry. Never `IsFakeSuccess`.

## Compatibility and migration

- `--ui` data root remains `probe-results/dev-ui` (gitignored). No user AppData unless an explicit root is passed.
- Existing Shell unit tests (`EmptyConfigShell`, `DaemonUnavailable`, `IndependentStatus`) stay green; they must assert Chinese chrome instead of enum dumps where those tests already read title/status.
- `ShellSurface.Pages` / `AutomationNames` gain tab strip, control bar, mosaic, connect.
- Narrow overlay behavior (`NarrowWidth = 800`) unchanged.

## Trade-offs

- **Two nav columns vs one herdr-like spaces list**: keep two columns (device/session vs workspaces) because HerdDesk is multi-device. herdr TUI has no DeviceId. Pane list moves into mosaic so the center can dominate.
- **Rect mosaic vs split tree**: rects are already on the projection; split DTOs would require a Core mapper change. MVP uses rects.
- **No demo catalog in `--ui`**: slower visual satisfaction, avoids fake-success (project invariant).

## Rollback

Revert child XAML/ViewModel commits independently. Default composition root stays Unavailable, so a mosaic-only regression cannot talk to herdr. Hide mosaic by routing a single `TerminalHost` if layout math fails.
