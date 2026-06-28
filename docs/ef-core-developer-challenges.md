# EF Core developer challenges (code-first, database-first, and shared pain)

A practical map of what EF Core developers struggle with today — split by approach and where tools like efvibe fit.

---

## Challenges all EF developers share

These hit whether you started from C# classes or an existing database.

### 1. “What SQL does this LINQ actually run?”

LINQ reads like C#, but execution is deferred. Until you run it, it is hard to know:

- Whether it translates to SQL or **client-evaluates** in memory
- How many round-trips (classic **N+1**)
- Whether `Include` chains explode into **cartesian** joins
- Whether `Take` without `OrderBy` is non-deterministic

Most teams discover this in production logs, a profiler, or a bug report — not while writing the query. That is exactly the gap efvibe’s REPL, SQL pane, and scan rules target.

### 2. The persistence + API split

Modern solutions almost always split:

| Project | Holds |
|---------|--------|
| `*.Persistence` / `*.Infrastructure` (`-p`) | `DbContext`, entities, migrations |
| `*.API` / host (`-s`) | `appsettings`, user secrets, connection strings |

Getting `-p` and `-s` wrong means wrong credentials, wrong provider, or a context that builds in the IDE but not in the REPL. This wiring friction is routine, not exotic.

### 3. Provider and naming mismatches

EF abstracts providers, but reality leaks through:

- PostgreSQL `snake_case` vs C# `PascalCase`
- Oracle **uppercase** identifiers (`PRODUCTID` vs `ProductId`)
- Type mismatches (e.g. `smallint` in DB, `bool` in entity)
- SQL Server on macOS/Linux (Docker, auth, `Encrypt` flags)

`EFCore.NamingConventions` and Fluent mappings help, but someone still has to **discover** the mismatch. Generic DB clients like DBeaver show the catalog; they do not show how *your* `DbContext` maps to it.

### 4. Performance footguns are easy to write

Common LINQ smells (many of which efvibe scan already flags):

| Pattern | Risk |
|---------|------|
| `ToList()` without `Take` | Loads entire table into memory |
| Query inside `foreach` | N+1 |
| Multiple `Include`s | Cartesian product |
| `AsEnumerable()` mid-chain | Client-side evaluation |
| `FromSqlRaw` without parameters | SQL injection |
| `Take` without `OrderBy` | Unstable paging |

Static analysis in the IDE catches syntax errors, not “this compiles but kills the database.”

### 5. Hard to explore the database *through the model*

Developers need to answer questions like:

- “What does `db.Orders` actually map to?”
- “Can I filter on this navigation?”
- “What happens if I add this `Where`?”

Options today are fragmented: run the app, write a unit test, use SSMS/pgAdmin/DBeaver for raw SQL, or use LINQPad with a manually configured context. None of those consistently use **your real project build, secrets, and conventions**.

### 6. EF Core moves fast

Version upgrades (8 → 9 → 10), provider package bumps, and breaking changes in:

- Compiled models
- JSON columns
- Complex types
- Bulk operations
- Interceptors / `ExecuteUpdate` / `ExecuteDelete`

Teams carry migration debt and “works on my machine” provider differences.

---

## Code-first challenges

Code-first is the default for greenfield .NET apps. Pain points:

### Model ↔ database drift

You change entities and generate migrations, but:

- **Drift** appears when someone edits the DB manually (hotfix in prod, DBA script)
- **Migration conflicts** in git when two devs add migrations in parallel
- **Destructive migrations** (`DropColumn`) slip through review
- **Squashing** history is painful on long-lived products

There is no single “source of truth” unless the team is disciplined about migrations-only workflow.

### Migrations are a team process problem

- Who applies migrations in CI/CD?
- How do you test migrations against a prod-like schema?
- Zero-downtime deploys with additive-then-subtractive migrations
- Rollback strategy (EF migrations are forward-only by default)
- Seeding vs migrations (`HasData` vs SQL scripts)

### Mapping complexity grows faster than entities

Early code-first is simple. Later you accumulate:

- Owned types, value objects, enums as strings
- TPH / TPT / TPC inheritance (easy to get wrong)
- Global query filters (`IsDeleted`, multi-tenancy)
- Shadow properties, converters, computed columns
- Concurrency tokens

The C# model becomes a **domain document**; the database becomes harder to reason about from LINQ alone.

### Refactoring entities is expensive

Rename a property → new migration → coordinate deploy → fix raw SQL, reports, stored procs, external integrations. Code-first optimizes for C# ergonomics; renames are still a cross-layer event.

### “It works in tests” with InMemory / SQLite

`UseInMemoryDatabase` or SQLite in tests **does not** catch:

- Provider-specific SQL
- Translation limits
- Transaction behavior
- Type mapping differences

Code-first teams often get a false sense of security until they hit PostgreSQL or SQL Server in staging.

---

## Database-first challenges

Database-first (reverse engineer / scaffold from existing schema) is common for brownfield, enterprise, and legacy systems.

### Scaffold output is not the final model

`dotnet ef dbcontext scaffold` gives you:

- Verbose Fluent API or attributes
- Names derived from DB conventions (often ugly in C#)
- No domain semantics — `TblCustOrdHdr` becomes an entity name

Teams immediately hand-edit, which means **the next scaffold overwrites your work** unless you treat scaffold as one-time bootstrap.

### Legacy schema is EF-unfriendly

Real databases have:

- Missing or incorrect foreign keys
- Composite keys, link tables without surrogate IDs
- `smallint` flags, `char(1)` enums, nullable everything
- Views and stored procedures as the real API
- Triggers that EF does not model
- Schemas owned by DBAs — developers cannot change tables to suit EF

You spend time on **mapping glue**, not features.

### Naming and casing wars

This is huge in database-first:

| Database | Typical style | C# style |
|----------|---------------|----------|
| PostgreSQL | `product_id`, `production.product` | `ProductId`, `Product` |
| Oracle | `PRODUCTID`, `PRODUCTION.PRODUCT` | `ProductId` |
| SQL Server | Mixed — `ProductID` vs `ProductId` | `ProductId` |

EF must bridge that gap with explicit column names, schemas, and converters. A raw SQL client shows `product_id`; your entity says `ProductId` — connecting the two is manual mental work.

### Stored procedures and views dominate

Many database-first systems do not use LINQ for hot paths. EF becomes:

- A thin layer for CRUD
- Or a migration tool you barely use
- Or “we scaffolded once and never again”

Developers still need LINQ for new features, but the **mental model is split** between sprocs and `DbSet`s.

### Keeping in sync when the DB changes

DBA publishes script → dev re-scaffolds or hand-updates entities → drift until someone runs a comparison. There is no great built-in “diff database vs model” story in everyday EF tooling.

---

## Hybrid / real-world situations

Most mature teams are neither pure code-first nor pure database-first:

| Situation | Challenge |
|-----------|-----------|
| Legacy DB + new tables via migrations | Two workflows, one `DbContext` |
| Multiple `DbContext`s | Bounded contexts, read/write split, confusion over which context owns what |
| Microservices | Each service owns schema; shared DB anti-pattern still exists |
| Read replicas | Routing, lag, `AsNoTracking` patterns |
| Multi-tenant | Global filters, connection-per-tenant, schema-per-tenant |
| Raw SQL islands | `FromSqlRaw`, Dapper alongside EF — scan and reasoning break down |

---

## How this connects to MyEFvibe Studio positioning

The challenges above explain why “DBeaver + LINQPad, but EF-only” is a coherent product idea:

| Developer pain | DBeaver helps | LINQPad helps | EF-native Studio helps |
|----------------|---------------|---------------|------------------------|
| Browse schema | ✅ raw catalog | ⚠️ driver-dependent | ✅ DbSets + EF metadata |
| Try a query fast | ✅ SQL | ✅ scratchpad | ✅ LINQ against real `db` |
| See generated SQL | ✅ if you wrote SQL | ⚠️ varies | ✅ always `ToQueryString()` + executed SQL |
| Catch N+1 / client eval | ❌ | ❌ | ✅ scan |
| Match *your* project conventions | ❌ | ❌ | ✅ build + naming probes |
| Multi-project / multi-context | ✅ connections | ⚠️ per file | ✅ workspace model |
| Code-first migration workflow | ❌ | ❌ | ⚠️ out of scope (use EF CLI / IDE) |
| Database-first scaffold loop | ⚠️ DDL view | ❌ | ⚠️ partial (explore + map, not scaffold) |

Studio does not replace **migrations**, **scaffolding**, or **DBA tools**. It targets the daily loop: *understand the model, write LINQ, see SQL, catch smells, compare environments* — the work that sits between “entity class” and “production incident.”

---

## Short summary

**Everyone:** opaque SQL, performance footguns, provider quirks, persistence/API wiring, hard to explore through the real `DbContext`.

**Code-first:** migration drift and team workflow, mapping complexity, refactor cost, misleading test doubles.

**Database-first:** scaffold friction, legacy schema mapping, naming/casing, sprocs/views, staying in sync when the DB changes.

**Hybrid:** the hardest case — and where an EF-specific client plus scratchpad (not a generic SQL IDE) is most valuable.

---

## See also

- [efvibe-studio-plan.md](efvibe-studio-plan.md) — Problem statement, product DNA, and phased delivery for MyEFvibe Studio
- [features.md](../features.md) — What the efvibe CLI and editor plugins ship today
- [linq-scan-rules.md](linq-scan-rules.md) — Scan rules reference (N+1, client eval, cartesian, etc.)
