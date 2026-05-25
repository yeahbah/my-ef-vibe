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

    fun runDbInfo(): CliResult =
        run(baseArgs() + listOf("--dbinfo-json", "--no-banner"), timeoutMs = 10 * 60_000)

    fun runTables(): CliResult =
        run(baseArgs() + listOf("--tables-json", "--no-banner"), timeoutMs = 10 * 60_000)

    fun runScan(mode: String): CliResult {
        val args = mutableListOf("scan", mode)
        args += baseArgs()
        args += listOf("--json", "--no-banner")
        if (settings.scanRespectDismissals) args += "--respect-dismissals"
        if (settings.scanMinSeverity.isNotBlank()) args += listOf("--min-severity", settings.scanMinSeverity.trim())
        return run(args, timeoutMs = 20 * 60_000)
    }

    private fun run(args: List<String>, timeoutMs: Int): CliResult {
        val invocation = resolveInvocation()
        val commandLine = GeneralCommandLine(invocation.command)
            .withParameters(invocation.prefixArgs + args)
            .withWorkDirectory(PathResolver.solutionDirectory(project).toFile())
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
