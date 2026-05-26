package com.yeahbah.efvibe.services

data class ExpressionRunResult(
    val result: CliResult,
    val payload: EvaluationPayload?,
)
