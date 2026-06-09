# Visual Studio Extension

The Visual Studio extension is a VSIX that hosts efvibe inside Visual Studio 2022 with the same workflow as the Rider plugin: a unified **My EF Vibe** tool window, `efvibe serve` daemon support with CLI fallback, and Tools/editor commands for REPL, scans, and model exploration.

efvibe works with **most EF Core relational providers** — SQL Server, PostgreSQL, SQLite, Oracle, MySQL/MariaDB, Firebird, and other packages auto-discovered from the EF project. See [database-providers.md](database-providers.md).

## Build

The project lives at `visualstudio-extension/MyEfVibe.VisualStudio.csproj`.

```powershell
dotnet restore visualstudio-extension/MyEfVibe.VisualStudio.csproj
dotnet build visualstudio-extension/MyEfVibe.VisualStudio.csproj
```

Full VSIX packaging and experimental-instance debugging should be done on Windows with Visual Studio 2022 and the Visual Studio extension development workload installed.

## Configure

Open **Tools > Options > My EF Vibe > General** and set:

| Setting | Maps to |
|---------|---------|
| EF project | `efvibe -p` |
| Startup project | `efvibe -s` |
| DbContext | `efvibe -c` |
| Workspace root | `efvibe -w` |
| Tool path | Explicit `efvibe` or `myefvibe` executable |
| Target framework | `efvibe --framework` |
| Connection string | `efvibe --connection-string` |

Relative paths resolve from the active solution directory. The extension also supports `$(SolutionDir)` and `${workspaceFolder}` placeholders.

## Commands

Commands are available from **Tools > My EF Vibe** and the editor context menu:

- **Start REPL** — external terminal with the resolved `efvibe` command.
- **Refresh Connection** — restarts the `efvibe serve` daemon session.
- **Run Selection** / **Run Selection with Plan** — evaluate selection or current line (`--with-plan` for the latter).
- **Run with My EF Vibe** / **Run with My EF Vibe :plan** — same from the code editor context menu.
- **Show DbInfo** / **Show Tables** — load the Model tab.
- **Scan Lite** / **Scan Deep** — populate Scan Review.
- **Check Prerequisites** — runs `--about-json`.

If the submenu is not visible, look for the fallback command **Tools > My EF Vibe - Check Prerequisites**. If neither the submenu nor the fallback command is visible, restart Visual Studio after installing the VSIX and open a solution. Confirm the extension is enabled under **Extensions > Manage Extensions > Installed**, then check whether the options page appears at **Tools > Options > My EF Vibe > General**. If the options page exists but the menu is still missing, uninstall the old VSIX, close all Visual Studio instances, delete `%LocalAppData%\Microsoft\VisualStudio\17.0_*\ComponentModelCache` and `%LocalAppData%\Microsoft\VisualStudio\17.0_*\Extensions\extensions.en-US.cache` when present, reinstall the current VSIX, and start Visual Studio again so the command table cache is rebuilt.

## Tool window (View > Other Windows > My EF Vibe)

Single docked window aligned with Rider:

| Tab | Purpose |
|-----|---------|
| **Result** | Grid + export CSV/JSON |
| **SQL** / **Plan** / **Messages** | Executed SQL, query plan, metrics and warnings |
| **Session** | Resolved settings, REPL/serve commands, daemon status |
| **Model** | DbInfo, tables, Run Count/Sample/Describe for selected DbSet |
| **Scan Review** | Previous/Next, Go to code, Note, Dismiss |
| **History** | Recent evaluations |
| **Notebook** | Open/save `.efvibe-notebook`, Run All |

Toolbar: **Run**, **Run Plan**, **Scan Lite**, **Scan Deep**, **Copy Tab**. Status bar shows `Ready (daemon)` vs `Ready (CLI)` when applicable.

## Windows Verification

1. Build or install the `efvibe` CLI.
2. Open an EF Core solution in Visual Studio 2022.
3. Configure EF project, startup project, and DbContext in **Tools > Options > My EF Vibe**.
4. Run **Tools > My EF Vibe > Check Prerequisites**.
5. Run **Start REPL** and verify `:dbinfo`.
6. Select a LINQ expression and run **Run Selection**.
7. Run **Scan Lite** and **Scan Deep** and confirm the scan review tool window populates.
