package com.yeahbah.efvibe.actions

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.ide.CopyPasteManager
import com.intellij.openapi.ui.Messages
import com.intellij.terminal.ui.TerminalWidget
import com.yeahbah.efvibe.services.CliRunner
import com.yeahbah.efvibe.services.PathResolver
import org.jetbrains.plugins.terminal.TerminalToolWindowManager
import java.awt.datatransfer.StringSelection

class StartReplAction : AnAction() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        event.presentation.isEnabled = event.project != null
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = EfvibeActionSupport.requireProject(event) ?: return
        val command = CliRunner(project).buildReplCommandLine()

        runCatching {
            val workingDirectory = PathResolver.solutionDirectory(project).toString()
            val terminal: TerminalWidget = TerminalToolWindowManager.getInstance(project)
                .createShellWidget(workingDirectory, "My EF Vibe", true, true)
            terminal.sendCommandToExecute(command)
        }.onFailure {
            CopyPasteManager.getInstance().setContents(StringSelection(command))
            Messages.showInfoMessage(
                project,
                "Could not open a terminal tab automatically. The efvibe command was copied to the clipboard:\n\n$command",
                "My EF Vibe",
            )
        }
    }
}
