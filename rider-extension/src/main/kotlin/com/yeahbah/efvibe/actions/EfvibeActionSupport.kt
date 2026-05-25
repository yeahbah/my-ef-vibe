package com.yeahbah.efvibe.actions

import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.actionSystem.CommonDataKeys
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Messages
import com.intellij.openapi.wm.ToolWindowManager
import com.yeahbah.efvibe.services.CliResult
import com.yeahbah.efvibe.services.EfvibeProjectService
import javax.swing.SwingUtilities

object EfvibeActionSupport {
    fun requireProject(event: AnActionEvent): Project? =
        event.project.also {
            if (it == null) {
                Messages.showWarningDialog("Open a Rider project before using My EF Vibe.", "My EF Vibe")
            }
        }

    fun editorExpression(event: AnActionEvent): String {
        val editor = event.getData(CommonDataKeys.EDITOR) ?: return ""
        val selection = editor.selectionModel.selectedText
        if (!selection.isNullOrBlank()) return selection.trim()

        val document = editor.document
        val line = document.getLineNumber(editor.caretModel.offset)
        return document.getText(
            com.intellij.openapi.util.TextRange(
                document.getLineStartOffset(line),
                document.getLineEndOffset(line),
            ),
        ).trim()
    }

    fun showToolWindow(project: Project, afterShown: () -> Unit = {}) {
        val toolWindow = ToolWindowManager.getInstance(project).getToolWindow("My EF Vibe")
        if (toolWindow == null) {
            Messages.showWarningDialog(project, "The My EF Vibe tool window is not available yet.", "My EF Vibe")
            return
        }

        toolWindow.show {
            afterShown()
        }
    }

    fun runInToolWindow(project: Project, title: String, action: () -> CliResult) {
        showToolWindow(project) {
            project.service<EfvibeProjectService>().panel?.appendOutput(title, "Running...")
            ApplicationManager.getApplication().executeOnPooledThread {
                val result = runCatching(action).getOrElse {
                    CliResult(1, "", it.message ?: it.toString())
                }

                SwingUtilities.invokeLater {
                    val body = buildString {
                        appendLine("Exit code: ${result.exitCode}")
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
                    project.service<EfvibeProjectService>().panel?.appendOutput(title, body)
                }
            }
        }
    }
}
