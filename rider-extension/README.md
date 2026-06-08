# My EF Vibe Rider Extension

JetBrains Rider plugin that shells out to the `efvibe` CLI. The plugin owns settings, actions, terminal/tool-window UX, and output display; `efvibe` remains the evaluation and scan engine.

efvibe supports **most EF Core relational providers** — SQL Server, PostgreSQL, SQLite, Oracle, MySQL/MariaDB, Firebird, and other packages discovered from your EF project. See [docs/database-providers.md](../docs/database-providers.md).

For a step-by-step setup and usage walkthrough, see [docs/rider-extension.md](../docs/rider-extension.md).

## Install

Install from the [JetBrains Marketplace](https://plugins.jetbrains.com/plugin/31961-my-ef-vibe): **Settings → Plugins → Marketplace**, search **`My EF Vibe`**.

For offline or pre-release builds, download `rider-efvibe-<version>.zip` from [GitHub Releases](https://github.com/yeahbah/my-ef-vibe/releases) and use **Install Plugin from Disk…**.

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

Publish to the [JetBrains Marketplace](https://plugins.jetbrains.com/plugin/31961-my-ef-vibe) (CI uses the `PUBLISH_TOKEN` repository secret):

```bash
export PUBLISH_TOKEN='your-marketplace-token'
gradle publishPlugin
```

## Configure

Open **Settings -> Languages & Frameworks -> My EF Vibe** for the current Rider project and set:

| Setting | CLI flag |
|---------|----------|
| Workspace root | `-w` |
| EF project | `-p` |
| Startup project | `-s` |
| DbContext | `-c` |
| Connection string | `--connection-string` |
| Provider | `--provider` — alias (`npgsql`, `sqlserver`, …) or EF package id; leave empty to discover from `-p` |
| efvibe executable | direct executable path |
| .NET framework | `--framework` / local tool framework |

Paths may be absolute, solution-relative, or use `${workspaceFolder}` / `$(SolutionDir)`.

Settings are stored per Rider project in the project workspace state, not globally. Depending on the project format, Rider usually writes them to one of:

- `<project>/.idea/workspace.xml`
- `<solution>/.idea/.idea.<SolutionName>/.idea/workspace.xml`

Look for the `MyEfVibeSettings` component. This is user-local workspace state and normally should not be committed.

## Features

- **Tools -> My EF Vibe -> Start REPL** opens a Rider terminal tab and runs the resolved `efvibe` command.
- **Run Selection** and **Run Selection with Plan** send the selected LINQ expression to `efvibe -e --format json`.
- **Show DbInfo** and **Show Tables** call `--dbinfo-json` and `--tables-json`.
- **Scan Lite** and **Scan Deep** call `efvibe scan lite|deep --json`.
- The **My EF Vibe** tool window includes a resizable query editor, Run / Run Plan buttons, result rows, SQL, query plans, messages, session settings, model actions, scan review, history, and a notebook-style multi-cell runner.
- The **Result** tab includes Export CSV and Export JSON actions for the last successful run.
- The **Model** tab includes Db Info, Tables, Run Count, Run Sample, and Describe actions for the selected DbSet.
- The **Scan Review** tab supports finding navigation, Go to code, notes, and dismissals.
- Run Selection prefers `efvibe serve` for faster repeated evaluations and falls back to one-shot CLI execution.
- The notebook tab can open/save `.efvibe-notebook` JSON files and run cells separated by `---`.

This is a Phase 0/1 Rider experience. Deeper integrations such as Problems/inspections can build on the same CLI runner and scan JSON parser.
