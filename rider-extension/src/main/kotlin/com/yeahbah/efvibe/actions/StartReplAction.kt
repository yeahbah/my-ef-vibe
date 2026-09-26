package com.yeahbah.efvibe.actions

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.ide.CopyPasteManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Messages
import com.yeahbah.efvibe.services.CliRunner
import com.yeahbah.efvibe.services.PathResolver
import java.awt.datatransfer.StringSelection

class StartReplAction : AnAction() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        event.presentation.isEnabled = event.project != null
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = EfvibeActionSupport.requireProject(event) ?: return
        val runner = CliRunner(project)
        var command = runner.buildReplCommandLine()

        runCatching {
            val workingDirectory = PathResolver.solutionDirectory(project).toString()
            command = runner.replTerminalCommand(workingDirectory)
            val terminal = createTerminalSession(project)
            terminal.javaClass
                .getMethod("sendCommandToExecute", String::class.java)
                .invoke(terminal, command)
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

private fun createTerminalSession(project: Project): Any {
    val managerClass = Class.forName("org.jetbrains.plugins.terminal.TerminalToolWindowManager")
    val manager = managerClass
        .getMethod("getInstance", Project::class.java)
        .invoke(null, project)
    return managerClass
        .getMethod("createNewSession")
        .invoke(manager)
}
