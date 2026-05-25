# My EF Vibe Rider Extension

JetBrains Rider plugin that shells out to the `efvibe` CLI. The plugin owns settings, actions, terminal/tool-window UX, and output display; `efvibe` remains the evaluation and scan engine.

## Build

Install Gradle 9 or add a wrapper, then run from this directory:

```bash
gradle buildPlugin
```

For a Rider sandbox:

```bash
gradle runIde
```

The generated plugin ZIP is written under `build/distributions/`.

## Configure

Open **Settings -> Languages & Frameworks -> My EF Vibe** and set:

| Setting | CLI flag |
|---------|----------|
| Workspace root | `-w` |
| EF project | `-p` |
| Startup project | `-s` |
| DbContext | `-c` |
| Connection string | `--connection-string` |
| Provider | `--provider` |
| efvibe executable | direct executable path |
| .NET framework | `--framework` / local tool framework |

Paths may be absolute, solution-relative, or use `${workspaceFolder}` / `$(SolutionDir)`.

## Features

- **Tools -> My EF Vibe -> Start REPL** opens a Rider terminal tab and runs the resolved `efvibe` command.
- **Run Selection** and **Run Selection with Plan** send the selected LINQ expression to `efvibe -e --format json`.
- **Show DbInfo** and **Show Tables** call `--dbinfo-json` and `--tables-json`.
- **Scan Lite** and **Scan Deep** call `efvibe scan lite|deep --json`.
- The **My EF Vibe** tool window includes an editable expression box, Run / Run :plan buttons, and raw JSON/text output.

This is a Phase 0/1 MVP scaffold. Deeper Rider integrations such as Problems/inspections and schema trees can build on the same CLI runner.
