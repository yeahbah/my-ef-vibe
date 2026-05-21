# MyEfVibe integration tests (local only)

Live-database tests against the AdventureWorks sample repos on your machine. They are **not** run in CI (see `.github/workflows/ci.yml`, which only runs `MyEfVibe.Tests`).

## Prerequisites

1. Clone the sample repos under one directory (default in `integration-scenarios.json`: `/Users/arnolddiaz/Projects`).
2. Start each database (Docker compose in each repo, or your own instances).
3. For Oracle, set `connectionString` in `integration-scenarios.json` or rely on the sample value for `localhost:1521/FREEPDB1`.
4. For SQLite, ensure `Source/AdventureWorksLT.db` exists under the SQLite repo (tests skip if the DB file is missing).

## Run

```bash
export EFVIBE_RUN_INTEGRATION=1
export EFVIBE_INTEGRATION_ROOT=/Users/arnolddiaz/Projects   # optional if manifest integrationRoot is correct

dotnet test tests/MyEfVibe.IntegrationTests/MyEfVibe.IntegrationTests.csproj
```

Without `EFVIBE_RUN_INTEGRATION=1`, all tests are skipped.

## Scenarios

| Id | Provider | Repo (under integration root) |
|----|----------|-----------------------------|
| `sqlserver` | SQL Server | `AdventureWorks` |
| `postgresql` | PostgreSQL | `AdventureWorksPg/AdventureWorks` |
| `oracle` | Oracle | `AdventureWorksOra/app/AdventureWorks` |
| `sqlite` | SQLite | `AdventureWorksSqlite/AdventureWorks` |

Edit `integration-scenarios.json` to change paths, TFM (`framework`), or connection strings.
