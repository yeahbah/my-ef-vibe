# Rider Plugin User Guide

This guide walks through installing and configuring the My EF Vibe Rider plugin for a single Rider project. The plugin uses your existing `efvibe` CLI configuration model: EF project, optional startup project, DbContext, and workspace root.

> Screenshot placeholder: Rider plugin installed in **Settings > Plugins**.

## 1. Install the plugin

### From JetBrains Marketplace (recommended)

1. In Rider, open **Settings → Plugins → Marketplace**.
2. Search for **My EF Vibe**, or open [My EF Vibe on JetBrains Marketplace](https://plugins.jetbrains.com/plugin/31961-my-ef-vibe) and click **Install to IDE**.
3. Restart Rider when prompted.

### From disk (offline or pre-release)

1. Download `rider-efvibe-<version>.zip` from [GitHub Releases](https://github.com/yeahbah/my-ef-vibe/releases), or build locally (below).
2. In Rider, open **Settings → Plugins**.
3. Click the gear icon and choose **Install Plugin from Disk...**.
4. Select the ZIP file.
5. Restart Rider when prompted.

For local development builds, the ZIP is generated at:

```bash
rider-extension/build/distributions/rider-efvibe-<version>.zip
```

## 2. Open your solution

Open the .NET solution that contains your EF Core project. My EF Vibe resolves relative paths from the Rider project or solution directory.

> Screenshot placeholder: Rider opened on a solution with the My EF Vibe tool window available on the right.

## 3. Configure project settings

Open **Settings > Languages & Frameworks > My EF Vibe**.

Set the fields that match your EF Core solution:

| Setting | What to enter |
|---------|---------------|
| **EF project** | The `.csproj` that contains or references your `DbContext`, for example `src/MyApp.Persistence/MyApp.Persistence.csproj`. |
| **Startup project** | The app project with `appsettings*.json` or user secrets, for example `src/MyApp.Api/MyApp.Api.csproj`. |
| **DbContext** | The context type name, for example `AppDbContext`. |
| **Workspace root** | Optional session/output root. Leave blank to use the CLI default. |
| **Connection string** | Optional override when the startup project does not provide one. |
| **Provider** | Optional override: alias (`sqlserver`, `npgsql`, `sqlite`, …) or EF package id (for example `FirebirdSql.EntityFrameworkCore.Firebird`). Leave empty to discover the provider from `-p` `PackageReference` entries. Required when **Connection string** is set. |
| **efvibe executable** | Optional explicit path to `efvibe` or `myefvibe`. Leave blank to use a local tool or PATH. |
| **.NET framework** | Optional target framework, for example `net8.0` or `net10.0`. |

Paths can be absolute, solution-relative, or use `${workspaceFolder}` / `$(SolutionDir)`.

Example:

```text
EF project: src/AdventureWorks.Infrastructure.Persistence/AdventureWorks.Infrastructure.Persistence.csproj
Startup project: src/AdventureWorks.API/AdventureWorks.API.csproj
DbContext: AdventureWorksDbContext
```

Click **Apply** or **OK**.

> Screenshot placeholder: My EF Vibe settings page with EF project, startup project, and DbContext filled in.

## 4. Understand where settings are saved

Rider plugin settings are per project, not global. Rider stores them in the project workspace state, usually one of:

```text
<project>/.idea/workspace.xml
<solution>/.idea/.idea.<SolutionName>/.idea/workspace.xml
```

Look for the `MyEfVibeSettings` component. This is user-local workspace state and normally should not be committed.

## 5. Open the tool window

Open **View > Tool Windows > My EF Vibe**. The tool window defaults to the right side of Rider.

The tool window includes:

- A resizable query editor.
- **Run** and **Run Plan** buttons.
- Result, SQL, Plan, Messages, Session, Model, Scan Review, History, and Notebook tabs.

> Screenshot placeholder: My EF Vibe tool window with query editor and Result tab.

## 6. Run a LINQ query

Type a query using `db` as the current `DbContext`:

```csharp
db.Products.Take(10)
```

Click **Run**. The **Result** tab shows rows returned by the query. Use **Export CSV** or **Export JSON** in the Result tab to export the last successful run.

Click **Run Plan** to execute the same query and immediately open the **Plan** tab.

> Screenshot placeholder: Result tab showing rows and export buttons.

## 7. Inspect the model

Open the **Model** tab.

Use:

- **Db Info** to load provider, connection, and DbContext details.
- **Tables** to load DbSets and entity types.
- **Run Count** to count rows for the selected DbSet.
- **Run Sample** to run `Take(10)` for the selected DbSet.
- **Describe** to show entity metadata.

> Screenshot placeholder: Model tab showing DbSets and entity actions.

## 8. Review scan findings

Use **Scan Lite** or **Scan Deep** from the top toolbar.

Scan results appear in the **Scan Review** tab. From there you can:

- Move between findings with **Previous** and **Next**.
- Open the source with **Go to code**.
- Add a finding note with **Note**.
- Dismiss a finding with **Dismiss**.

> Screenshot placeholder: Scan Review tab with colored severity rows and finding details.

## 9. Use notebooks

Open the **Notebook** tab to run multiple cells. Separate cells with `---`:

```csharp
db.Products.Take(10)

---
:dbinfo

---
db.SalesOrderHeaders.Count()
```

Use **Open** and **Save** for `.efvibe-notebook` files. Click **Run All** to execute every cell.

> Screenshot placeholder: Notebook tab with multiple cells and output.

## Troubleshooting

If `DbContext` resolution fails, check:

- The **Startup project** points to the app that contains `appsettings*.json` or user secrets.
- The EF project and startup project paths are correct relative to the solution.
- The `efvibe` CLI is installed globally, restored as a local tool, or configured with an explicit executable path.
- The selected target framework matches your project when multiple frameworks are present.

If the tool window is missing, restart Rider after installing the plugin and check **Settings > Plugins** to confirm **My EF Vibe** is enabled.
