package com.yeahbah.efvibe.services

import com.google.gson.JsonParser
import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.OSProcessHandler
import com.intellij.execution.process.ProcessEvent
import com.intellij.execution.process.ProcessListener
import com.intellij.execution.process.ProcessOutputTypes
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Key
import java.nio.charset.StandardCharsets
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

@Service(Service.Level.PROJECT)
class EfvibeDaemonClient(private val project: Project) {
    private val lifecycleLock = ReentrantLock()
    private var daemon: DaemonState? = null
    private var sessionKey: String? = null

    fun isReady(): Boolean =
        lifecycleLock.withLock {
            daemon?.takeIf { !it.handler.isProcessTerminated && it.ready } != null
        }

    fun warmup() {
        lifecycleLock.withLock {
            ensureStartedLocked()
        }
    }

    fun runExpression(expression: String, withPlan: Boolean): ExpressionRunResult {
        val line = runCommand(
            """{"type":"eval","expression":${jsonString(expression)},"withPlan":$withPlan}""",
            COMMAND_TIMEOUT_MINUTES,
        )
        val payload = EfvibeJsonParser.parseEvaluation(line)
        val exitCode = if (payload?.success == true) 0 else 20
        return ExpressionRunResult(CliResult(exitCode, line, ""), payload, usedDaemon = true)
    }

    fun runDbInfo(): String =
        runCommand("""{"type":"dbinfo"}""")

    fun runTables(): String =
        runCommand("""{"type":"tables"}""")

    fun runDescribe(entity: String): String =
        runCommand("""{"type":"describe","entity":${jsonString(entity)}}""")

    fun runScan(mode: String, respectDismissals: Boolean, minSeverity: String): String {
        val minSeverityField = if (minSeverity.isBlank()) {
            ""
        } else {
            ""","minSeverity":${jsonString(minSeverity.trim())}"""
        }
        return runCommand(
            """{"type":"scan","mode":${jsonString(mode)},"respectDismissals":$respectDismissals$minSeverityField}""",
            SCAN_TIMEOUT_MINUTES,
        )
    }

    private fun runCommand(requestJson: String, timeoutMinutes: Long = COMMAND_TIMEOUT_MINUTES): String =
        lifecycleLock.withLock {
            val state = ensureStartedLocked()
            val input = state.handler.processInput
                ?: throw IllegalStateException("efvibe daemon stdin is not available.")
            input.write("$requestJson\n".toByteArray(StandardCharsets.UTF_8))
            input.flush()

            val line = state.waitForLine(timeoutMinutes, TimeUnit.MINUTES)
                ?: throw IllegalStateException("efvibe daemon timed out waiting for a response.")
            when (parseMessageType(line)) {
                "error" -> throw IllegalStateException(parseMessageText(line) ?: "efvibe daemon error.")
                else -> line
            }
        }

    fun invalidate() {
        lifecycleLock.withLock {
            daemon?.handler?.destroyProcess()
            daemon = null
            sessionKey = null
        }
    }

    private fun ensureStartedLocked(): DaemonState {
        val key = buildSessionKey()
        daemon?.takeIf { !it.handler.isProcessTerminated && sessionKey == key && it.ready }?.let { return it }

        daemon?.handler?.destroyProcess()
        daemon = null
        sessionKey = key

        val spec = CliRunner(project).buildServeSpec()
        val commandLine = GeneralCommandLine(spec.command)
            .withParameters(spec.args)
            .withWorkDirectory(spec.workingDirectory)
            .withCharset(StandardCharsets.UTF_8)
            .withRedirectErrorStream(false)

        val handler = OSProcessHandler(commandLine)
        val state = DaemonState(handler)
        daemon = state
        handler.addProcessListener(state)
        handler.startNotify()

        waitForReadyLocked(state)
        state.ready = true
        return state
    }

    private fun waitForReadyLocked(state: DaemonState) {
        val deadlineNanos = System.nanoTime() + TimeUnit.MINUTES.toNanos(READY_TIMEOUT_MINUTES)
        while (System.nanoTime() < deadlineNanos) {
            val remainingMs = TimeUnit.NANOSECONDS.toMillis(deadlineNanos - System.nanoTime()).coerceAtLeast(1)
            val line = state.waitForLine(remainingMs, TimeUnit.MILLISECONDS)
                ?: break

            when (parseMessageType(line)) {
                "ready" -> return
                "error" -> {
                    invalidateLocked()
                    throw IllegalStateException(parseMessageText(line) ?: "efvibe serve failed to start.")
                }
            }
        }

        invalidateLocked()
        val details = state.stderrSummary().ifBlank { "No handshake response received." }
        throw IllegalStateException("efvibe serve timed out during workspace load.$details")
    }

    private fun invalidateLocked() {
        daemon?.handler?.destroyProcess()
        daemon = null
        sessionKey = null
    }

    private fun buildSessionKey(): String {
        val settings = project.service<EfvibeSettingsService>().state
        return listOf(
            PathResolver.solutionDirectory(project).toString(),
            PathResolver.resolve(settings.workspaceRoot, project),
            PathResolver.resolve(settings.project, project),
            PathResolver.resolve(settings.startupProject, project),
            settings.context.trim(),
            settings.connectionString.trim(),
            settings.provider.trim(),
            settings.toolPath.trim(),
            settings.dotnetFramework.trim(),
            settings.dbLog.toString(),
        ).joinToString("|")
    }

    private class DaemonState(val handler: OSProcessHandler) : ProcessListener {
        private val lines = ArrayBlockingQueue<String>(64)
        private val buffer = StringBuilder()
        private val stderr = StringBuilder()
        var ready: Boolean = false

        override fun onTextAvailable(event: ProcessEvent, outputType: Key<*>) {
            when (outputType) {
                ProcessOutputTypes.STDOUT -> appendStdout(event.text)
                ProcessOutputTypes.STDERR -> stderr.append(event.text)
            }
        }

        private fun appendStdout(chunk: String) {
            buffer.append(chunk)
            while (true) {
                val index = buffer.indexOf("\n")
                if (index < 0) break
                val line = buffer.substring(0, index).trim()
                buffer.delete(0, index + 1)
                if (line.isNotEmpty()) {
                    lines.offer(line)
                }
            }
        }

        fun waitForLine(timeout: Long, unit: TimeUnit): String? =
            lines.poll(timeout, unit)

        fun stderrSummary(): String {
            val text = stderr.toString().trim()
            if (text.isBlank()) return ""
            return "${System.lineSeparator()}stderr:${System.lineSeparator()}${text.take(4000)}"
        }
    }

    companion object {
        private const val READY_TIMEOUT_MINUTES = 10L
        private const val COMMAND_TIMEOUT_MINUTES = 10L
        private const val SCAN_TIMEOUT_MINUTES = 20L

        fun getInstance(project: Project): EfvibeDaemonClient =
            project.service()

        private fun parseMessageType(line: String): String? =
            runCatching {
                JsonParser.parseString(line).asJsonObject.get("type")?.asString
            }.getOrNull()

        private fun parseMessageText(line: String): String? =
            runCatching {
                JsonParser.parseString(line).asJsonObject.get("message")?.asString
            }.getOrNull()
    }
}

private fun jsonString(value: String): String =
    buildString {
        append('"')
        for (char in value) {
            when (char) {
                '\\' -> append("\\\\")
                '"' -> append("\\\"")
                '\n' -> append("\\n")
                '\r' -> append("\\r")
                '\t' -> append("\\t")
                else -> append(char)
            }
        }
        append('"')
    }
