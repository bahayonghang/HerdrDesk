# Design: explicit herdr observe orchestration

The composition root selects one of two adapter sets: unavailable (no grant, invalid or unsupported configuration) or explicitly authorized local observe. The authorized path resolves only explicit profile endpoint/path data, constructs Core `DeviceSession` with the existing decoder/store/binding, and consumes its state stream into the App projection catalog.

RPC state uses `RpcStdioConnectionFactory`/`herddesk-bridge` on the API plane. A selected pane uses `TerminalCliProcessFactory` and `TerminalCliArgumentList` on the terminal stdio plane. These lifetimes are independent. On successful terminal open, register the returned transport/owned child with `AppExitCoordinator`; never register the herdr daemon itself.

Renderer and input remain behind existing `TerminalHostSession`/lease policy. A process-alive signal, first frame, focus, or renderer Ready cannot set `ControlVerified`. On disconnect or stale epoch, close only this app's transport and return projection state to a truthful unavailable/stale state.

Compatibility work updates the old composition contract tests and adds fake-process tests. No Core reference to WinUI/WebView2/SSH is introduced and no live evidence is claimed.
