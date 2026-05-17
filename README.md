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

See [PUBLISHING.md](PUBLISHING.md) for packing and pushing `efvibe` to NuGet.org.

CI runs on push/PR; NuGet publish runs when you push a `v*` tag (requires `NUGET_API_KEY` secret).

## Contributing

Contributions welcome via pull request. By contributing, you agree that your
contributions are licensed under the Apache License 2.0.
