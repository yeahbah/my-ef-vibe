# LINQ scan rules reference

efvibe’s **`:scan lite`** and **`:scan deep`** (and headless `efvibe scan`) report findings using stable **rule ids**. Each finding includes a **message**, **Fix** guidance, and a **severity** level.

## Severity

Severity is assigned **only from the rule id** ([`LinqScanRuleCatalog`](../src/MyEfVibe/LinqScanRuleCatalog.cs)). It does **not** change when SQL translation fails, when translated SQL is available, or for any other runtime signal.

| Level | Value (CI / JSON) | Meaning |
|-------|-------------------|---------|
| Info | `info` | Informational — review SQL shape, not necessarily a defect |
| Warning | `warning` | Likely smell or footgun — worth reviewing |
| Error | `error` | Strong performance or correctness risk |
| Critical | `critical` | Severe risk (e.g. unbounded reads) |

**Order (low → high):** `info` < `warning` < `error` < `critical`

Use in CI:

```bash
efvibe scan lite -p path/to/Project.csproj --fail-on critical
efvibe scan lite -p ... --min-severity warning --fail-on error
```

`--fail-on` sets both the **CI exit gate** and the **report filter** (findings below that level are omitted from JSON and summary). Use `--min-severity` to report a different cutoff than the gate, e.g. `--min-severity warning --fail-on critical` shows warnings+ but only fails on critical.

## Scan modes

| Mode | Rules detected |
|------|----------------|
| **`:scan lite`** / `efvibe scan lite` | All heuristic rules below (`client-eval` through `n-plus-one`) |
| **`:scan deep`** / `efvibe scan deep` | Lite rules **plus** `query-site` (and translated SQL where the probe compiles) |

Heuristics are **static** (Roslyn text analysis on EF-related sources). They can produce false positives; use **`:dismiss`** / `--respect-dismissals` for known-safe patterns.

---

## Rules at a glance

| Rule id | Severity | Scan | Trigger (summary) |
|---------|----------|------|-------------------|
| [`unbounded-materialize`](#unbounded-materialize) | **critical** | lite, deep | `ToList` / `ToArray` (+ async) without `Take` |
| [`n-plus-one`](#n-plus-one) | **error** | lite, deep | Query-like calls inside `foreach` / `for` |
| [`cartesian`](#cartesian) | **warning** | lite, deep | Two or more `Include` / `ThenInclude` in one statement |
| [`raw-sql`](#raw-sql) | **warning** | lite, deep | `FromSqlRaw*` / `ExecuteSqlRaw*` with separate SQL parameters |
| [`raw-sql-unparameterized`](#raw-sql-unparameterized) | **error** | lite, deep | `FromSqlRaw*` / `ExecuteSqlRaw*` with only a SQL string (no params) |
| [`client-eval`](#client-eval) | **warning** | lite, deep | `AsEnumerable()` in the chain |
| [`unordered-take`](#unordered-take) | **warning** | lite, deep | `Take` / `TakeAsync` without `OrderBy` |
| [`query-site`](#query-site) | **info** | deep only | EF query call site with no other rule (SQL panel) |

Unknown rule ids default to **warning** severity.

---

## `unbounded-materialize`

| | |
|--|--|
| **Severity** | `critical` |
| **Scan** | lite, deep |
| **Message** | Materializes results without Take() — may load a large result set. |

### What triggers it

The statement contains `.ToList()`, `.ToListAsync()`, `.ToArray()`, or `.ToArrayAsync()` and does **not** contain `.Take(` or `.TakeAsync(` anywhere in the same snippet (whole statement text).

### Why it matters

Materializing an unbounded `IQueryable` pulls **all matching rows** into memory. With wide graphs (`Include` chains) this can cause high memory use, slow responses, and pressure on the change tracker.

### Fix

- Add **`Take` / `TakeAsync`** or real paging (`Skip` + `Take`) before materializing.
- Use **`AsNoTracking()`** for read-only lists.
- **`Select`** into a DTO to load only needed columns.

### Example

**Flagged:**

```csharp
return await DbContext.BusinessEntityContacts
    .Include(x => x.ContactType)
    .Include(x => x.Person)
    .Where(x => businessEntityIds.Contains(x.BusinessEntityId))
    .ToListAsync(cancellationToken);
```

**Safer patterns:**

```csharp
// Bounded batch
return await DbContext.BusinessEntityContacts
    .AsNoTracking()
    .Where(x => businessEntityIds.Contains(x.BusinessEntityId))
    .Take(500)
    .ToListAsync(cancellationToken);

// Or paging
var page = await DbContext.BusinessEntityContacts
    .AsNoTracking()
    .OrderBy(x => x.BusinessEntityId)
    .Skip(pageIndex * pageSize)
    .Take(pageSize)
    .ToListAsync(cancellationToken);
```

---

## `n-plus-one`

| | |
|--|--|
| **Severity** | `error` |
| **Scan** | lite, deep |
| **Message** | Query-like calls inside a loop — possible N+1 pattern. |

### What triggers it

A `foreach` or `for` loop body (as Roslyn prints it) looks query-like: contains `.Where(`, `.Include(`, `.ToList(`, `.First`, `.Single`, `.Count(`, `.Any(`, `db.`, `DbContext`, or `Set<`.

### Why it matters

Executing a database round-trip **per iteration** scales linearly with collection size and dominates latency under load.

### Fix

- **Eager-load** related data with `Include` / `ThenInclude`, or one query with `Where(id => ids.Contains(...))`.
- Build a **dictionary** from a single query; use the loop only over in-memory data.
- Combine multiple collection includes with **`AsSplitQuery()`** when appropriate.

### Example

**Flagged:**

```csharp
foreach (var orderId in orderIds)
{
    var lines = await DbContext.OrderLines
        .Where(l => l.OrderId == orderId)
        .ToListAsync(cancellationToken);
    // process lines
}
```

**Safer pattern:**

```csharp
var lines = await DbContext.OrderLines
    .AsNoTracking()
    .Where(l => orderIds.Contains(l.OrderId))
    .ToListAsync(cancellationToken);

var byOrder = lines.ToLookup(l => l.OrderId);
foreach (var orderId in orderIds)
{
    var batch = byOrder[orderId];
    // process batch
}
```

---

## `raw-sql`

| | |
|--|--|
| **Severity** | `warning` |
| **Scan** | lite, deep |
| **Message** | Uses parameterized raw SQL — verify indexing and execution plan. |

### What triggers it

The statement calls `FromSqlRaw(` or `ExecuteSqlRaw(` with **at least two arguments** (SQL text plus separate parameter values). `FromSqlInterpolated` / `ExecuteSqlInterpolated` are not flagged by these rules.

### Why it matters

Parameterized raw SQL is safer than a single string, but you still bypass LINQ translation — verify **indexes**, plans, and row shapes manually.

### Fix

- Review **execution plans** and indexes for predicates.
- Prefer LINQ when EF can express the query.
- Keep passing values as **parameters**, not embedded in the SQL string.

### Example

**Flagged (warning):**

```csharp
await DbContext.Database.ExecuteSqlRawAsync(
    "DELETE FROM Staging WHERE BatchId = {0}",
    batchId);
```

---

## `raw-sql-unparameterized`

| | |
|--|--|
| **Severity** | `error` |
| **Scan** | lite, deep |
| **Message** | Uses raw SQL without separate SQL parameters — injection and plan-cache risk. |

### What triggers it

The statement calls `FromSqlRaw(` or `ExecuteSqlRaw(` with **only one argument** (typically a single string or interpolated string). No trailing parameter arguments are detected.

### Why it matters

Building SQL in one string (concatenation, interpolation, or a variable) risks **SQL injection** and prevents consistent parameterization / plan reuse.

### Fix

- Pass **`{0}` / `@p0` placeholders** and values as additional arguments to `ExecuteSqlRaw` / `FromSqlRaw`.
- Or use **`ExecuteSqlInterpolated`** / **`FromSqlInterpolated`**.
- Never concatenate or interpolate user input into the SQL text for `*SqlRaw` overloads.

### Example

**Flagged (error):**

```csharp
var sql = $"SELECT * FROM Products WHERE Name = '{name}'";
await DbContext.Database.ExecuteSqlRawAsync(sql);

await DbContext.Products.FromSqlRaw("SELECT * FROM Products").ToListAsync();
```

**Safer patterns:**

```csharp
await DbContext.Database.ExecuteSqlRawAsync(
    "DELETE FROM Staging WHERE BatchId = {0}",
    batchId);

await DbContext.Database.ExecuteSqlInterpolatedAsync(
    $"DELETE FROM Staging WHERE BatchId = {batchId}");
```

---

## `cartesian`

| | |
|--|--|
| **Severity** | `warning` |
| **Scan** | lite, deep |
| **Message** | Multiple Include/ThenInclude calls (N) — watch for cartesian explosion. |

### What triggers it

The statement contains **two or more** occurrences of `.Include(` and/or `.ThenInclude(` (counted separately).

### Why it matters

Multiple collection includes in one SQL graph can produce a **wide Cartesian product** (duplicate parent rows), increasing payload size and materialization cost.

### Fix

- Use **`AsSplitQuery()`** so EF issues separate SQL statements per include branch.
- Reduce the graph: **`Select`** to a DTO, or load rare navigations with **`Entry` / `Collection.Load`**.
- Split into **multiple targeted queries** instead of one mega-graph.

### Example

**Flagged:**

```csharp
return await DbContext.Employees
    .Include(e => e.EmployeeDepartmentHistory)
        .ThenInclude(h => h.Department)
    .Include(e => e.EmployeeDepartmentHistory)
        .ThenInclude(h => h.Shift)
    .FirstOrDefaultAsync(e => e.BusinessEntityId == id, cancellationToken);
```

**Safer patterns:**

```csharp
return await DbContext.Employees
    .AsSplitQuery()
    .Include(e => e.EmployeeDepartmentHistory)
        .ThenInclude(h => h.Department)
    .Include(e => e.EmployeeDepartmentHistory)
        .ThenInclude(h => h.Shift)
    .FirstOrDefaultAsync(e => e.BusinessEntityId == id, cancellationToken);
```

---

## `client-eval`

| | |
|--|--|
| **Severity** | `warning` |
| **Scan** | lite, deep |
| **Message** | Uses AsEnumerable() — may force client-side evaluation. |

### What triggers it

The statement contains `AsEnumerable(`.

### Why it matters

After `AsEnumerable()`, subsequent operators run in **.NET**, not in the database — filters and projections may pull **more data than necessary** over the wire.

### Fix

- Keep **`Where` / `Select` / `OrderBy` / `Take`** on `IQueryable` until the query is fully shaped.
- Call **`AsEnumerable()`** only after the DB-backed part is done (if you need IEnumerable at all).
- Prefer **`ToListAsync()`** on the composed `IQueryable`.

### Example

**Flagged:**

```csharp
var query = DbContext.Products
    .AsEnumerable()
    .Where(p => SomeClientOnlyFilter(p))
    .ToList();
```

**Safer pattern:**

```csharp
var query = await DbContext.Products
    .Where(p => p.Discontinued == false) // translated to SQL
    .ToListAsync(cancellationToken);

var filtered = query.Where(SomeClientOnlyFilter).ToList();
```

---

## `unordered-take`

| | |
|--|--|
| **Severity** | `warning` |
| **Scan** | lite, deep |
| **Message** | Take() without OrderBy — row order is undefined. |

### What triggers it

The statement contains `.Take(` or `.TakeAsync(` and does **not** contain `OrderBy` (any casing as substring).

### Why it matters

Without an explicit order, **which rows** `Take` returns is undefined and can change between runs, breaking paging and “top N” semantics.

### Fix

- Add **`OrderBy` / `OrderByDescending`** before `Take`.
- Prefer an **indexed, stable key** (often the primary key) for paging.

### Example

**Flagged:**

```csharp
var latest = await DbContext.Orders.Take(10).ToListAsync(cancellationToken);
```

**Safer pattern:**

```csharp
var latest = await DbContext.Orders
    .OrderByDescending(o => o.OrderDate)
    .ThenBy(o => o.OrderId)
    .Take(10)
    .ToListAsync(cancellationToken);
```

---

## `query-site`

| | |
|--|--|
| **Severity** | `info` |
| **Scan** | **deep only** |
| **Message** | Queryable call site — translated SQL available. *or* Queryable call site — SQL translation failed. |

### What triggers it

During **deep scan**, an EF query **invocation** is found (e.g. `Include`, `ToListAsync`, `FirstOrDefaultAsync`) at a line that did **not** already produce another heuristic finding for that file/line. Deep scan then attempts `ToQueryString()` via a REPL probe.

- **Message (SQL ok):** translation succeeded — review the **Translated SQL** panel.
- **Message (SQL failed):** probe could not be compiled or translated — see **SQL** note on the finding. **Severity stays `info`** regardless.

### Why it matters

Useful for **auditing SQL shape** (filters, joins, row multiplication) even when no static rule fired. Not a defect by itself.

### Fix

- Compare translated SQL to intent.
- Paste the expression in the REPL, run **`:plan`**, or benchmark hot paths.
- If translation failed, fix is about the **probe** (parameters, unsupported syntax), not elevating severity.

### Example

A simple filtered query with no Include/Take issues may only appear as `query-site`:

```csharp
return await DbContext.Departments
    .FirstOrDefaultAsync(s => s.DepartmentId == id, cancellationToken);
```

Deep scan adds an **info** finding with generated SQL like `SELECT ... TOP(1) ... WHERE [DepartmentId] = @p0`.

---

## Finding identity and artifacts

| Field | Role |
|-------|------|
| **File** + **Line** + **Rule id** | Dismissal key (`:dismiss`, `myefvibe-scan-dismissals.json`) |
| **Severity** | From rule id only (stored in scan JSON v3 as `"severity": "critical"` etc.) |
| **Translated SQL** | Deep scan only; optional on the finding |
| **SqlTranslationNote** | Deep scan failure detail; does not affect severity |

Session files: `myefvibe-scan-lite.json`, `myefvibe-scan-deep.json` under the workspace project/DbContext folder.

## Related docs

- [linq-scan-feasibility.md](./linq-scan-feasibility.md) — how scanning works, limitations, shortcuts
- [features.md](../features.md) — REPL `:scan` commands and review queue
