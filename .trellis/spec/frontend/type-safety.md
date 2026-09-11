# Type Safety

C# nullable reference types and `HerdDesk.Contracts` identity. Not TypeScript.

---

## Overview

App, Core, and Contracts compile with nullable enabled and `TreatWarningsAsErrors`. There is no Zod, io-ts, or frontend `types/` folder.

---

## Type Organization

- Identity and frames: `src/HerdDesk.Contracts` (BCL only).
- UI ViewModels may consume Contracts and Core types. They must not redefine `PaneKey` as a window title or pane id.
- WebView messages are validated in `HerdDesk.Terminal.Web` (`WebMessageValidator`). Unknown web types fail closed.
- `docs/plan/contracts/HerdDesk.Contracts.cs` is a draft archive. Do not overwrite `src/HerdDesk.Contracts`.

---

## Rules

- Do not use `dynamic` to pass terminal payload or RPC bodies.
- Do not treat Python `bool` as a JSON integer on the wire.
- Uint8Array-equivalent bytes stay raw; do not UTF-8-decode terminal payload in the UI.
- Keep `DeviceId` / `SessionKey` / `PaneKey` / `ConnectionEpoch` as the Contracts types. Pane id, window title, and agent type are not global keys.
