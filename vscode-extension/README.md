# My EF Vibe (`efvibe`)

Run **[efvibe](https://myefvibe.com/)** inside VS Code — evaluate EF Core LINQ against your real `DbContext`, see translated and executed SQL, scan the repo, and open an interactive REPL. Maps workspace settings to CLI `-p`, `-s`, and `-c`.

**Docs:** [myefvibe.com/docs/vscode.html](https://myefvibe.com/docs/vscode.html) · **CLI:** [NuGet `efvibe`](https://www.nuget.org/packages/efvibe) · **Source:** [github.com/yeahbah/my-ef-vibe](https://github.com/yeahbah/my-ef-vibe)

## Screenshots

**Run Selection** — evaluate LINQ in place; result panel with rows and captured SQL (`Shift+Alt+E`):

![Run Selection with result panel and SQL](https://raw.githubusercontent.com/yeahbah/my-ef-vibe/main/vscode-extension/images/vscode1.png)

**Scan Review** — deep-scan findings carousel with SQL, query plan, note, and dismiss:

![Scan Review carousel for deep scan findings](https://raw.githubusercontent.com/yeahbah/my-ef-vibe/main/vscode-extension/images/vscode-scan-review.png)

## Prerequisites

1. [.NET SDK](https://dotnet.net/download) (same major version as your EF project)
2. **`efvibe` CLI** on PATH: `dotnet tool install -g efvibe`
3. Workspace settings: `efvibe.project`, `efvibe.context` (and usually `efvibe.startupProject`)

**Run Selection** uses `efvibe serve` by default (`efvibe.useDaemon`: true) so build + DbContext stay warm.

## Quick start

```json
{
  "efvibe.project": "${workspaceFolder}/src/MyApp.Data/MyApp.Data.csproj",
  "efvibe.startupProject": "${workspaceFolder}/src/MyApp.Api/MyApp.Api.csproj",
  "efvibe.context": "AppDbContext",
  "efvibe.dotnetFramework": "net8.0"
}
```

Command Palette → **efvibe: Check Prerequisites** → **efvibe: Run Selection** on a `db.*` query.

## Commands

| Command | Description |
|---------|-------------|
| **efvibe: Start REPL** | Integrated terminal running `efvibe` with your settings |
| **efvibe: Run Selection** | Evaluate selected LINQ (`Shift+Alt+E` in C#) |
| **efvibe: Run Line at Cursor** | Evaluate the current line |
| **efvibe: Run Statement at Cursor** | Expand and evaluate the statement around the cursor |
| **efvibe: Run Expression** | Selection, or prompt if nothing selected |
| **efvibe: Show Last SQL** | SQL from the last JSON evaluation |
| **efvibe: Export Last Result** | Save last panel result as CSV or JSON |
| **efvibe: Scan Workspace** / **Scan Workspace (Deep)** | Project scan + **Scan Review** carousel |
| **efvibe: Open Scan Review** | Browse findings (Previous / Next, note, dismiss) |
| **efvibe: Send to REPL** | Send selection to the REPL terminal (`Ctrl/Cmd+Shift+Enter`) |

**efvibe Session** sidebar: DbSets, **Scan Deep**, **Run Query**, **Start REPL**, session artifacts. **CodeLens:** **Run with efvibe** on query statements.

## Settings

| Setting | Description |
|---------|-------------|
| `efvibe.project` / `efvibe.startupProject` / `efvibe.context` | EF project, config host, DbContext type name |
| `efvibe.resultDestination` | `panel` (default), `output`, or `terminal` |
| `efvibe.useDaemon` | `true` (default): Run Selection uses `efvibe serve` |
| `efvibe.dbLog` | Show executed SQL (default `true`) |
| `efvibe.scan.mode` | `lite` or `deep` for workspace scan |
| `efvibe.toolPath` | Optional path to a local `efvibe` / `myefvibe` build |

Full list in the extension settings UI. Repository snippets (`await`, `DbContext`, `*Async`) are adapted automatically — see [features.md](https://github.com/yeahbah/my-ef-vibe/blob/main/features.md#repository-snippets-from-your-codebase).

## More

- Offline / pre-release: [GitHub Releases](https://github.com/yeahbah/my-ef-vibe/releases) (`.vsix`) or [INSTALL.md](https://github.com/yeahbah/my-ef-vibe/blob/main/vscode-extension/INSTALL.md)
- Build from source: open the [my-ef-vibe](https://github.com/yeahbah/my-ef-vibe) repo and F5 **Run Extension**
