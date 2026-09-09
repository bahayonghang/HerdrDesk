# Independent-user release checklist (later)

This file records steps for a later authorized independent user. **This L1 catalog is not AC45.** The walkthrough was not executed. `independent_user_walkthrough_executed=false`.

Do not fill results here from structure validation or from a Markdown link.

## Preconditions that are still missing

1. Admitted WinUI shell on a supported Windows 11 x64 client.
2. Signed package with recorded SHA-256. No fake Publisher.
3. Independent reviewer who did not implement the candidate SHA.
4. Live platform evidence for promised Windows 11 x64 client and Linux x64 remote. Do not copy one platform onto the other.
5. GitHub required-check on that HEAD. Older hosted SHA is not a substitute.
6. Clean-machine locked restore of the frozen SHA.

## Later steps (not run)

1. Install from the signed package as a standard user. Record identity, hash, and exit code.
2. Follow only `docs/user-guide/` against the admitted shell. Stop if a documented control does not exist.
3. Observe default. Explicit control. Release. No auto-submit after attach. OSC 52 remains denied.
4. Export diagnostics and confirm redaction. No canary credentials in the default log.
5. Record failures as failures. Do not rewrite `not_run` to `passed`.

Missing grant today: `no_authorized_independent_user_walkthrough`.
