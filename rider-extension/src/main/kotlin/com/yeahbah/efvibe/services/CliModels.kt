package com.yeahbah.efvibe.services

data class CliResult(
    val exitCode: Int,
    val stdout: String,
    val stderr: String,
) {
    val succeeded: Boolean get() = exitCode == 0
}

data class CliInvocation(
    val command: String,
    val prefixArgs: List<String> = emptyList(),
)
