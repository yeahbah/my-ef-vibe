# Installing the efvibe VS Code extension

The extension is **not on the Marketplace yet**. Until it is published, install it locally from this folder.

## Option A — Install from VSIX (recommended)

From the repository root:

```bash
cd vscode-extension
npm install
npm run package
```

Then in VS Code or Cursor:

1. Open the **Extensions** view (`Cmd+Shift+X` / `Ctrl+Shift+X`)
2. Click the **`...`** menu → **Install from VSIX…**
3. Choose `vscode-extension/vscode-efvibe-0.1.0.vsix` (version in the filename may differ)
4. **Reload** the window when prompted

Or from a terminal:

```bash
code --install-extension vscode-extension/vscode-efvibe-0.1.0.vsix
# Cursor:
cursor --install-extension vscode-extension/vscode-efvibe-0.1.0.vsix
```

After install, open the **Command Palette** (`Cmd+Shift+P`) and run **efvibe: Start REPL**.

## Option B — Extension Development Host (contributors)

1. Open the **my-ef-vibe** repo root in VS Code
2. Run **efvibe Extension** from Run and Debug (F5)
3. In the **new** `[Extension Development Host]` window, open your .NET solution and use the commands

Commands only exist in the window where the extension is loaded (main VS Code after VSIX install, or the dev host after F5).

## Verify installation

Command Palette → type `efvibe`. You should see:

- efvibe: Start REPL
- efvibe: Run Expression
- efvibe: Check Prerequisites
- efvibe: Refresh Status

If you see **no matching commands**, the extension is not installed in that window — use Option A or B above.

## Configure before Start REPL

`.vscode/settings.json` in your solution:

```json
{
  "efvibe.project": "${workspaceFolder}/path/to/Your.Data.csproj",
  "efvibe.startupProject": "${workspaceFolder}/path/to/Your.Api.csproj",
  "efvibe.context": "YourDbContext",
  "efvibe.dotnetFramework": "net8.0"
}
```
