package com.yeahbah.efvibe.services

import com.intellij.openapi.components.Service
import com.yeahbah.efvibe.toolwindow.EfvibeToolWindowPanel

@Service(Service.Level.PROJECT)
class EfvibeProjectService {
    var panel: EfvibeToolWindowPanel? = null
    var lastEvaluation: EvaluationPayload? = null
    var lastExpression: String = ""
    val history: MutableList<EvaluationHistoryEntry> = mutableListOf()

    fun recordEvaluation(expression: String, payload: EvaluationPayload) {
        lastExpression = expression
        lastEvaluation = payload
        history.add(0, EvaluationHistoryEntry(expression, payload))
        if (history.size > 25) {
            history.removeAt(history.lastIndex)
        }
    }
}

data class EvaluationHistoryEntry(
    val expression: String,
    val payload: EvaluationPayload,
)
