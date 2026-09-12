# Implement: observe-opt-in

Do not start this child in the same turn as chrome/mosaic. Wait for live-observe grant.

1. Confirm grant in chat.
2. Add opt-in composition tests with non-production fakes that are not `IsFakeSuccess`.
3. Wire Settings Connect consumer.
4. Operator may run `just dev` only if grant includes `--ui` observe.
5. `just ci` must stay herdr-free.

Rollback: remove opt-in branch; CreateProduction remains Unavailable.
