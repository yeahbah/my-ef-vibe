# VS Code extension — v2 plan (suggestions)

For **v2**, treat the work as **depth and polish**, not re-building the engine. The CLI and `efvibe serve` stay the center; v2 makes the editor feel native, fast, and team-ready.

## Context

**v1 status:** Phases 0–3 in [vscode-extension-plan.md](./vscode-extension-plan.md) are shipped (REPL, Run Selection, daemon-backed workspace commands, scan + Scan Review, session sidebar, model tree, notebooks, completions, CodeLens, Marketplace publish).

**v2 theme:** Configure once, work in one panel, scan and explore as a team workflow — fast and boring-reliable.

**Positioning:**

- **v1:** “Run EF LINQ from the editor with SQL and scan.”
- **v2:** “Configure once, work in one panel, scan and explore as a team workflow — fast and boring-reliable.”

---

## 1. Onboarding and multi-project (highest leverage)

Most friction today is configuration, not missing commands.

- **First-open wizard:** detect `.csproj` pairs, pick `DbContext`, write `.vscode/settings.json`, run Check Prerequisites, optional warmup daemon.
- **Profiles per workspace folder:** named presets (Dev API + `AppDbContext`, Tests + `TestDbContext`) with a quick picker in the status bar.
- **“Wrong project” guardrails:** clear errors when cwd/settings don’t match the file you’re editing.

*Why v2:* Cuts time-to-first-query from “read docs” to “click through once.”

---

## 2. One primary surface (Rider/VS parity)

v1 spreads UX across webviews, sidebar, and terminal. v2 can add a **single My EF Vibe view** (Activity Bar or editor area):

- Expression editor, results grid, SQL, plan, messages in one place.
- Keep existing commands; the panel becomes the default for `resultDestination: panel`.
- Toolbar: Run, Run Plan, Scan, Copy SQL, Export — same mental model as Rider.

*Why v2:* Fewer “where did my SQL go?” moments; easier screenshots and docs.

---

## 3. Protocol and performance (daemon v2)

You already serialize requests; v2 hardens the pipe:

- **Request `id`** on every serve message (correlation, logging, future cancel).
- **`ping` / health** in status bar; auto-restart on crash with one notification.
- **Warmup on folder open** (optional setting): daemon ready before first Run Selection.
- **`--no-build` when artifacts are fresh** (CLI + extension agree on fingerprint).

*Why v2:* Feels instant and trustworthy under real multitasking.

---

## 4. Scan as a workflow, not just a report

Scan works; v2 makes it actionable in the editor:

- **CodeLens on query sites** (“N findings · Run with efvibe”) — still called out as deferred in the v1 plan.
- **Quick fixes / code actions** where rules have safe mechanical fixes (e.g. add `AsNoTracking`, split `Include`).
- **Scan diff:** compare lite/deep or run vs baseline; highlight new/regressed findings.
- **Problems panel profile:** dedicated `efvibe` diagnostic source that doesn’t fight C# LSP (filter by rule/severity).

*Why v2:* Unique value vs “run scan in terminal and read JSON.”

---

## 5. Notebooks as a first-class exploration medium

MVP exists; v2 makes notebooks worth adopting:

- Command cells: `:scan`, dismiss/note, `:export`, maybe `:plan`.
- **Parameter cells** (declare `Guid id = …` once, reference in LINQ cells).
- **Persisted outputs** + share/export (HTML or `.efvibe-notebook` in repo for team playbooks).
- Same daemon queue as the rest of the extension.

*Why v2:* Good story for “explore this bug” docs and onboarding workshops.

---

## 6. Smarter editor intelligence (LSP v2)

Today: subprocess completions + minimal language server. v2:

- **One long-lived path** for completions/diagnostics (daemon or `efvibe language-server` backed by serve).
- **Hover:** last SQL for this line, or scan rule doc + translated SQL.
- **Inline eval** (optional): evaluate small `db.*` expression from hover peek (read-only guard).

*Why v2:* `db.` completion feels like EF tooling, not a bolt-on.

---

## 7. Team and CI hooks

- **Scan in CI** template (GitHub Action snippet in repo) wired to same JSON the extension uses.
- **Shared dismissals/notes** workflow doc (what to commit, what stays local).
- **Export “session report”**: last N evals + SQL + metrics as markdown for PRs.

*Why v2:* Positions My EF Vibe as a team hygiene tool, not a solo REPL toy.

---

## 8. Safety and environments

- **Connection profiles** (dev/staging) using user secrets / env — never write secrets into settings.
- **Row limits / timeouts** in panel with clear UI (“truncated at 500 rows”).
- **PII / destructive SQL warnings** in guard (extend what you have for `SaveChanges`).

*Why v2:* Enterprise-adjacent without becoming a full DBA suite.

---

## Suggested v2 scope

| Tier | Ship in v2.0 | Defer to v2.x |
|------|----------------|----------------|
| **Must** | Setup wizard, multi-root profiles, daemon health + warmup, unified tool panel | — |
| **Should** | Notebook command cells + parameters, scan CodeLens, request `id` on serve | Scan quick fixes |
| **Could** | Scan diff/baseline, LSP hover + unified completion, CI Action template | Inline eval peek |
| **Won’t** | Rewrite CLI in TS, full ORM designer, AI “fix my query” as core | — |

---

## Priority recommendation

If picking a direction, prioritize first:

1. **Onboarding / multi-root** — improves every feature without new CLI concepts.
2. **Unified panel** — Rider/VS parity and clearer UX.

---

## Related docs

- [vscode-extension-plan.md](./vscode-extension-plan.md) — v1 phased roadmap (complete)
- [efvibe-daemon-and-vscode.md](./efvibe-daemon-and-vscode.md) — serve protocol
- [vscode-extension/README.md](../vscode-extension/README.md) — install and settings
- [rider-extension-plan.md](./rider-extension-plan.md) — Rider plugin roadmap
