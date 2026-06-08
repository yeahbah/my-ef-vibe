package com.yeahbah.efvibe.services

import com.intellij.openapi.components.PersistentStateComponent
import com.intellij.openapi.components.Service
import com.intellij.openapi.components.State
import com.intellij.openapi.components.Storage
import com.intellij.openapi.components.StoragePathMacros

data class EfvibeSettingsState(
    var workspaceRoot: String = "",
    var project: String = "",
    var startupProject: String = "",
    var context: String = "",
    var connectionString: String = "",
    var toolPath: String = "",
    var dotnetFramework: String = "",
    var dbLog: Boolean = true,
    var scanRespectDismissals: Boolean = true,
    var scanMinSeverity: String = "",
)

@Service(Service.Level.PROJECT)
@State(name = "MyEfVibeSettings", storages = [Storage(StoragePathMacros.WORKSPACE_FILE)])
class EfvibeSettingsService : PersistentStateComponent<EfvibeSettingsState> {
    private var state = EfvibeSettingsState()

    override fun getState(): EfvibeSettingsState = state

    override fun loadState(state: EfvibeSettingsState) {
        this.state = state
    }
}
