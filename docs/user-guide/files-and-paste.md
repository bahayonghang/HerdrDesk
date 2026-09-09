# Files and paste (L1)

Dual-pane file ViewModels, `TransferCoordinator`, `AttachmentCoordinator`, `PasteCoordinator`, and `OscClipboardPolicy` exist as L1 code. Live FS, SSH, TOCTOU, agent attach, and clipboard IME stay UNVERIFIED. AC30–AC36 stay `not_run`.

## Policy that is already in code

- File listing does not parse `ls` text.
- Upload after attach does not auto-submit Enter to the agent.
- Path paste is not the same as agent attachment accept.
- OSC 52 clipboard read is denied.
- High-risk paste is previewed by `PastePreviewViewModel`; that is not a live IME desktop.

## What this page is not

This page does not invent a file-manager window or screenshot. Fake FS and mock process cannot pass live TOCTOU. Missing grant: `no_authorized_independent_user_walkthrough`.
