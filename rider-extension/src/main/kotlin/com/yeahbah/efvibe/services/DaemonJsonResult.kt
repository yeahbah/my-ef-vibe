package com.yeahbah.efvibe.services

data class DaemonJsonResult<T>(
    val result: CliResult,
    val payload: T?,
    val usedDaemon: Boolean = false,
    val daemonError: String? = null,
)

data class DaemonRunResult(
    val result: CliResult,
    val usedDaemon: Boolean = false,
    val daemonError: String? = null,
)
