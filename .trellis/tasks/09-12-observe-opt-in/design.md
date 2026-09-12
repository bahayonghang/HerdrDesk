# Design: observe-opt-in

Blocked on an explicit live-herdr observe grant. See parent design Composition section.

Default `CreateProduction` unchanged. Opt-in composes real `IRpcConnectionFactory` / terminal transport / WebView renderer only when the grant flag is present (CLI or explicit operator switch — exact flag named at implementation time, default off).

`DaemonAvailable` follows a real snapshot/ping result, never a compiled-in true. `BeginObserve` then uses the terminal stdio plane, not the RPC socket.

Kill ledger: existing `AppExitCoordinator` owned children only.

If bridge binary or pipe ACL is missing, surface existing unavailable codes; do not degrade into fake frames.
