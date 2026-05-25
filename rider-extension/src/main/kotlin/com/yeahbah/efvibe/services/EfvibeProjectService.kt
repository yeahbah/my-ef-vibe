package com.yeahbah.efvibe.services

import com.intellij.openapi.components.Service
import com.yeahbah.efvibe.toolwindow.EfvibeToolWindowPanel

@Service(Service.Level.PROJECT)
class EfvibeProjectService {
    var panel: EfvibeToolWindowPanel? = null
}
