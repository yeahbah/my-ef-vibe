# Visual Studio Extension

The Visual Studio extension is an MVP VSIX that hosts efvibe inside Visual Studio 2022. It is intentionally thin: the extension resolves solution-relative settings and shells out to the existing `efvibe` CLI for DbContext hosting, query evaluation, dbinfo, table metadata, and scan results.

## Build

The project lives at `src/MyEfVibe.VisualStudio/MyEfVibe.VisualStudio.csproj`.

```powershell
dotnet restore src/MyEfVibe.VisualStudio/MyEfVibe.VisualStudio.csproj
dotnet build src/MyEfVibe.VisualStudio/MyEfVibe.VisualStudio.csproj
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
| Provider | `efvibe --provider` |

Relative paths resolve from the active solution directory. The extension also supports `$(SolutionDir)` and `${workspaceFolder}` placeholders.

## Commands

Commands are available from **Tools > My EF Vibe**:

- **Start REPL** opens an external terminal with the resolved `efvibe -p ... -s ... -c ...` command.
- **Run Selection** evaluates the selected C# expression with `efvibe -e --format json --no-banner`.
- **Show DbInfo** runs `--dbinfo-json`.
- **Show Tables** runs `--tables-json`.
- **Describe Entity...** runs `--describe-json <entity>`.
- **Scan Lite** runs `efvibe scan lite --json --no-banner`.
- **Scan Deep** runs `efvibe scan deep --json --no-banner`.
- **Check Prerequisites** runs `--about-json` and writes details to the **My EF Vibe** Output pane.

If the submenu is not visible, look for the fallback command **Tools > My EF Vibe - Check Prerequisites**. If neither the submenu nor the fallback command is visible, restart Visual Studio after installing the VSIX and open a solution. Confirm the extension is enabled under **Extensions > Manage Extensions > Installed**, then check whether the options page appears at **Tools > Options > My EF Vibe > General**. If the options page exists but the menu is still missing, uninstall the old VSIX, close all Visual Studio instances, reinstall the current VSIX, and start Visual Studio again so the command table cache is rebuilt.

## Tool Windows

- **My EF Vibe Result** shows query rows plus SQL, metrics, warnings, and query plans. It includes **Run** and **Run :plan** buttons for rerunning the current expression.
- **My EF Vibe Scan Review** shows scan summary and findings parsed from the CLI JSON output.

## Windows Verification

1. Build or install the `efvibe` CLI.
2. Open an EF Core solution in Visual Studio 2022.
3. Configure EF project, startup project, and DbContext in **Tools > Options > My EF Vibe**.
4. Run **Tools > My EF Vibe > Check Prerequisites**.
5. Run **Start REPL** and verify `:dbinfo`.
6. Select a LINQ expression and run **Run Selection**.
7. Run **Scan Lite** and **Scan Deep** and confirm the scan review tool window populates.
