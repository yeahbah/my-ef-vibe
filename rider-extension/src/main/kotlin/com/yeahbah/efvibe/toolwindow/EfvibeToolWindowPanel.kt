package com.yeahbah.efvibe.toolwindow

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Messages
import com.intellij.ui.components.JBScrollPane
import com.yeahbah.efvibe.services.CliResult
import com.yeahbah.efvibe.services.CliRunner
import com.yeahbah.efvibe.services.ExpressionGuard
import java.awt.BorderLayout
import java.awt.FlowLayout
import javax.swing.JButton
import javax.swing.JPanel
import javax.swing.JSplitPane
import javax.swing.JTextArea
import javax.swing.SwingUtilities

class EfvibeToolWindowPanel(private val project: Project) : JPanel(BorderLayout()) {
    private val expression = JTextArea("db.Set<Product>().Take(10)", 6, 80)
    private val output = JTextArea()

    init {
        output.isEditable = false
        output.lineWrap = false

        val toolbar = JPanel(FlowLayout(FlowLayout.LEFT)).apply {
            add(JButton("Run").apply {
                addActionListener { runExpression(withPlan = false) }
            })
            add(JButton("Run :plan").apply {
                addActionListener { runExpression(withPlan = true) }
            })
            add(JButton(":dbinfo").apply {
                addActionListener { runCommand("dbinfo") { CliRunner(project).runDbInfo() } }
            })
            add(JButton(":tables").apply {
                addActionListener { runCommand("tables") { CliRunner(project).runTables() } }
            })
        }

        val split = JSplitPane(
            JSplitPane.VERTICAL_SPLIT,
            JBScrollPane(expression),
            JBScrollPane(output),
        ).apply {
            resizeWeight = 0.25
        }

        add(toolbar, BorderLayout.NORTH)
        add(split, BorderLayout.CENTER)
    }

    fun setExpression(value: String) {
        expression.text = value
    }

    fun appendOutput(title: String, text: String) {
        val current = output.text
        val separator = if (current.isBlank()) "" else "\n\n"
        output.text = "$current$separator## $title\n$text"
        output.caretPosition = output.document.length
    }

    fun showError(message: String) {
        appendOutput("Error", message)
    }

    private fun runExpression(withPlan: Boolean) {
        val source = expression.text.trim()
        ExpressionGuard.validate(source)?.let {
            Messages.showWarningDialog(project, it, "My EF Vibe")
            return
        }

        runCommand(if (withPlan) "Run with Plan" else "Run") {
            CliRunner(project).runExpression(source, withPlan)
        }
    }

    private fun runCommand(title: String, action: () -> CliResult) {
        appendOutput(title, "Running...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val result = runCatching(action).getOrElse {
                CliResult(1, "", it.message ?: it.toString())
            }

            SwingUtilities.invokeLater {
                appendOutput(title, formatResult(result))
            }
        }
    }

    private fun formatResult(result: CliResult): String {
        val status = if (result.succeeded) "Exit code: 0" else "Exit code: ${result.exitCode}"
        return buildString {
            appendLine(status)
            if (result.stdout.isNotBlank()) {
                appendLine()
                appendLine(result.stdout.trim())
            }
            if (result.stderr.isNotBlank()) {
                appendLine()
                appendLine("stderr:")
                appendLine(result.stderr.trim())
            }
        }
    }
}
