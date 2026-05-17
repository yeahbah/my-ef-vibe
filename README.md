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

In the REPL, end input with `;` to run. Use `:help` for commands.

One-shot:

```bash
efvibe -w ./MyApp -e "dbContext.Products.Count();"
```

## License

Licensed under the [Apache License, Version 2.0](LICENSE).

Optional paid tiers (Pro / Team / Enterprise) are described in [COMMERCIAL.md](COMMERCIAL.md).
The open source CLI remains free to use under Apache 2.0.

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
