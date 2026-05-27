package com.yeahbah.efvibe.services

data class ExpressionRunResult(
    val result: CliResult,
    val payload: EvaluationPayload?,
    val usedDaemon: Boolean = false,
    val daemonError: String? = null,
)
