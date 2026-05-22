# efvibe VS Code extension (Phase 1, v0.2.1)

Run the [efvibe](https://github.com/yeahbah/my-ef-vibe) EF Core LINQ REPL from VS Code with workspace settings for `-p`, `-s`, and `-c`. Phase 1 adds **editor-integrated queries** with JSON results and a **split-tab** result + SQL panel.

## Prerequisites

- [.NET SDK](https://dotnet.net/download)
- `efvibe` built from this repo or installed as a [global/local tool](https://www.nuget.org/packages/efvibe)

Phase 1 requires CLI support for `efvibe -e --format json --no-banner` (included when you build `MyEfVibe` from this repository).

## Commands

| Command | Description |
|---------|-------------|
| **efvibe: Start REPL** | Opens an integrated terminal running `efvibe` with your settings |
| **efvibe: Run Selection** | Evaluates the selected LINQ (`Shift+Alt+E` in C#) |
| **efvibe: Run Line at Cursor** | Evaluates the current line |
| **efvibe: Run Statement at Cursor** | Expands and evaluates the statement around the cursor |
| **efvibe: Run Expression** | Selection, or prompt if nothing selected |
| **efvibe: Show Last SQL** | Opens SQL from the last JSON evaluation |
| **efvibe: Generate REPL Task** | Writes/updates `efvibe: Start REPL` in `.vscode/tasks.json` |
| **efvibe: Check Prerequisites** | Verifies `dotnet` and `efvibe` on PATH |
| **efvibe: Refresh Status** | Updates the status bar via `efvibe --about-json` |

Right-click in a `.cs` file for **Run Selection** / **Run Line at Cursor**.

## Settings

Configure in `.vscode/settings.json`:

```json
{
  "efvibe.project": "${workspaceFolder}/src/MyApp.Data/MyApp.Data.csproj",
  "efvibe.startupProject": "${workspaceFolder}/src/MyApp.Api/MyApp.Api.csproj",
  "efvibe.context": "AppDbContext",
  "efvibe.dotnetFramework": "net8.0",
  "efvibe.dbLog": true,
  "efvibe.resultDestination": "panel",
  "efvibe.toolPath": "${workspaceFolder}/../my-ef-vibe/src/MyEfVibe/bin/Debug/net10.0/myefvibe"
}
```

| Setting | Description |
|---------|-------------|
| `efvibe.resultDestination` | `panel` (split webview with result + SQL), `output`, or `terminal` |
| `efvibe.dbLog` | When `true` (default), CLI database logging is on; when `false`, passes `--no-dblog` |
| `efvibe.toolPath` | Optional path to `myefvibe` / `efvibe` (use a local build for latest CLI features) |

Deprecated: `efvibe.showSql` (renamed to `efvibe.dbLog`; the CLI flag is `--dblog`, not `--sql`).

### Running repository code from the editor

Select a full handler query (including `await`, `DbContext`, and `cancellationToken`) and use **Run Selection**. The CLI adapts it the same way as deep-scan probes: `DbContext` → `db`, parameters stubbed, async terminals converted to sync. See [features.md](../features.md#repository-snippets-from-your-codebase) for details and limits.

## Install (required)

This extension is **not published** to the Marketplace yet. See **[INSTALL.md](INSTALL.md)** to install from a VSIX or run via F5.

```bash
cd vscode-extension && npm install && npm run package
```

Then **Extensions → … → Install from VSIX** and reload the window.

## Development

```bash
cd vscode-extension
npm install
npm run compile
```

Open the **repo root** and press **F5** → **Run Extension**.

Point `efvibe.toolPath` at your local build if needed:

```json
{
  "efvibe.toolPath": "${workspaceFolder}/src/MyEfVibe/bin/Debug/net10.0/myefvibe"
}
```

## Phase roadmap

- **Phase 0:** Terminal REPL, settings, prerequisites, status bar
- **Phase 1 (this release):** Run selection, JSON evaluation, SQL panel, REPL task generator
- **Phase 2+:** Scan diagnostics, schema sidebar — see [docs/vscode-extension-plan.md](../docs/vscode-extension-plan.md)
