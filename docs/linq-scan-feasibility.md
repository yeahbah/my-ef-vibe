# Scanning project LINQ for slow queries

**Short answer:** Yes — efvibe supports **`:scan lite`** (static heuristics) and **`:scan deep`** (heuristics + `ToQueryString()` per call site) in the REPL, plus runtime analysis on snippets you execute.

## What efvibe does now

| Approach | What you get |
|----------|----------------|
| **`:scan lite`** | Roslyn walk of EF project + referenced projects; file/line, rule, message, code preview, and **Fix** recommendations; review queue with keyboard shortcuts |
| **`:scan deep`** | Lite findings plus translated SQL where the live `db` context can evaluate the expression; extra `query-site` entries when SQL translates but no heuristic fires |

Session artifacts live under `~/.efvibe/<DbContextName>/` (or `%APPDATA%\efvibe\<DbContextName>\`): `myefvibe-scan-lite.json`, `myefvibe-scan-deep.json`, `myefvibe-scan-dismissals.json`, `myefvibe-scan-notes.json`.
| **REPL + `:warnings`** | Same heuristics on the snippet you just ran |
| **Translated / executed SQL** | Real SQL for that query against your live DB |
| **`:plan`** | Execution plan for the last translated query |
| **`:benchmark N`** | Repeat timing for one snippet |

**`:scan lite`** does not use the database. **`:scan deep`** uses the connected `DbContext` to call `ToQueryString()` — it does not execute the full materializing query.

## Rules detected

- `AsEnumerable()` / client-side evaluation risk (`client-eval`)
- `ToList()` / `ToArray()` without `Take` on large sets (`unbounded-materialize`)
- Multiple `Include()` / `ThenInclude()` — cartesian explosion risk (`cartesian`)
- `Take()` without `OrderBy` — unstable paging (`unordered-take`)
- `FromSqlRaw` / `ExecuteSqlRaw` (`raw-sql`)
- Query-like calls inside `foreach` / `for` — possible N+1 (`n-plus-one`)
- `query-site` (deep only) — call site with translated SQL and no heuristic hit

## Deep scan expression adaptation

Before calling `ToQueryString()`, efvibe normalizes each call site:

- `DbContext` / `_context` property access → `db`
- `if (await db.Set.AnyAsync(ct))` → condition inside parentheses, then strip `await` and `AnyAsync(...)` to leave the `IQueryable`
- Terminal methods with arguments (`AnyAsync`, `ToListAsync`, `CountAsync`, …) — not only parameterless `AnyAsync()`

## Deep scan limitations

- Repository / method code is rewritten (`DbContext` → `db`); unusual patterns may not translate.
- Parameters, locals, and DI-only state from the original method are not available in the REPL harness (e.g. `Where(x => x.Id == id)` when `id` is a method argument).
- Raw SQL and client-evaluated chains may not produce `IQueryable` probes.

## Scan review shortcuts (empty prompt)

| Input | Action |
|-------|--------|
| **→** / `:next` | Next finding |
| **←** / `:prev` | Previous finding |
| **Del** / `:dismiss` | Dismiss (skip on future scans) |
| `:note` … | Save a note (shown in yellow on next scan) |
| `:repeat` | Restart queue |
| `:end` | Exit review |

## Practical recommendation

- **Repo-wide smell pass:** `:scan lite`, then step the queue and apply **Fix** hints; dismiss noise with **Del** or `:dismiss`.
- **SQL shape review:** `:scan deep` on the same project when `db` is connected.
- **What actually runs slow:** REPL on hot paths (`:plan`, `:benchmark`) plus EF command logging in dev/staging.
