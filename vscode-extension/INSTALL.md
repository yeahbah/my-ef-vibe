# Installing the efvibe VS Code extension

Install guide on the site: [myefvibe.com/docs/vscode.html](https://myefvibe.com/docs/vscode.html). Quick start: [myefvibe.com/docs/getting-started.html](https://myefvibe.com/docs/getting-started.html).

The extension is **not on the Marketplace yet** (optional CI publish when `VSCE_PAT` / `OVSX_PAT` are configured). Install from a [GitHub Release](https://github.com/yeahbah/my-ef-vibe/releases) `.vsix` or build locally from this folder.

## Option A — Install from VSIX (recommended)

**From a release:** download `vscode-efvibe-<version>.vsix` from [GitHub Releases](https://github.com/yeahbah/my-ef-vibe/releases).

**From source** (repository root):

```bash
cd vscode-extension
npm install
npm run package
```

Then in VS Code or Cursor:

1. Open the **Extensions** view (`Cmd+Shift+X` / `Ctrl+Shift+X`)
2. Click the **`...`** menu → **Install from VSIX…**
3. Choose the `.vsix` file (for example `vscode-efvibe-0.5.0.vsix`)
4. **Reload** the window when prompted

Or from a terminal (one line — do not break the path onto a new line):

```bash
code --install-extension "/path/to/my-ef-vibe/vscode-extension/vscode-efvibe-0.5.0.vsix"
```

Cursor:

```bash
cursor --install-extension "/path/to/my-ef-vibe/vscode-extension/vscode-efvibe-0.3.1.vsix"
```

### Updating an existing VSIX install

Repeat **Option A**: `npm run package`, then **Install from VSIX…** again with the new file, and **Reload Window**. You do not need to uninstall first.

After install, open the **Command Palette** (`Cmd+Shift+P`) and run **efvibe: Start REPL**.

## Option B — Extension Development Host (contributors)

1. Open the **my-ef-vibe** repo root in VS Code
2. Run **Run Extension** from Run and Debug (F5)
3. In the **new** `[Extension Development Host]` window, open your .NET solution and use the commands

Commands only exist in the window where the extension is loaded (main VS Code after VSIX install, or the dev host after F5).

## Verify installation

Command Palette → type `efvibe`. You should see:

- efvibe: Start REPL
- efvibe: Run Selection
- efvibe: Run Line at Cursor
- efvibe: Run Statement at Cursor
- efvibe: Run Expression
- efvibe: Show Last SQL
- efvibe: Generate REPL Task
- efvibe: Check Prerequisites
- efvibe: Refresh Status
- efvibe: Scan Workspace
- efvibe: Scan Workspace (Deep)
- efvibe: Open Scan Review
- efvibe: Refresh Scan Diagnostics
- efvibe: Dismiss Scan Finding
- efvibe: Export Last Result

If you see **no matching commands**, the extension is not installed in that window — use Option A or B above.

## Configure before Start REPL

`.vscode/settings.json` in your solution:

```json
{
  "efvibe.project": "${workspaceFolder}/path/to/Your.Data.csproj",
  "efvibe.startupProject": "${workspaceFolder}/path/to/Your.Api.csproj",
  "efvibe.context": "YourDbContext",
  "efvibe.dotnetFramework": "net8.0",
  "efvibe.dbLog": true,
  "efvibe.resultDestination": "panel",
  "efvibe.useDaemon": true,
  "efvibe.scan.openReviewOnScan": true,
  "efvibe.scan.problemsPanel": false
}
```

Use the exact `DbContext` type name (spelling matters). Point `efvibe.toolPath` at a local `myefvibe` build when using **serve**, **scan**, and repository-snippet evaluation from the latest CLI in this repo.

See [README.md](README.md) for editor workflows and [features.md](../features.md) for CLI flags (`--dblog`, `--format json`, repository snippet adaptation).
