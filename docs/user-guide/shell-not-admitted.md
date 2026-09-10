# Shell not product-accepted

HD-011 ships four-zone WinUI Shell XAML bound to L1 ViewModels. L2 visual/activation and L3 IME/Narrator/DPI stay UNVERIFIED. Product AC19 is not passed. `just ci` does not launch a WinUI window.

This page does not invent screenshots or an install wizard.

## Install

No signed MSIX, App Installer channel, or production Publisher identity is admitted. `packaging/` ships a lab-identity unsigned layout overlay. Lab identity is not a Store identity and is not a release Publisher. `App.xaml` exists. HD-034 records live install/sign/update/rollback as `not_run`. Do not copy an unsigned local `dotnet` output and call it a release install.

`just setup` is a developer SDK check. It is not an end-user installer.

## First connection

L1 code can resolve an explicit endpoint and named session on fake ports. It must not guess `%APPDATA%` or a conventional pipe name. Named session must not fall back to default. UNC is rejected.

Live Windows named-pipe ACL, live herdr connect, and daemon-unreachable desktop diagnosis stay UNVERIFIED. AC03 stays `not_run`.

## Chrome that would need UI

Search palette, device tree, settings, and diagnostics bind those ViewModels in WinUI XAML. Keyboard, Narrator, and DPI evidence stay UNVERIFIED (HD-033). Do not write walkthrough steps that treat a local window as AC19 or AC37/AC38.

Independent-user verification of this page is not authorized. Missing grant: `no_authorized_independent_user_walkthrough`.
