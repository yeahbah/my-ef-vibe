# MyEfVibe

Interactive CLI to run LINQ against an external .NET project's EF Core `DbContext`.

Point `efvibe` at a workspace, get a REPL with `dbContext` in scope, see translated SQL,
execution metrics, and helpers like `:plan`, `:stats`, and `:tracked`.

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer (tool ships `net8.0`, `net9.0`, and `net10.0` assets).

## Install

From NuGet (when published):

```bash
dotnet tool install --global efvibe
```

From a local build:

```bash
dotnet pack src/MyEfVibe/MyEfVibe.csproj -c Release -o ./artifacts
dotnet tool install --global efvibe --add-source ./artifacts
```

### Local tool (per repository)

Restore the pinned tool after cloning:

```bash
dotnet tool restore
efvibe -w ./path/to/project
```

Update [`.config/dotnet-tools.json`](.config/dotnet-tools.json) when releasing a new version.

## Quick start

```bash
efvibe -w /path/to/your/dotnet/project
```

When several `.csproj` files exist, `efvibe` scores them (DbContext in source, EF references, host vs library) and picks one automatically, or prompts you to choose. Use `-p` in scripts or CI when several projects exist and the host is not obvious.

In the REPL, end input with `;` to run. Use `:help` for commands.

One-shot:

```bash
efvibe -w ./MyApp -e "dbContext.Products.Count();"
```

Explicit project and SQL Server (e.g. AdventureWorks with Docker on macOS/Linux):

```bash
efvibe -w . \
  -p ./src/MyApp.Infrastructure/MyApp.Infrastructure.csproj \
  -c MyApp.Infrastructure.DbContexts.AppDbContext \
  --provider sqlserver \
  --connection-string "Server=localhost,1433;Database=MyApp;User Id=sa;Password=Your_password;Encrypt=false;TrustServerCertificate=true"
```

## macOS and SQL Server

SQL Server is not Windows-only for development. On macOS (and Linux), run **SQL Server in Docker** and connect with `--provider sqlserver`. The tool loads the Unix `Microsoft.Data.SqlClient` runtime from the workspace `.deps.json` (not the portable `lib/` assembly).

Typical issues:

| Symptom | Cause / fix |
|---------|-------------|
| `SqlClient is not supported on this platform` | Old `efvibe` build; use a current build with RID-aware dependency loading. |
| `LocalAppContextSwitches` / `ConfigurationManager` errors | Host/tool vs workspace assembly conflict; fixed in recent builds (workspace deps preload). |
| `:plan` — `SET SHOWPLAN` batch error | SQL Server requires `SET SHOWPLAN_ALL` in its own batch; fixed in recent builds. |

For greenfield Mac work without Docker, use `--provider sqlite` or `npgsql` on a project that targets those providers.

## Workspace discovery

`efvibe` builds your project and resolves NuGet dependencies from `.deps.json`, including **class library** projects that do not copy EF/SqlClient into `bin/`. It scans workspace assemblies for `DbContext` types and can construct a context via:

- `IDesignTimeDbContextFactory<T>`
- Parameterless constructor
- `--connection-string` + `--provider` (`sqlserver` \| `npgsql` \| `sqlite`)

Connection strings can also be read from `appsettings*.json` near the built output or workspace root.

## License

Licensed under the [Apache License, Version 2.0](LICENSE).

The open source CLI is free to use under Apache 2.0. See [features.md](features.md) for the full command reference.

## Publishing

Every push to `main` runs CI, then automatically:

1. Computes the next patch version (max of latest `v*` git tag, NuGet, and `.csproj`)
2. Creates and pushes a `v*` tag (e.g. `v0.1.4`)
3. Publishes that version to [NuGet](https://www.nuget.org/packages/efvibe) and opens a GitHub Release

Set the repository secret **`NUGET_API_KEY`** ([nuget.org API key](https://www.nuget.org/account/apikeys)) for publish to work.

Manual publish: **Actions → Publish to NuGet → Run workflow** (optional version input), or push a tag:

```bash
git tag v0.1.5 && git push origin v0.1.5
```

Version-bump commits from CI include `[skip ci]` so they do not trigger another release.

## Contributing

Contributions welcome via pull request. By contributing, you agree that your
contributions are licensed under the Apache License 2.0.
