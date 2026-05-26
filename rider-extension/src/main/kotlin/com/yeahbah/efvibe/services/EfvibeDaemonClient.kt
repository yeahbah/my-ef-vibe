package com.yeahbah.efvibe.services

import com.intellij.execution.configurations.GeneralCommandLine
import com.intellij.execution.process.OSProcessHandler
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Key
import java.nio.charset.StandardCharsets
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit

@Service(Service.Level.PROJECT)
class EfvibeDaemonClient(private val project: Project) {
    private var daemon: DaemonState? = null

    fun runExpression(expression: String, withPlan: Boolean): ExpressionRunResult {
        val state = ensureStarted()
        val request = """{"type":"eval","expression":${jsonString(expression)},"withPlan":$withPlan}"""
        val input = state.handler.processInput
            ?: throw IllegalStateException("efvibe daemon stdin is not available.")
        input.write("$request\n".toByteArray(StandardCharsets.UTF_8))
        input.flush()

        val line = state.waitForLine(10, TimeUnit.MINUTES)
            ?: throw IllegalStateException("efvibe daemon timed out waiting for an evaluation response.")
        val payload = EfvibeJsonParser.parseEvaluation(line)
        val exitCode = if (payload?.success == true) 0 else 20
        return ExpressionRunResult(CliResult(exitCode, line, ""), payload)
    }

    fun invalidate() {
        daemon?.handler?.destroyProcess()
        daemon = null
    }

    private fun ensureStarted(): DaemonState {
        daemon?.takeIf { !it.handler.isProcessTerminated }?.let { return it }

        val spec = CliRunner(project).buildServeSpec()
        val commandLine = GeneralCommandLine(spec.command)
            .withParameters(spec.args)
            .withWorkDirectory(spec.workingDirectory)
            .withCharset(StandardCharsets.UTF_8)

        val handler = OSProcessHandler(commandLine)
        val state = DaemonState(handler)
        daemon = state
        handler.addProcessListener(state)
        handler.startNotify()

        val line = state.waitForLine(10, TimeUnit.MINUTES)
            ?: throw IllegalStateException("efvibe serve timed out during workspace load.")
        if (!line.contains("\"type\":\"ready\"") && !line.contains("\"type\": \"ready\"")) {
            invalidate()
            throw IllegalStateException("Unexpected efvibe serve handshake: $line")
        }

        return state
    }

    private class DaemonState(val handler: OSProcessHandler) : com.intellij.execution.process.ProcessAdapter() {
        private val lines = ArrayBlockingQueue<String>(16)
        private val buffer = StringBuilder()

        override fun onTextAvailable(event: com.intellij.execution.process.ProcessEvent, outputType: Key<*>) {
            if (outputType != com.intellij.execution.process.ProcessOutputTypes.STDOUT) return

            buffer.append(event.text)
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
    }

    companion object {
        fun getInstance(project: Project): EfvibeDaemonClient =
            project.service()
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
