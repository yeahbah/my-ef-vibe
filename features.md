# MyEfVibe features

**Website:** [myefvibe.com](https://myefvibe.com/) · **Documentation:** [myefvibe.com/docs](https://myefvibe.com/docs/)

MyEfVibe is an interactive CLI for running LINQ against an **external** EF Core `DbContext`. It builds the EF project, loads assemblies into a Roslyn scripting session, and exposes the context as `db` in a REPL.

efvibe works with **most EF Core relational providers** — SQL Server, PostgreSQL, SQLite, Oracle, MySQL/MariaDB, Firebird, and other packages auto-discovered from `-p`. See [docs/database-providers.md](docs/database-providers.md).

Licensed under [Apache 2.0](LICENSE).

## Core workflow

1. Use the default workspace root (`~/.efvibe` or `%APPDATA%\efvibe`) or pass **`-w`**. Session files live under **`<ProjectName>/<DbContextName>/`** (e.g. `~/.efvibe/AdventureWorks.Infrastructure.Persistence/AdventureWorksDbContext/`).
2. MyEfVibe resolves the **EF project** to build (`-p`, auto-select, or prompt) and the **startup project** for configuration (`-s` / `--startup-project`, auto-inferred from project references, or same as EF project).
3. It runs `dotnet build` on the EF project and loads assemblies from the output folder and `.deps.json` (including NuGet package paths and RID-specific runtimes such as `runtimes/unix/...` on macOS).
4. It locates a concrete `DbContext` type and constructs an instance when possible.
5. It attaches workspace assemblies to a Roslyn scripting session so entity types, extension methods, and project types are available in scripts.
6. You run LINQ in the REPL, a one-shot expression (`-e`), or **`efvibe serve`** (long-running daemon for editors); results, SQL, and metrics are shown in the terminal or JSON for tools.

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
| `-s`, `--startup-project` | Startup `.csproj` for user secrets / appsettings (auto-inferred when omitted). `-s` is not used for SQL — use `--dblog` or `:dblog`. |
| `-c`, `--context` | `DbContext` type name (e.g. `MyDbContext`) or fully qualified name |
| `--connection-string`, `-cs` | Connection string for manual `DbContextOptions` construction (provider discovered from `-p`) |
| *(automatic)* | When building `DbContextOptions` from config, efvibe discovers the provider from `-p` `PackageReference` entries and invokes the matching `Use*` extension (optional satellite packages such as NetTopologySuite are applied when referenced) |
| `-e`, `--expression` | Run one expression and exit |
| `expression` (positional) | Same as `-e` when passed as trailing arguments |
| `--format` | One-shot output format: `text` (default) or `json` (for editors and scripts) |
| `--no-banner` | Suppress workspace/build banners (recommended with `--format json`) |
| `--with-plan` | With `-e --format json`, include EXPLAIN / SHOWPLAN for the evaluated SQL |
| `--dblog` | Show executed SQL via EF database logging (default: **on**; use `--no-dblog` to disable; toggle in REPL with `:dblog`) |
| `--no-dblog` | Disable database command logging for this run |
| `--about-json` | Write tool metadata as JSON to stdout and exit (no workspace or DbContext required) |

### `efvibe serve` (editor daemon)

Long-running mode for fast repeated evaluation (VS Code **Run Selection** uses this by default):

```bash
efvibe serve -p ./MyApp.Persistence.csproj -s ./MyApp.Api.csproj -c AppDbContext
# stdout: {"type":"ready","dbContext":"AppDbContext",...}
# stdin (one JSON object per line):
{"type":"eval","expression":"db.Products.Count()"}
{"type":"eval","expression":"db.Orders.Take(5).ToList()","withPlan":true}
{"type":"shutdown"}
```

Each `eval` response is the same JSON shape as `-e --format json`. Build and `DbContext` stay loaded until the process exits.

### `efvibe sql-to-linq` (SQL → LINQ draft)

EF-model-aware draft assistant for simple `SELECT` queries. Output includes confidence, table→DbSet mappings, and optional round-trip validation via `ToQueryString()`:

```bash
efvibe sql-to-linq -p ./MyApp.Persistence.csproj -c AppDbContext \
  --sql "SELECT TOP 10 * FROM Products WHERE Name = 'Helmet'" \
  --format json --no-banner
```

`serve` also accepts `{"type":"sqlToLinq","sql":"SELECT ..."}` for editor and Studio use.

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

### Repository snippets (from your codebase)

You can paste or run selections copied from repositories and handlers — not only `db.*` written for the REPL. When a snippet looks like repository code (multiline, `await`, `DbContext` / `dbContext`, `*Async(`, `cancellationToken`), efvibe prepares it before evaluation:

| Transformation | Example |
|----------------|---------|
| `DbContext` / `dbContext` → `db` | `await DbContext.Orders` → `db.Orders` |
| Strip `await` / `return` / `var x =` | `await query.FirstOrDefaultAsync()` → `query.FirstOrDefault()` |
| Stub method parameters | `entraObjectId` compared to `Rowguid` → `Guid.Empty` |
| Remove `cancellationToken` | `FirstOrDefaultAsync(cancellationToken)` → `FirstOrDefault()` |
| Async terminals → sync | `ToListAsync()` → `ToList()` |
| Drop translation-neutral ops | `AsNoTracking()` removed for probes |

**Limits:** stubbed parameters mean you see **SQL shape and sample execution**, not the same rows as production unless you declare variables in the REPL first (e.g. `var entraObjectId = Guid.Parse("...");`). Heavy `Include` / `SelectMany` chains may still fail at runtime depending on provider and data.

JSON for tools and the VS Code extension (one-shot or via `serve`):

```bash
efvibe -p ... -c MyDbContext -e "db.Products.Count()" --format json --no-banner
efvibe -p ... -c MyDbContext -e "db.Products.Take(5).ToList()" --format json --no-banner --with-plan
```

See [docs/efvibe-daemon-and-vscode.md](docs/efvibe-daemon-and-vscode.md) and [vscode-extension/README.md](vscode-extension/README.md).

### Rider extension

Install from the [JetBrains Marketplace](https://plugins.jetbrains.com/plugin/31961-my-ef-vibe): **Settings → Plugins → Marketplace**, search **`My EF Vibe`**. See [docs/rider-extension.md](docs/rider-extension.md).

| Area | Behavior |
|------|----------|
| Run Selection | `efvibe serve` daemon (default) or one-shot `-e --format json` |
| DbInfo / Tables / Describe / Scan | Same daemon when possible; CLI fallback |
| Tool window | Query editor, results, SQL, plan, model, scan review, notebook |

### VS Code extension (v0.5+)

Install from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=yeahbah.vscode-efvibe): Extensions → search **`efvibe`** (`yeahbah.vscode-efvibe`). See [vscode-extension/INSTALL.md](vscode-extension/INSTALL.md).

| Area | Behavior |
|------|----------|
| Run Selection | `efvibe serve` daemon (default) or one-shot `-e --format json` |
| Result panel | Editable expression, Run / Run Plan, export CSV/JSON, **📋 copy** on SQL and query plan blocks |
| Scan Review | Carousel tab after **Scan Workspace** — Previous/Next, Dismiss, Note, clickable location, copy on code/SQL/plan |
| Headless scan | `efvibe scan lite\|deep --json --no-banner` (same engine as REPL `:scan`) |

Settings: `efvibe.scan.openReviewOnScan`, `efvibe.scan.problemsPanel` (optional squiggles, default off).

## SQL output

When SQL display is on (default):

- **Translated SQL** — `IQueryable` SQL from EF Core (`ToQueryString()`), including parameter comments.
- **Executed SQL** — commands captured via EF Core logging while the snippet runs.

Toggle in the REPL: `:dblog`, `:dblog on`, `:dblog off` (optional `verbose` for full EF logs).

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
| `:dblog` | Toggle database command logging (sql-only by default) |
| `:stats` | Session evaluation table and aggregates |
| `:tracked` | Change tracker summary by state |
| `:tables` | DbSets and entity types (no row counts) |
| `:dbinfo` | DbContext type, provider, connection string, server version, and related metadata |
| `:describe <entity>`, `:desc` | Entity property sheet (see below) |
| `:scan lite` | Static Roslyn scan of EF project sources for slow-query patterns (see below) |
| `:scan deep` | Lite scan plus `ToQueryString()` SQL per call site using live `db` |
| `:next`, `:prev` | Step through scan review (also **→** / **←** on empty prompt) |
| `:dismiss`, `:note` | Skip finding in future scans · save a note (**Del** = dismiss on empty prompt) |
| `:repeat`, `:end` | Restart scan review · exit scan review |
| `:plan` | Execution plan for last translated SQL when supported — `EXPLAIN` (PostgreSQL), `EXPLAIN QUERY PLAN` (SQLite), `SET SHOWPLAN_ALL` (SQL Server, separate batches). Unavailable for newly discovered providers until capabilities are registered; shows a friendly note instead of failing. |
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

**`:scan deep`** runs the lite heuristics, then attempts **`ToQueryString()`** and **`EXPLAIN`** (same as `:plan`) for each query call site using the live REPL `db` context (requires a working connection). Source is adapted for the REPL: `DbContext` → `db`, conditions extracted from `if` / `while` / `switch`, and terminal operators removed (including `AnyAsync(ct)`, `ToListAsync(cancellationToken)`, etc.). Expressions that still depend on method parameters, locals, or other runtime-only values may fail translation — the note is shown on the finding.

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

Rules include: client-side `AsEnumerable()`, unbounded materialization, multiple `Include`/`ThenInclude`, `Take` without `OrderBy`, raw SQL, possible N+1 inside loops, and (deep only) `query-site` entries for call sites with SQL but no heuristic warning. Full reference: [docs/linq-scan-rules.md](docs/linq-scan-rules.md).

### Schema and connection (`:tables`, `:describe`, `:dbinfo`)

**`:tables`** — table of each DbSet name and CLR entity type (no database `Count()`).

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
- EF provider package and feature tier (when resolved from `-p`)
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

See [docs/database-providers.md](docs/database-providers.md) for the full reference (discovery rules, feature tiers, and limits).

efvibe auto-discovers the EF provider from **`PackageReference` entries on `-p`** (and project references). Connection strings from `-s` are not parsed to guess the provider. Any relational package matching `*.EntityFrameworkCore.*` is supported for DbContext construction, the LINQ REPL, and SQL translation when the provider exposes a standard `Use*` extension.

| Provider | EF package (typical) | Notes |
|----------|---------------------|--------|
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | Use Docker on macOS/Linux; Unix SqlClient from workspace `.deps.json`; local connection string normalization |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | `EXPLAIN` for `:plan`; naming convention customizers |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | `EXPLAIN QUERY PLAN` for `:plan`; good for local files |
| Oracle | `Oracle.EntityFrameworkCore` | Requires `Oracle.EntityFrameworkCore` in the workspace; `EXPLAIN PLAN FOR` for `:plan` |
| MySQL | `Pomelo.EntityFrameworkCore.MySql` or `MySql.EntityFrameworkCore` | Pomelo or Oracle provider; `EXPLAIN` for `:plan` |
| MariaDB | `MariaDB.EntityFrameworkCore` or Pomelo | `ConnectionStrings:MariaDb` supported; `EXPLAIN` for `:plan` |
| Firebird | `FirebirdSql.EntityFrameworkCore.Firebird` | Generic discovery; LINQ REPL and SQL translation; `:plan` not yet registered |
| Other relational EF packages | `*.EntityFrameworkCore.*` | Auto-discovered from `-p`; same **Sql** tier as Firebird unless capabilities are added |

Pass `--connection-string` (or rely on the startup project). Reference exactly one provider package on `-p`.

### Feature tiers

| Tier | What works |
|------|------------|
| **Sql** | DbContext construction, LINQ REPL, SQL translation (default for newly discovered providers) |
| **QueryPlan** | Above + `:plan` / EXPLAIN (SqlServer, Oracle, MySQL/MariaDB, …) |
| **Conventions** | Above + PostgreSQL/SQLite naming customizers |

`:dbinfo` shows the resolved EF provider package and feature tier for the active session.

### Connection string resolution (no `-cs`)

When you do not pass `--connection-string`, and the DbContext cannot be created via a design-time factory or parameterless constructor, `efvibe` loads credentials from the **startup project** in this order:

1. **User secrets** — `UserSecretsId` on the startup `.csproj`, then `~/.microsoft/usersecrets/<id>/secrets.json` (macOS/Linux).
2. **`appsettings.json` / `appsettings.Development.json`** — next to the startup project (and its `bin` output if present).

Preferred keys: `ConnectionStrings:DefaultConnection`, then `Postgres`, `Sqlite`, `MySql`, `MariaDb`, `Oracle`, `Database`, then any other `ConnectionStrings:*` entry. The database provider comes from the EF provider package on `-p`, not from the connection string text.

## macOS notes

- **SQL Server:** run the database in Docker; connect to `localhost,1433` (or your mapped port). Store `User Id=sa;Password=...` in the **API** user secrets or appsettings via `-s` — not integrated security.
- **SSPI errors:** `Cannot generate SSPI context` usually means config was read from the wrong project. Point `-s` at the API, not the persistence library.
- **Assembly loading:** library projects keep dependencies in the NuGet cache; `efvibe` reads `.deps.json` so EF Core and SqlClient resolve correctly on Unix.
- **Avoiding host conflicts:** the tool preloads workspace `System.Configuration.ConfigurationManager` (9.x) before SqlClient initializes, so it does not clash with older copies pulled in by optional Roslyn packages.

## Open source and commercial

- **Open source:** full CLI under [Apache 2.0](LICENSE).
- **Commercial:** optional paid tiers may be offered separately; the OSS CLI remains Apache 2.0.
