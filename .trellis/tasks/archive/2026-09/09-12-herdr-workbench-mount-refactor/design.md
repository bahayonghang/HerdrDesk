# Design: 四区工作台与显式 herdr 连接编排

## Boundaries

```text
MainWindow / ShellPage
  ├─ DeviceSessionRail (Device / Session)
  ├─ WorkspacePaneTree (Workspace only)
  ├─ WorkbenchHost (tabs + ControlBar + <=4 TerminalHost slots)
  └─ DetailsHost (collapsed/visible by an explicit breakpoint policy)

Settings RequestConnect
  → App connection orchestrator
  → EndpointResolver + explicit DeviceProfile path/endpoint
  → Core DeviceSession(IRpcConnectionFactory, decoder, store)
  → ProjectionCatalog / GlobalProjectionStore
  → selected PaneKey + ConnectionEpoch
  → TerminalCliProcessFactory (`herdr terminal session observe`)
  → TerminalHost renderer/frame pump
```

The App composition root owns orchestration and adapter lifetime. Core remains BCL-only and owns epoch/state semantics. `TerminalHost` consumes a terminal transport; it must not open RPC or infer daemon state from a process alive signal.

## Layout design

- Keep the existing four-zone Shell and `WorkbenchLayout` as separate responsibilities.
- Add visible panel boundaries/minimum geometry at `ShellPage` level, preserve the central `*` column, and make the details collapse/expand policy explicit for narrow (<800), normal, and wide (>=1200) widths.
- Keep empty rails and a docked central banner in `NoDevices`/`DaemonUnavailable`; a pane projection replaces the banner with tab/mosaic content.
- Keep Zone2 filtered to workspaces. Use the existing `LayoutProjection` normalized rectangles and `PaneVisibilityCoordinator` budget of four; waiting slots are labeled tiles without WebView instances.

## Connection design

- Production defaults remain unavailable until the user grants observe. The grant selects a real adapter set; it does not launch the daemon.
- Build `DeviceSession` with existing `IRpcConnectionFactory`, `IRpcStateDecoder`, `DeviceProjectionStore`, `SchemaCompatibilityBinding`, diagnostics and time provider. Consume `ReadStatesAsync` into the App catalog/aggregate.
- Resolve only explicit endpoint/path data from the profile. Do not guess `%APPDATA%`, conventional pipe names, or infer a socket from a pane id.
- Create `RpcStdioConnectionFactory` for the API/bridge plane and `TerminalCliProcessFactory` for terminal stdio. The two factories must remain independently disposable and diagnosable.
- A selected pane opens observe mode only. Control remains behind existing lease/coordinator verification; no first-frame/process/focus shortcut is allowed.

## Compatibility and rollback

- Update current contract tests that assert permanent `UnavailableAdapter` behavior to assert adapter selection by authorization and fail-closed behavior when configuration is missing.
- Keep an unavailable adapter path for no grant, invalid endpoint, or unsupported renderer; this is a state, not a fake successful projection.
- If projection or renderer wiring fails, hide/close only this pane's terminal/bridge children, return the catalog to unavailable/stale, and leave herdr daemon/agent untouched.
- No new dependency, package, Core reference to WinUI/WebView2/SSH, or live evidence claim is allowed.
