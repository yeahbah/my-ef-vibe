package com.yeahbah.efvibe.settings

import com.intellij.openapi.components.service
import com.intellij.openapi.options.Configurable
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.DialogPanel
import com.intellij.ui.dsl.builder.bindItem
import com.intellij.ui.dsl.builder.bindSelected
import com.intellij.ui.dsl.builder.bindText
import com.intellij.ui.dsl.builder.panel
import com.yeahbah.efvibe.services.EfvibeDaemonClient
import com.yeahbah.efvibe.services.EfvibeSettingsService
import javax.swing.JComponent

class EfvibeConfigurable(private val project: Project) : Configurable {
    private val settings get() = project.service<EfvibeSettingsService>().state
    private var panel: DialogPanel? = null

    override fun getDisplayName(): String = "My EF Vibe"

    override fun createComponent(): JComponent {
        panel = panel {
            row("Workspace root:") {
                textField()
                    .align(com.intellij.ui.dsl.builder.AlignX.FILL)
                    .bindText(settings::workspaceRoot)
            }
            row("EF project:") {
                textField()
                    .align(com.intellij.ui.dsl.builder.AlignX.FILL)
                    .bindText(settings::project)
                    .comment("Supports \$PROJECT_DIR\$, \${workspaceFolder}, and \$(SolutionDir).")
            }
            row("Startup project:") {
                textField()
                    .align(com.intellij.ui.dsl.builder.AlignX.FILL)
                    .bindText(settings::startupProject)
                    .comment("Supports \$PROJECT_DIR\$, \${workspaceFolder}, and \$(SolutionDir).")
            }
            row("DbContext:") {
                textField()
                    .align(com.intellij.ui.dsl.builder.AlignX.FILL)
                    .bindText(settings::context)
            }
            row("Connection string:") {
                textField()
                    .align(com.intellij.ui.dsl.builder.AlignX.FILL)
                    .bindText(settings::connectionString)
            }
            row("Provider:") {
                textField()
                    .align(com.intellij.ui.dsl.builder.AlignX.FILL)
                    .bindText(settings::provider)
                    .comment("Optional alias or EF package id (for example FirebirdSql.EntityFrameworkCore.Firebird). Leave empty to auto-discover from the EF project.")
            }
            row("efvibe executable:") {
                textField()
                    .align(com.intellij.ui.dsl.builder.AlignX.FILL)
                    .bindText(settings::toolPath)
            }
            row(".NET framework:") {
                textField()
                    .align(com.intellij.ui.dsl.builder.AlignX.FILL)
                    .bindText(settings::dotnetFramework)
            }
            row {
                checkBox("Enable database logging")
                    .bindSelected(settings::dbLog)
            }
            group("Scan") {
                row {
                    checkBox("Respect dismissals")
                        .bindSelected(settings::scanRespectDismissals)
                }
                row("Minimum severity:") {
                    comboBox(listOf("", "info", "warning", "error"))
                        .bindItem({ settings.scanMinSeverity }, { settings.scanMinSeverity = it.orEmpty() })
                }
            }
        }

        return panel!!
    }

    override fun isModified(): Boolean = panel?.isModified() == true

    override fun apply() {
        panel?.apply()
        project.service<EfvibeDaemonClient>().invalidate()
    }

    override fun reset() {
        panel?.reset()
    }

    override fun disposeUIResources() {
        panel = null
    }
}
