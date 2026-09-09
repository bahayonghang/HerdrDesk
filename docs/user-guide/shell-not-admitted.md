# Shell not admitted

The product WinUI window is not in the tree. `App.xaml` content is absent. `tests/Integration.Windows` is absent. Windows App SDK is not in the product lock.

This page does not invent button names, screenshots, or an install wizard.

## Install

No signed MSIX, App Installer channel, or Publisher identity is admitted. `packaging/` is absent. HD-034 records live install/sign/update/rollback as `not_run`. Do not copy an unsigned local `dotnet` output and call it a release install.

`just setup` is a developer SDK check. It is not an end-user installer.

## First connection

L1 code can resolve an explicit endpoint and named session on fake ports. It must not guess `%APPDATA%` or a conventional pipe name. Named session must not fall back to default. UNC is rejected.

Live Windows named-pipe ACL, live herdr connect, and daemon-unreachable desktop diagnosis stay UNVERIFIED. AC03 stays `not_run`.

## Chrome that would need UI

Search palette, device tree, settings, and diagnostics ViewModels exist as BCL objects. They are not a shipped window. Keyboard, Narrator, and DPI evidence stay UNVERIFIED (HD-033). Do not write walkthrough steps that click unbuilt chrome.

Independent-user verification of this page is not authorized. Missing grant: `no_authorized_independent_user_walkthrough`.
