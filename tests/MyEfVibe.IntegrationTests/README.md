# MyEfVibe integration tests (local only)

Live-database tests against [AdventureWorks](https://github.com/theMickster/AdventureWorks) on your machine. They are *
*not** run in CI (see `.github/workflows/ci.yml`, which only runs `MyEfVibe.Tests`).

All four providers use the **same** repo: `/home/adiaz/Projects/AdventureWorks`. Databases are provisioned from the SQL
Server `AdventureWorks2022` backup via Docker.

## Quick setup

```bash
chmod +x tests/MyEfVibe.IntegrationTests/scripts/*.sh
tests/MyEfVibe.IntegrationTests/scripts/setup.sh
```

This script:

1. Clones or updates `AdventureWorks` under your integration root.
2. Restores `AdventureWorks2022` into `adventureworks-sql` (port **1433**).
3. Starts PostgreSQL and Oracle containers (`docker/docker-compose.yml`).
4. Converts the SQL Server database to PostgreSQL (pgloader), SQLite (DuckDB), and Oracle (Python).
5. Builds **myefvibe**.

Override paths with `EFVIBE_INTEGRATION_ROOT` if needed.

Re-run conversion only:

```bash
tests/MyEfVibe.IntegrationTests/scripts/convert-databases.sh
```

## Run

```bash
export EFVIBE_RUN_INTEGRATION=1
export EFVIBE_INTEGRATION_ROOT=/home/adiaz/Projects

dotnet test tests/MyEfVibe.IntegrationTests/MyEfVibe.IntegrationTests.csproj
```

Without `EFVIBE_RUN_INTEGRATION=1`, all tests are skipped.

## Databases (Docker)

| Scenario     | Container / file                            | Port | Source                                                                                                    |
|--------------|---------------------------------------------|------|-----------------------------------------------------------------------------------------------------------|
| `sqlserver`  | `adventureworks-sql`                        | 1433 | `AdventureWorks2022.bak` restore                                                                          |
| `postgresql` | `efvibe-integration-postgres`               | 5432 | pgloader from SQL Server → `adventureworks` DB                                                            |
| `oracle`     | `efvibe-integration-oracle`                 | 1521 | copy from PostgreSQL; column/table names taken from SQL Server metadata (pgloader lowercases identifiers) |
| `sqlite`     | `AdventureWorks/Source/AdventureWorksLT.db` | —    | copy from PostgreSQL via DuckDB                                                                           |

## Scenarios

| Id           | Provider   | Repo             | Notes                                                                                                  |
|--------------|------------|------------------|--------------------------------------------------------------------------------------------------------|
| `sqlserver`  | SQL Server | `AdventureWorks` | `AdventureWorks2022` on localhost:1433                                                                 |
| `postgresql` | PostgreSQL | `AdventureWorks` | lowercase schemas (`production.product`); efvibe applies naming hints                                  |
| `oracle`     | Oracle     | `AdventureWorks` | `AdvWorks` / `localhost:1521/FREEPDB1`                                                                 |
| `sqlite`     | SQLite     | `AdventureWorks` | `Source/AdventureWorksLT.db`; tables named `Schema.Table`; efvibe maps EF schema/table to dotted names |

Edit `integration-scenarios.json` to change paths, TFM (`framework`), or connection strings.
