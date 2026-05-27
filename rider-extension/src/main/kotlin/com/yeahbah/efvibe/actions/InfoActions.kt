package com.yeahbah.efvibe.actions

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import com.yeahbah.efvibe.services.CliRunner
import com.yeahbah.efvibe.services.EfvibeDaemonClient
import com.yeahbah.efvibe.services.EfvibeProjectService
import com.yeahbah.efvibe.toolwindow.EfvibeToolWindowPanel

abstract class EfvibeCliAction : AnAction() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        event.presentation.isEnabled = event.project != null
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = EfvibeActionSupport.requireProject(event) ?: return
        EfvibeActionSupport.showToolWindow(project) {
            run(project, project.service<EfvibeProjectService>().panel)
        }
    }

    protected abstract fun run(project: Project, panel: EfvibeToolWindowPanel?)
}

class ShowDbInfoAction : EfvibeCliAction() {
    override fun run(project: Project, panel: EfvibeToolWindowPanel?) {
        panel?.runDbInfo()
    }
}

class ShowTablesAction : EfvibeCliAction() {
    override fun run(project: Project, panel: EfvibeToolWindowPanel?) {
        panel?.runTables()
    }
}

class ScanLiteAction : EfvibeCliAction() {
    override fun run(project: Project, panel: EfvibeToolWindowPanel?) {
        panel?.runScan("lite")
    }
}

class ScanDeepAction : EfvibeCliAction() {
    override fun run(project: Project, panel: EfvibeToolWindowPanel?) {
        panel?.runScan("deep")
    }
}

class CheckPrerequisitesAction : EfvibeCliAction() {
    override fun run(project: Project, panel: EfvibeToolWindowPanel?) {
        val result = CliRunner(project).runAboutJson()
        panel?.appendOutput("Prerequisites", result.stdout.ifBlank { result.stderr })
    }
}

class RefreshConnectionAction : EfvibeCliAction() {
    override fun run(project: Project, panel: EfvibeToolWindowPanel?) {
        project.service<EfvibeDaemonClient>().invalidate()
        panel?.appendOutput(
            "Refresh Connection",
            "Connection refreshed. The next query will start a new efvibe session.",
        )
    }
}
