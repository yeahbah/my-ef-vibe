package com.yeahbah.efvibe.services

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.CapturingProcessHandler
import com.intellij.execution.process.ProcessOutput
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import java.nio.charset.StandardCharsets
import kotlin.io.path.exists

class CliRunner(private val project: Project) {
    private val settings: EfvibeSettingsState get() = project.service<EfvibeSettingsService>().state

    fun buildReplCommandLine(): String {
        val invocation = resolveInvocation()
        return listOf(invocation.command)
            .plus(invocation.prefixArgs)
            .plus(baseArgs())
            .joinToString(" ") { quote(it) }
    }

    fun runAboutJson(): CliResult =
        run(listOf("--about-json", "--no-banner"), timeoutMs = 30_000)

    fun runExpression(expression: String, withPlan: Boolean): CliResult {
        val args = baseArgs().toMutableList()
        args += listOf("-e", expression, "--format", "json", "--no-banner")
        if (withPlan) args += "--with-plan"
        return run(args, timeoutMs = 10 * 60_000)
    }

    fun runExpressionPayload(expression: String, withPlan: Boolean, preferDaemon: Boolean = true): ExpressionRunResult {
        if (preferDaemon) {
            val daemonFailure = runCatching {
                return EfvibeDaemonClient.getInstance(project).runExpression(expression, withPlan)
            }.exceptionOrNull()

            if (daemonFailure != null) {
                val fallback = runExpression(expression, withPlan)
                return ExpressionRunResult(
                    result = fallback,
                    payload = EfvibeJsonParser.parseEvaluation(fallback.stdout),
                    usedDaemon = false,
                    daemonError = daemonFailure.message ?: daemonFailure.toString(),
                )
            }
        }

        val result = runExpression(expression, withPlan)
        return ExpressionRunResult(result, EfvibeJsonParser.parseEvaluation(result.stdout))
    }

    fun runDbInfo(): CliResult =
        run(baseArgs() + listOf("--dbinfo-json", "--no-banner"), timeoutMs = 10 * 60_000)

    fun runDbInfoPayload(preferDaemon: Boolean = true): DaemonJsonResult<DbInfoPayload?> =
        runJsonWithDaemon(
            preferDaemon = preferDaemon,
            daemonRequest = { EfvibeDaemonClient.getInstance(project).runDbInfo() },
            fallback = { runDbInfo() },
            parse = { EfvibeJsonParser.parseDbInfo(it) },
        )

    fun runTables(): CliResult =
        run(baseArgs() + listOf("--tables-json", "--no-banner"), timeoutMs = 10 * 60_000)

    fun runTablesPayload(preferDaemon: Boolean = true): DaemonJsonResult<TablesPayload?> =
        runJsonWithDaemon(
            preferDaemon = preferDaemon,
            daemonRequest = { EfvibeDaemonClient.getInstance(project).runTables() },
            fallback = { runTables() },
            parse = { EfvibeJsonParser.parseTables(it) },
        )

    fun runDescribe(entityName: String, preferDaemon: Boolean = true): DaemonRunResult =
        runWithDaemon(
            preferDaemon = preferDaemon,
            daemonRequest = { EfvibeDaemonClient.getInstance(project).runDescribe(entityName) },
            fallback = { runDescribeCli(entityName) },
        )

    private fun runDescribeCli(entityName: String): CliResult =
        run(baseArgs() + listOf("--describe-json", entityName, "--no-banner"), timeoutMs = 10 * 60_000)

    fun runScan(mode: String): CliResult {
        val args = mutableListOf("scan", mode)
        args += baseArgs()
        args += listOf("--json", "--no-banner")
        if (settings.scanRespectDismissals) args += "--respect-dismissals"
        if (settings.scanMinSeverity.isNotBlank()) args += listOf("--min-severity", settings.scanMinSeverity.trim())
        return run(args, timeoutMs = 20 * 60_000)
    }

    fun runScanPayload(mode: String, preferDaemon: Boolean = true): DaemonJsonResult<ScanPayload?> =
        runJsonWithDaemon(
            preferDaemon = preferDaemon,
            daemonRequest = {
                EfvibeDaemonClient.getInstance(project).runScan(
                    mode,
                    settings.scanRespectDismissals,
                    settings.scanMinSeverity,
                )
            },
            fallback = { runScan(mode) },
            parse = { EfvibeJsonParser.parseScan(it) },
        )

    fun runScanNote(finding: ScanFinding, note: String): CliResult =
        run(scanFindingArgs("note", finding) + listOf("--text", note), timeoutMs = 30_000)

    fun runScanDismiss(finding: ScanFinding, note: String?): CliResult {
        val args = scanFindingArgs("dismiss", finding).toMutableList()
        if (!note.isNullOrBlank()) {
            args += listOf("--note", note.trim())
        }
        return run(args, timeoutMs = 30_000)
    }

    fun buildServeSpec(): CliCommandSpec =
        buildSpec(listOf("serve") + baseArgs())

    fun buildSpec(args: List<String>): CliCommandSpec {
        val invocation = resolveInvocation()
        return CliCommandSpec(
            command = invocation.command,
            args = invocation.prefixArgs + args,
            workingDirectory = PathResolver.solutionDirectory(project).toFile(),
        )
    }

    private inline fun <T> runJsonWithDaemon(
        preferDaemon: Boolean,
        crossinline daemonRequest: () -> String,
        fallback: () -> CliResult,
        crossinline parse: (String) -> T?,
    ): DaemonJsonResult<T> {
        if (preferDaemon) {
            val failure = runCatching {
                val stdout = daemonRequest()
                return DaemonJsonResult(
                    result = CliResult(0, stdout, ""),
                    payload = parse(stdout),
                    usedDaemon = true,
                )
            }.exceptionOrNull()

            if (failure != null) {
                val cli = fallback()
                return DaemonJsonResult(
                    result = cli,
                    payload = parse(cli.stdout),
                    usedDaemon = false,
                    daemonError = failure.message ?: failure.toString(),
                )
            }
        }

        val cli = fallback()
        return DaemonJsonResult(cli, parse(cli.stdout))
    }

    private inline fun runWithDaemon(
        preferDaemon: Boolean,
        crossinline daemonRequest: () -> String,
        fallback: () -> CliResult,
    ): DaemonRunResult {
        if (preferDaemon) {
            val failure = runCatching {
                val stdout = daemonRequest()
                return DaemonRunResult(CliResult(0, stdout, ""), usedDaemon = true)
            }.exceptionOrNull()

            if (failure != null) {
                val cli = fallback()
                return DaemonRunResult(
                    result = cli,
                    usedDaemon = false,
                    daemonError = failure.message ?: failure.toString(),
                )
            }
        }

        return DaemonRunResult(fallback())
    }

    private fun run(args: List<String>, timeoutMs: Int): CliResult {
        val spec = buildSpec(args)
        val commandLine = GeneralCommandLine(spec.command)
            .withParameters(spec.args)
            .withWorkDirectory(spec.workingDirectory)
            .withCharset(StandardCharsets.UTF_8)

        val output: ProcessOutput = CapturingProcessHandler(commandLine).runProcess(timeoutMs)

        return CliResult(output.exitCode, output.stdout, output.stderr)
    }

    private fun resolveInvocation(): CliInvocation {
        val toolPath = PathResolver.resolve(settings.toolPath, project)
        if (toolPath.isNotBlank() && java.nio.file.Path.of(toolPath).exists()) {
            return CliInvocation(toolPath)
        }

        if (findDotnetToolsManifest() != null) {
            val framework = settings.dotnetFramework.trim()
            val prefixArgs = if (framework.isBlank()) listOf("efvibe") else listOf("efvibe", "-f", framework)
            return CliInvocation("dotnet", prefixArgs)
        }

        return CliInvocation("efvibe")
    }

    private fun baseArgs(): List<String> = buildList {
        addOption("-w", PathResolver.resolve(settings.workspaceRoot, project))
        addOption("-p", PathResolver.resolve(settings.project, project))
        addOption("-s", PathResolver.resolve(settings.startupProject, project))
        addOption("-c", settings.context.trim())
        addOption("--connection-string", settings.connectionString.trim())
        addOption("--provider", settings.provider.trim())
        if (!settings.dbLog) add("--no-dblog")
        addOption("--framework", settings.dotnetFramework.trim())
    }

    private fun MutableList<String>.addOption(name: String, value: String) {
        if (value.isBlank()) return
        add(name)
        add(value)
    }

    private fun scanFindingArgs(command: String, finding: ScanFinding): List<String> = buildList {
        add("scan")
        add(command)
        addOption("-w", PathResolver.resolve(settings.workspaceRoot, project))
        addOption("-p", PathResolver.resolve(settings.project, project))
        addOption("-c", settings.context.trim())
        addOption("--file", finding.filePath)
        addOption("--line", finding.line.toString())
        addOption("--rule", finding.ruleId)
        addOption("--code", finding.code)
    }

    private fun findDotnetToolsManifest(): java.nio.file.Path? {
        var current: java.nio.file.Path? = PathResolver.solutionDirectory(project)
        repeat(12) {
            val candidates = listOfNotNull(
                current?.resolve("dotnet-tools.json"),
                current?.resolve(".config")?.resolve("dotnet-tools.json"),
            )
            candidates.firstOrNull { it.exists() }?.let { return it }
            current = current?.parent
        }
        return null
    }

    private fun quote(value: String): String =
        if (value.any { it.isWhitespace() || it == '"' }) "\"${value.replace("\"", "\\\"")}\"" else value
}
