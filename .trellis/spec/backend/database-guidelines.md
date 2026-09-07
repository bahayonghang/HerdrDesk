# Database Guidelines

**N/A.** HerdDesk G0 has no database.

There is no ORM, no migrations, no SQL, no connection string, and no embedded store in `src/` or `scripts/`. Session identity and terminal state are in-memory parser fields (`lastSequence`, `closed`, `failed`) for one connection epoch.

Do not add Entity Framework, SQLite, or a repository layer while implementing G0 protocol, setup, or harness-doc tasks. Persistence, if a later approved phase needs it, is a new design. Do not treat Trellis frontend or generic backend templates as a requirement to create tables.
