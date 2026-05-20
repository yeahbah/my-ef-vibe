# efvibe VS Code extension (Phase 0)

Run the [efvibe](https://github.com/yeahbah/my-ef-vibe) EF Core LINQ REPL from VS Code with workspace settings for `-p`, `-s`, and `-c`.

## Prerequisites

- [.NET SDK](https://dotnet.net/download)
- `efvibe` as a [global tool](https://www.nuget.org/packages/efvibe) or local tool in `dotnet-tools.json`

## Commands

| Command | Description |
|---------|-------------|
| **efvibe: Start REPL** | Opens an integrated terminal running `efvibe` with your settings |
| **efvibe: Run Expression** | Runs `efvibe -e` with the selection or a prompt |
| **efvibe: Check Prerequisites** | Verifies `dotnet` and `efvibe` on PATH |
| **efvibe: Refresh Status** | Updates the status bar via `efvibe --about-json` |

## Settings

Configure in `.vscode/settings.json`:

```json
{
  "efvibe.project": "${workspaceFolder}/src/MyApp.Data/MyApp.Data.csproj",
  "efvibe.startupProject": "${workspaceFolder}/src/MyApp.Api/MyApp.Api.csproj",
  "efvibe.context": "AppDbContext",
  "efvibe.dotnetFramework": "net8.0"
}
```

See the VS Code settings UI for all `efvibe.*` keys.

## Install (required)

This extension is **not published** to the Marketplace yet. See **[INSTALL.md](INSTALL.md)** to install from a VSIX or run via F5.

Quick install:

```bash
cd vscode-extension && npm install && npm run package
```

Then **Extensions → … → Install from VSIX** and pick the generated `.vsix` file. Reload the window.

## Development

```bash
cd vscode-extension
npm install
npm run compile
```

Open the **repo root** (not only `vscode-extension/`) and press **F5** → **efvibe Extension** to launch an Extension Development Host.

## Phase roadmap

Phase 0 (this release): terminal REPL, settings, prerequisites, status bar.

Later phases: JSON expression results, scan diagnostics, editor run-selection — see [docs/vscode-extension-plan.md](../docs/vscode-extension-plan.md).
