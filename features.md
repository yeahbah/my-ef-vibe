# MyEfVibe features

MyEfVibe is an interactive CLI for running LINQ against an **external** EF Core `DbContext`. It builds your workspace, loads its assemblies into a Roslyn scripting session, and exposes the context as `dbContext` in a REPL.

Licensed under [Apache 2.0](LICENSE). Optional paid tiers are described in [COMMERCIAL.md](COMMERCIAL.md).

## Core workflow

1. Point the tool at a workspace directory (a .NET project or solution folder).
2. MyEfVibe builds the project and locates a `DbContext` type.
3. It attaches to the workspace assemblies so entity types, extension methods, and project types are available in scripts.
4. You run LINQ in the REPL (or a one-shot expression); results, SQL, and metrics are shown in the terminal.

Example:

```csharp
dbContext.JsonBlobDocuments
    .AsNoTracking()
    .Where(d => d.Title.Contains("demo"))
    .Take(10)
    .ToList();
```

## CLI options

| Option | Description |
|--------|-------------|
| `-w`, `--workspace` | Workspace directory (required) |
| `-p`, `--project` | Explicit `.csproj` when several exist |
| `-c`, `--context` | Fully qualified `DbContext` type name |
| `--connection-string`, `-cs` | Connection string for manual `DbContextOptions` construction |
| `--provider` | Provider with `-cs`: `sqlserver`, `npgsql`, `sqlite` |
| `-e`, `--expression` | Run one expression and exit |
| `-s`, `--sql` | Show SQL (default: **on**) |

Install as a .NET tool: `dotnet tool install --global efvibe`, then run `efvibe`.
Requires .NET 8+ (package includes net8.0, net9.0, and net10.0 tool assets).
Local repo: `dotnet tool restore` (see `.config/dotnet-tools.json`).

## REPL input

| Action | Behavior |
|--------|----------|
| **Enter** | Next line (`…` continuation prompt) |
| **`;` at end of line** | Run the full snippet |
| **`;` alone on a line** | Run what you typed above |
| **Shift+Enter** | Newline inside the current input |
| **Tab** | Code completion (Roslyn) |
| **↑ / ↓** | Command history |

Statements such as `var id = 1;` keep their terminator for Roslyn. Trailing `;` on a **final expression** line is stripped so the REPL can display a return value (e.g. `.ToList();`).

`:commands` (e.g. `:help`, `:quit`) run on a single line and do not require `;`.

## SQL output

When SQL display is on (default):

- **Translated SQL** — `IQueryable` SQL from EF Core (`ToQueryString()`), including parameter comments.
- **Executed SQL** — commands captured via EF Core logging while the snippet runs.

Toggle in the REPL: `:sql`, `:sql on`, `:sql off`.

## Results and footer

After each evaluation:

- Formatted result (rows, scalars, or type name for complex objects).
- Footer: total time, DB time, SQL command count, row count, materialized vs deferred, rough size estimate.
- **Warnings** when the snippet looks risky (see below).

## Snippet warnings

Heuristic warnings (non-blocking), for example:

- `AsEnumerable()` — possible client-side evaluation
- `ToList()` without `Take()` — large materialization
- Multiple `Include()` — cartesian explosion risk
- `Take()` without `OrderBy` — undefined ordering
- `TagWith()` — reminder to check tagged SQL

Re-show with `:warnings`.

## REPL commands

| Command | Description |
|---------|-------------|
| `:help`, `:h`, `:?` | Command list and examples |
| `:clear`, `:cls` | Clear the terminal |
| `:reset` | Clear script variables (`dbContext` unchanged) |
| `:sql` | Toggle SQL output |
| `:stats` | Session evaluation table and aggregates |
| `:tracked` | Change tracker summary by state |
| `:tables` | DbSets with row counts |
| `:plan` | `EXPLAIN` for last translated SQL (Npgsql, SQLite, SQL Server) |
| `:compare set` | Set baseline for comparison |
| `:compare` | Diff baseline vs last run (timings, rows, SQL) |
| `:compare clear` | Clear comparison baseline |
| `:history stats` | Input history with per-snippet timings |
| `:benchmark N` | Run last snippet `N` times (default 5) |
| `:export csv\|json [path]` | Export last tabular result |
| `:warnings` | Warnings for last evaluation |
| `:chart`, `:viz` | Terminal charts (see below) |
| `:quit`, `:q`, `:exit` | Exit |

### Charts (`:chart`)

| Subcommand | Chart |
|------------|--------|
| `:chart stats` | Bar chart of recent evaluation times (ms) |
| `:chart timing` | Breakdown of last run (database vs app/Roslyn) |
| `:chart compare` | Baseline vs current timings |
| `:chart tables` | DbSet row counts |
| `:chart result` | Numeric column from last result (≤25 rows) |

`:benchmark` also shows an iteration timing chart.

## Session analytics

Per evaluation is recorded for `:stats`, `:compare`, `:history stats`, and `:benchmark`:

- Elapsed and database time, SQL command count
- Translated and executed SQL text
- Result kind, row count, materialization, estimated size
- Snippet text and warnings

## UI

- Spectre.Console panels, tables, and colors
- Startup banner and session panel (context, project, SQL toggle, input hints)
- Spinner while the workspace builds

## Scripting model

- Roslyn C# scripting with cumulative submissions (`var`, multiple lines, then use variables on later lines).
- Workspace references and namespaces imported automatically.
- Compilation and runtime errors shown in panels; script state resets after errors.

## One-shot mode

Non-interactive run for CI or quick checks:

```bash
efvibe -w ./MyApp -e "dbContext.Products.Count();"
```

## Open source and commercial

- **Open source:** full CLI under [Apache 2.0](LICENSE).
- **Commercial:** planned Pro / Team / Enterprise add-ons (hosted features, team libraries, support) — see [COMMERCIAL.md](COMMERCIAL.md). Not required to use the OSS tool.
