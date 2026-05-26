package com.yeahbah.efvibe.actions

import com.intellij.openapi.actionSystem.ActionUpdateThread
import com.intellij.openapi.actionSystem.AnAction
import com.intellij.openapi.actionSystem.AnActionEvent
import com.intellij.openapi.components.service
import com.intellij.openapi.ui.Messages
import com.yeahbah.efvibe.services.EfvibeProjectService
import com.yeahbah.efvibe.services.ExpressionGuard

open class BaseRunSelectionAction(private val withPlan: Boolean) : AnAction() {
    override fun getActionUpdateThread(): ActionUpdateThread = ActionUpdateThread.BGT

    override fun update(event: AnActionEvent) {
        event.presentation.isEnabled = event.project != null
    }

    override fun actionPerformed(event: AnActionEvent) {
        val project = EfvibeActionSupport.requireProject(event) ?: return
        val expression = EfvibeActionSupport.editorExpression(event)

        if (expression.isBlank()) {
            Messages.showWarningDialog(project, "Select a LINQ expression or place the caret on a query line.", "My EF Vibe")
            return
        }

        ExpressionGuard.validate(expression)?.let {
            Messages.showWarningDialog(project, it, "My EF Vibe")
            return
        }

        EfvibeActionSupport.showToolWindow(project) {
            val panel = project.service<EfvibeProjectService>().panel
            panel?.setExpression(expression)
            panel?.evaluateExpression(expression, withPlan)
        }
    }
}

class RunSelectionAction : BaseRunSelectionAction(withPlan = false)

class RunSelectionWithPlanAction : BaseRunSelectionAction(withPlan = true)
