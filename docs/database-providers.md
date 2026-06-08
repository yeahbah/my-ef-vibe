# Database providers

efvibe discovers the EF Core provider from **`PackageReference` entries on `-p`** (including referenced projects). Connection strings from `-s` are opaque — the tool does **not** infer the provider from connection string text.

## Auto discovery (default)

```
-p Persistence.csproj   → one EF provider package in the project graph
-s Api.csproj           → connection string from user secrets / appsettings
```

Reference exactly **one** relational EF provider package on `-p`. If several packages are present (for example SQL Server and PostgreSQL in the same `.csproj`), construction fails with a message listing the packages and suggesting `--provider`.

Excluded from auto-discovery: `Microsoft.EntityFrameworkCore`, `.Design`, `.Tools`, `.InMemory`, analyzers, and similar non-provider packages.

## Override with `--provider`

Use `--provider` when:

- You pass `--connection-string` explicitly (required together with `-cs`)
- `-p` references more than one EF provider package
- You want to force a provider that differs from the `-p` package (integration tests, cross-provider experiments)

Accepted values:

| Form | Example |
|------|---------|
| Alias | `sqlserver`, `npgsql`, `sqlite`, `oracle`, `mysql`, `mariadb` |
| EF package id | `Microsoft.EntityFrameworkCore.SqlServer`, `FirebirdSql.EntityFrameworkCore.Firebird` |

Aliases `postgres`, `pg`, `mssql`, `mariadb`, and similar shorthands are also accepted (see CLI `--help`).

## Known providers

| Provider | Alias | EF package (typical) | `:plan` | Naming customizers |
|----------|-------|----------------------|---------|-------------------|
| SQL Server | `sqlserver` | `Microsoft.EntityFrameworkCore.SqlServer` | Yes | No |
| PostgreSQL | `npgsql` | `Npgsql.EntityFrameworkCore.PostgreSQL` | Yes | Yes |
| SQLite | `sqlite` | `Microsoft.EntityFrameworkCore.Sqlite` | Yes | Yes |
| Oracle | `oracle` | `Oracle.EntityFrameworkCore` | Yes | No |
| MySQL | `mysql` | `Pomelo.EntityFrameworkCore.MySql` or `MySql.EntityFrameworkCore` | Yes | No |
| MariaDB | `mariadb` | `MariaDB.EntityFrameworkCore` | Yes | No |

Pomelo MySQL/MariaDB uses a dedicated configurator for `ServerVersion` when the `(builder, string)` overload is not enough.

## Other relational EF packages

Any package matching `*.EntityFrameworkCore.*` (for example `FirebirdSql.EntityFrameworkCore.Firebird`) gets **Tier Sql** by default:

- DbContext construction (when `Use*(builder, string)` or a registered configurator succeeds)
- LINQ REPL and SQL translation (`ToQueryString()`)
- `:plan` / EXPLAIN is not available until capabilities are registered for that provider

`:dbinfo` shows the resolved **EF provider package** and **feature tier** for the active session.

## Feature tiers

| Tier | What works |
|------|------------|
| **Sql** | DbContext, LINQ REPL, SQL translation (default for newly discovered providers) |
| **QueryPlan** | Above + `:plan` / EXPLAIN |
| **Conventions** | Above + PostgreSQL/SQLite naming customizers |

When `:plan` is unavailable, efvibe returns a friendly note — it does not fail the REPL session.

## DbContext construction order

1. `IDesignTimeDbContextFactory<T>`
2. Parameterless constructor
3. Startup project user secrets, then `appsettings*.json`
4. `--connection-string` + `--provider`

Preferred connection string keys: `ConnectionStrings:DefaultConnection`, then `Postgres`, `Sqlite`, `MySql`, `MariaDb`, `Oracle`, `Database`, then any other `ConnectionStrings:*` entry.

## Editor extensions

VS Code, Rider, and Visual Studio pass `--provider` from project settings when set. Leave **Provider** empty to rely on `-p` discovery. When using a connection string override in the IDE, set provider to an alias or EF package id.

## Limits

- **Cosmos DB** and **InMemory** are not supported through the relational auto-construct path.
- Non-standard `Use*` signatures may require a registered `IProviderConfigurator` (Pomelo is built in today).
- Central Package Management (`Directory.Packages.props`) is supported when `PackageReference` entries appear in the `-p` project graph.
