# Bpm.Admin.SeedCli

Dev-only console for resetting and seeding the admin DB. Refuses to run in
non-Development environments unless `FLOWCOOK_ALLOW_SEED=1`.

## Subcommands

```
dotnet run -- clear         # drop + recreate admin DB (no data)
dotnet run -- seed          # clear + (no data unless flags)
dotnet run -- seed --org    # clear + seed minimal org graph
dotnet run -- status        # report current row counts per table
```

## Seeded data (`--org`)

- 6 depts (Acme Corp → Engineering → Backend / Frontend; Product; HR)
- 13 users with passwords (default: `flowcook2026`)
- 1 group ("Security Committee") with three cross-dept members
- 14 roles
- A handful of role assignments demonstrating direct + dept-inherit + group-inherit
- 1 sample Delegation (Alice → Bob, starts in 2 days)

## Demo login

All seeded users share password `flowcook2026`. Emails are
`{name}@acme.example` (lower-cased), e.g. `alice@acme.example`.

## Connection string

Reads `ConnectionStrings:Admin` from `appsettings.json` / env vars
(`FLOWCOOK_ConnectionStrings__Admin=...`). Defaults to
`Data Source=admin.dev.db` in the working directory.

## TODO

- `clear` should also drop the bpm DB once flowcook-step4 introduces it.
