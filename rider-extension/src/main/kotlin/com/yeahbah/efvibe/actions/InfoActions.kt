package com.yeahbah.efvibe.actions

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.yeahbah.efvibe.services.CliRunner

abstract class EfvibeCliAction(private val title: String) : AnAction() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        event.presentation.isEnabled = event.project != null
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = EfvibeActionSupport.requireProject(event) ?: return
        EfvibeActionSupport.runInToolWindow(project, title) {
            run(CliRunner(project))
        }
    }

    protected abstract fun run(runner: CliRunner): com.yeahbah.efvibe.services.CliResult
}

class ShowDbInfoAction : EfvibeCliAction("DbInfo") {
    override fun run(runner: CliRunner) = runner.runDbInfo()
}

class ShowTablesAction : EfvibeCliAction("Tables") {
    override fun run(runner: CliRunner) = runner.runTables()
}

class ScanLiteAction : EfvibeCliAction("Scan Lite") {
    override fun run(runner: CliRunner) = runner.runScan("lite")
}

class ScanDeepAction : EfvibeCliAction("Scan Deep") {
    override fun run(runner: CliRunner) = runner.runScan("deep")
}

class CheckPrerequisitesAction : EfvibeCliAction("Prerequisites") {
    override fun run(runner: CliRunner) = runner.runAboutJson()
}
