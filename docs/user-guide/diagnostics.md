# Diagnostics (L1)

`DiagnosticsViewModel` and the JSONL diagnostic sink redact credentials, hosts, and private paths in L1 tests. That is not a live canary export. HD-035 records diagnostic canary as `not_run`.

## Policy that is already in code

- Offline, expired, and unknown results stay unknown. They are not rewritten as success.
- Restarting the user's herdr daemon is not a general repair step.
- Update and rollback of this application are independent of herdr. HD-034 live update/rollback stay `not_run`.
- Do not invent a Publisher, certificate, or signed-package hash.

## What this page is not

This page is not AC45. Independent-user diagnosis on a supported install is not authorized. Missing grant: `no_authorized_independent_user_walkthrough`.
