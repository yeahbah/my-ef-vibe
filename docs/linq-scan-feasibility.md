# Scanning project LINQ for slow queries

**Short answer:** Yes — efvibe supports a **static repo scan** via **`:scan lite`** in the REPL, plus **runtime** analysis on snippets you execute.

## What efvibe does now

| Approach | What you get |
|----------|----------------|
| **`:scan lite`** | Roslyn walk of EF project + referenced projects; file/line, rule, message, code preview, and **Fix** recommendations; saved to `myefvibe-scan-lite.json` under `-w`; review queue (`:next`, `:prev`, `:repeat`, `:end`) |
| **REPL + `:warnings`** | Same heuristics on the snippet you just ran (`AsEnumerable()`, `ToList()` without `Take()`, multiple `Include()`, etc.) |
| **Translated / executed SQL** | Real SQL for that query against your live DB |
| **`:plan`** | Execution plan for the last translated query |
| **`:benchmark N`** | Repeat timing for one snippet |

**`:scan lite`** is **static, whole-project** (no database). REPL evaluation is **runtime, one query at a time**.

## Rules detected by `:scan lite`

- `AsEnumerable()` / client-side evaluation risk (`client-eval`)
- `ToList()` / `ToArray()` without `Take` on large sets (`unbounded-materialize`)
- Multiple `Include()` / `ThenInclude()` — cartesian explosion risk (`cartesian`)
- `Take()` without `OrderBy` — unstable paging (`unordered-take`)
- `FromSqlRaw` / `ExecuteSqlRaw` (`raw-sql`)
- Query-like calls inside `foreach` / `for` — possible N+1 (`n-plus-one`)

Each finding panel includes remediation text (e.g. `AsSplitQuery()`, `OrderBy` before `Take`, batch loading for N+1).

## What a future `:scan deep` could add

### Expression resolution + SQL preview

For call sites Roslyn can fully resolve, optionally:

- Build a small harness and call `ToQueryString()` per site
- Report translated SQL next to the warning

Harder when queries depend on runtime values, DI, or repository indirection.

### CLI flag (optional)

```bash
efvibe -w ./session -p ... -s ... --scan-linq
```

Same engine as `:scan lite`, non-interactive report — not implemented today.

## Other ecosystem options

- EF Core **logging** (`LogLevel` for commands/duration)
- **Roslyn analyzers** / custom rules in your repo
- Tools like **EFCore.Visualizer**, **LINQPad** (manual per query)
- General static analyzers (Sonar, etc.) with some EF rules

## Practical recommendation

- **Repo-wide smell pass:** `:scan lite`, then step the queue and apply **Fix** hints at each site.
- **Exploratory / hot paths:** REPL on suspicious queries (`:plan`, `:benchmark`, SQL toggle).
- **What actually runs slow:** EF command logging + DB query stats in dev/staging.
