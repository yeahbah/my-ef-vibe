package com.yeahbah.efvibe.toolwindow

import com.intellij.openapi.components.service
import com.intellij.openapi.project.Project
import com.intellij.openapi.wm.ToolWindow
import com.intellij.openapi.wm.ToolWindowFactory
import com.intellij.ui.content.ContentFactory
import com.yeahbah.efvibe.services.EfvibeProjectService

class EfvibeToolWindowFactory : ToolWindowFactory {
    override fun createToolWindowContent(project: Project, toolWindow: ToolWindow) {
        val panel = EfvibeToolWindowPanel(project)
        project.service<EfvibeProjectService>().panel = panel

        val content = ContentFactory.getInstance().createContent(panel, "Results", false)
        toolWindow.contentManager.addContent(content)
    }
}
