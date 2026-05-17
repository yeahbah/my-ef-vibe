# MyEfVibe features

MyEfVibe is an interactive CLI for running LINQ against an **external** EF Core `DbContext`. It builds your workspace, loads its assemblies into a Roslyn scripting session, and exposes the context as `dbContext` in a REPL.

Licensed under [Apache 2.0](LICENSE).

## Core workflow

1. Point the tool at a workspace directory (a .NET project or solution folder).
2. MyEfVibe resolves the host `.csproj` (auto-select or `-p`), runs `dotnet build`, and loads assemblies from the output folder and `.deps.json` (including NuGet package paths and RID-specific runtimes such as `runtimes/unix/...` on macOS).
3. It locates a concrete `DbContext` type and constructs an instance when possible.
4. It attaches workspace assemblies to a Roslyn scripting session so entity types, extension methods, and project types are available in scripts.
5. You run LINQ in the REPL (or a one-shot expression); results, SQL, and metrics are shown in the terminal.

### Project selection

When `-p` is omitted and multiple projects exist, candidates are scored by:

- DbContext type names found in `.cs` sources
- EF Core package references (direct or transitive)
- Project kind (executable / web host preferred over pure class libraries)
- Whether the project references another candidate that contains a DbContext

Use `-p` to override. Use `-c` when multiple `DbContext` types are found.

In CI or piped stdin (non-interactive), specify `-p` when the workspace has several projects and auto-selection is ambiguous.

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
| `expression` (positional) | Same as `-e` when passed as trailing arguments |
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
| **Tab** | Keyword completion (LINQ / `dbContext` helpers) |
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
| `:plan` | Execution plan for last translated SQL — `EXPLAIN` (PostgreSQL), `EXPLAIN QUERY PLAN` (SQLite), `SET SHOWPLAN_ALL` (SQL Server, separate batches) |
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
efvibe -w ./MyApp dbContext.Products.Count();
```

## Database providers

| Provider | `--provider` | Notes |
|----------|--------------|--------|
| SQL Server | `sqlserver` | Use Docker on macOS/Linux; requires workspace `Microsoft.Data.SqlClient` + `Microsoft.EntityFrameworkCore.SqlServer` |
| PostgreSQL | `npgsql` | `EXPLAIN` for `:plan` |
| SQLite | `sqlite` | `EXPLAIN QUERY PLAN` for `:plan`; good for local files |

Pass `--connection-string` (or rely on `appsettings*.json` next to the build output). `--provider` is required when using `-cs` explicitly.

## macOS notes

- **SQL Server:** run the database in Docker; connect to `localhost,1433` (or your mapped port). This is the normal cross-platform dev setup — not a Windows-only stack.
- **Assembly loading:** library projects keep dependencies in the NuGet cache; `efvibe` reads `.deps.json` so EF Core and SqlClient resolve correctly on Unix.
- **Avoiding host conflicts:** the tool preloads workspace `System.Configuration.ConfigurationManager` (9.x) before SqlClient initializes, so it does not clash with older copies pulled in by optional Roslyn packages.

## Open source and commercial

- **Open source:** full CLI under [Apache 2.0](LICENSE).
- **Commercial:** optional paid tiers may be offered separately; the OSS CLI remains Apache 2.0.
