# MyEfVibe features

MyEfVibe is an interactive CLI for running LINQ against an **external** EF Core `DbContext`. It builds the EF project, loads assemblies into a Roslyn scripting session, and exposes the context as `db` in a REPL.

Licensed under [Apache 2.0](LICENSE).

## Core workflow

1. Use the default workspace root (`~/.efvibe` or `%APPDATA%\efvibe`) or pass **`-w`**. Session files live under **`<ProjectName>/<DbContextName>/`** (e.g. `~/.efvibe/AdventureWorks.Infrastructure.Persistence/AdventureWorksDbContext/`).
2. MyEfVibe resolves the **EF project** to build (`-p`, auto-select, or prompt) and the **startup project** for configuration (`-s` / `--startup-project`, auto-inferred from project references, or same as EF project).
3. It runs `dotnet build` on the EF project and loads assemblies from the output folder and `.deps.json` (including NuGet package paths and RID-specific runtimes such as `runtimes/unix/...` on macOS).
4. It locates a concrete `DbContext` type and constructs an instance when possible.
5. It attaches workspace assemblies to a Roslyn scripting session so entity types, extension methods, and project types are available in scripts.
6. You run LINQ in the REPL (or a one-shot expression); results, SQL, and metrics are shown in the terminal.

### EF project selection (`-p`)

When `-p` is omitted, projects are discovered under the **current working directory** (not `-w`). Candidates are scored by:

- DbContext type names found in `.cs` sources
- EF Core package references (direct or transitive)
- Project kind (executable / web host preferred over pure class libraries)
- Whether the project references another candidate that contains a DbContext

Use `-p` for the `.csproj` that contains the `DbContext` (often a persistence/infrastructure library). Use `-c MyDbContext` (short name) or the full type name when multiple `DbContext` types are found.

In CI or piped stdin (non-interactive), specify `-p` when several projects exist and auto-selection is ambiguous.

### Startup project selection (`-s`, `--startup-project`)

Configuration is read from the **startup project**, not the EF project:

- **User secrets** — `UserSecretsId` in the startup `.csproj`
- **`appsettings.json` / `appsettings.Development.json`** — next to that project

When `-s` / `--startup-project` is omitted, `efvibe` looks for projects that **reference** the EF project and scores them by user secrets, appsettings, and whether they are a web host or executable. If none match, the EF project is used for config.

Typical layout:

| Project | Flag | Purpose |
|---------|------|---------|
| `AdventureWorks.Infrastructure.Persistence` | `-p` | Build, load `DbContext` and entities |
| `AdventureWorks.API` | `-s` / `--startup-project` | `dotnet user-secrets`, `appsettings`, Docker SQL connection |

Passing only `-p` to a class library without `-s` can yield wrong connection strings (for example Windows SSPI on macOS).

Example:

```csharp
db.JsonBlobDocuments
    .AsNoTracking()
    .Where(d => d.Title.Contains("demo"))
    .Take(10)
    .ToList();
```

## CLI options

| Option | Description |
|--------|-------------|
| `-w`, `--workspace` | Workspace root; session path is `<ProjectName>/<DbContextName>/` beneath it; default `~/.efvibe` or `%APPDATA%\efvibe` |
| `-p`, `--project` | EF Core `.csproj` to build (DbContext assembly) |
| `-s`, `--startup-project` | Startup `.csproj` for user secrets / appsettings (auto-inferred when omitted). `-s` is not used for SQL — use `--sql` or `:sql`. |
| `-c`, `--context` | `DbContext` type name (e.g. `MyDbContext`) or fully qualified name |
| `--connection-string`, `-cs` | Connection string for manual `DbContextOptions` construction |
| `--provider` | Provider with `-cs`: `sqlserver`, `npgsql`, `sqlite` |
| `-e`, `--expression` | Run one expression and exit |
| `expression` (positional) | Same as `-e` when passed as trailing arguments |
| `--sql` | Show SQL (default: **on**; toggle in REPL with `:sql`) |

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
| **Tab** | Keyword completion (LINQ / `db` helpers) |
| **↑ / ↓** | Command history |

Statements such as `var id = 1;` keep their terminator for Roslyn. Trailing `;` on a **final expression** line is stripped so the REPL can display a return value (e.g. `.ToList();`).

`:commands` (e.g. `:help`, `:quit`) run on a single line and do not require `;`.

### Scripting global: `db`

The active `DbContext` instance is exposed as **`db`** in every snippet:

```csharp
db.Orders.Where(o => o.Total > 100).Count()
```

`:reset` clears script variables but leaves `db` unchanged.

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
| `:about` | Tool version, license, repository, and current session summary |
| `:clear`, `:cls` | Clear the terminal |
| `:reset` | Clear script variables (`db` unchanged) |
| `:sql` | Toggle SQL output |
| `:stats` | Session evaluation table and aggregates |
| `:tracked` | Change tracker summary by state |
| `:tables` | DbSets with row counts |
| `:dbinfo` | DbContext type, provider, connection string, server version, and related metadata |
| `:describe <entity>`, `:desc` | Entity property sheet (see below) |
| `:scan lite` | Static Roslyn scan of EF project sources for slow-query patterns (see below) |
| `:scan deep` | Lite scan plus `ToQueryString()` SQL per call site using live `db` |
| `:next`, `:prev` | Step through scan review (also **→** / **←** on empty prompt) |
| `:dismiss`, `:note` | Skip finding in future scans · save a note (**Del** = dismiss on empty prompt) |
| `:repeat`, `:end` | Restart scan review · exit scan review |
| `:plan` | Execution plan for last translated SQL — `EXPLAIN` (PostgreSQL), `EXPLAIN QUERY PLAN` (SQLite), `SET SHOWPLAN_ALL` (SQL Server, separate batches) |
| `:compare set` | Set baseline for comparison |
| `:compare` | Diff baseline vs last run (timings, rows, SQL) |
| `:compare clear` | Clear comparison baseline |
| `:history stats` | Input history with per-snippet timings |
| `:benchmark N` | Run last snippet `N` times (default 5) |
| `:export csv\|json [path]` | Export last tabular result to `<ProjectName>/<DbContextName>/`; optional path is relative to that folder |
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

### Static LINQ scan (`:scan lite`, `:scan deep`)

**`:scan lite`** walks `.cs` files in:

1. The **EF project** (`-p`) and everything it references (e.g. domain), and  
2. When different, the **startup project** (`-s`, usually the API) and everything *it* references, and  
3. Any other project in the solution that **references** `-p` (e.g. **Application** when API → Application → Persistence, even if the API has no LINQ in its own `.cs` files).

Test projects are skipped. This covers persistence + API + application-layer repositories. It uses Roslyn syntax analysis and the same heuristics as snippet `:warnings` — no database, no SQL generation.

**`:scan deep`** runs the lite heuristics, then attempts **`ToQueryString()`** for each query call site using the live REPL `db` context (requires a working connection). Source is adapted for the REPL: `DbContext` → `db`, conditions extracted from `if` / `while` / `switch`, and terminal operators removed (including `AnyAsync(ct)`, `ToListAsync(cancellationToken)`, etc.). Expressions that still depend on method parameters, locals, or other runtime-only values may fail translation — the note is shown on the finding.

```text
:scan lite
:scan deep
```

Output:

1. Summary panel — finding count, files scanned, project count (deep also shows SQL translated / failed counts)
2. Findings saved under `<ProjectName>/<DbContextName>/` (`myefvibe-scan-lite.json`, `myefvibe-scan-deep.json`, `myefvibe-scan-dismissals.json`, `myefvibe-scan-notes.json`)
3. **Review queue** — one finding at a time; step through with:

| Command | Action |
|---------|--------|
| `:next` or **→** (empty prompt) | Next finding |
| `:prev` or **←** (empty prompt) | Previous finding |
| `:dismiss` or **Del** (empty prompt) | Dismiss current finding — excluded from future scans (optional note with `:dismiss …`) |
| `:note` text… | Save a required note (shown in **yellow** on next scan) |
| `:repeat` | Back to the first finding |
| `:end` | Exit review mode |

At the last finding, `:next` reports that the queue is complete.

Dismissals and notes are keyed by file, line, and rule id (`myefvibe-scan-dismissals.json`, `myefvibe-scan-notes.json` under the project/DbContext session folder).

Each finding includes a **Fix** section with concrete remediation hints (e.g. `AsSplitQuery()` for cartesian includes, `OrderBy` before `Take`, batching for N+1). Deep findings may also show a **Translated SQL** panel.

Rules include: client-side `AsEnumerable()`, unbounded materialization, multiple `Include`/`ThenInclude`, `Take` without `OrderBy`, raw SQL, possible N+1 inside loops, and (deep only) `query-site` entries for call sites with SQL but no heuristic warning.

### Schema and connection (`:tables`, `:describe`, `:dbinfo`)

**`:tables`** — table of each DbSet name, CLR entity type, and row count (`Count()` per set).

**`:describe <entity>`** (alias `:desc`) — resolves an entity from DbSet names or type names on the current context:

```text
:describe Product
:describe Products
:describe AddressEntity
:describe AdventureWorks.Domain.Product
```

Matching order: exact DbSet name → exact type name → suffix → substring. Ambiguous names list candidates; unknown names list all DbSets.

Output columns: **Member**, **Type**, **nullable**, **Notes**. Scalar properties are listed first; navigation properties last with a `navigation` note. When EF Core model metadata is available, notes can include `PK`, `FK`, `column: …`, and `max N`.

**`:dbinfo`** — panel with:

- DbContext full type name
- EF project and startup project (when different)
- Session directory (`<ProjectName>/<DbContextName>/` under workspace root)
- EF Core assembly version
- Provider display name and EF provider name
- Command timeout and DbSet count
- Live connection: state, data source, database, server version, connection string

## Session analytics

Per evaluation is recorded for `:stats`, `:compare`, `:history stats`, and `:benchmark`:

- Elapsed and database time, SQL command count
- Translated and executed SQL text
- Result kind, row count, materialization, estimated size
- Snippet text and warnings

## UI

- Spectre.Console panels, tables, and colors
- Startup lines: session directory, EF project, startup project (when different), build status
- Session panel (context, project, SQL toggle, input hints)
- Spinner while the EF project builds

## Scripting model

- Roslyn C# scripting with cumulative submissions (`var`, multiple lines, then use variables on later lines).
- Workspace references and namespaces imported automatically.
- Compilation and runtime errors shown in panels; script state resets after errors.

## One-shot mode

Non-interactive run for CI or quick checks:

```bash
efvibe -w ./myefvibe-session \
  -p ./src/MyApp.Infrastructure/MyApp.Infrastructure.csproj \
  -s ./src/MyApp.Api/MyApp.Api.csproj \
  -e "db.Products.Count();"
```

```bash
efvibe -w ./myefvibe-session -p ./src/MyApp.Api/MyApp.Api.csproj db.Products.Count();
```

## Database providers

| Provider | `--provider` | Notes |
|----------|--------------|--------|
| SQL Server | `sqlserver` | Use Docker on macOS/Linux; requires workspace `Microsoft.Data.SqlClient` + `Microsoft.EntityFrameworkCore.SqlServer` |
| PostgreSQL | `npgsql` | `EXPLAIN` for `:plan` |
| SQLite | `sqlite` | `EXPLAIN QUERY PLAN` for `:plan`; good for local files |

Pass `--connection-string` (or rely on the startup project). `--provider` is required when using `-cs` explicitly.

### Connection string resolution (no `-cs`)

When you do not pass `--connection-string`, and the DbContext cannot be created via a design-time factory or parameterless constructor, `efvibe` loads credentials from the **startup project** in this order:

1. **User secrets** — `UserSecretsId` on the startup `.csproj`, then `~/.microsoft/usersecrets/<id>/secrets.json` (macOS/Linux).
2. **`appsettings.json` / `appsettings.Development.json`** — next to the startup project (and its `bin` output if present).

Preferred keys: `ConnectionStrings:DefaultConnection`, then `Postgres`, `Sqlite`, `Database`, then any other `ConnectionStrings:*` entry. Provider is inferred from EF assemblies in the **built EF project** output.

Use `-p` for the persistence/library project and `-s` for the API (or rely on auto-inference when the API references the library).

## macOS notes

- **SQL Server:** run the database in Docker; connect to `localhost,1433` (or your mapped port). Store `User Id=sa;Password=...` in the **API** user secrets or appsettings via `-s` — not integrated security.
- **SSPI errors:** `Cannot generate SSPI context` usually means config was read from the wrong project. Point `-s` at the API, not the persistence library.
- **Assembly loading:** library projects keep dependencies in the NuGet cache; `efvibe` reads `.deps.json` so EF Core and SqlClient resolve correctly on Unix.
- **Avoiding host conflicts:** the tool preloads workspace `System.Configuration.ConfigurationManager` (9.x) before SqlClient initializes, so it does not clash with older copies pulled in by optional Roslyn packages.

## Open source and commercial

- **Open source:** full CLI under [Apache 2.0](LICENSE).
- **Commercial:** optional paid tiers may be offered separately; the OSS CLI remains Apache 2.0.
