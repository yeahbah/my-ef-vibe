package com.yeahbah.efvibe.services

import java.io.File
import java.nio.file.Files
import kotlin.io.path.deleteIfExists
import kotlin.system.exitProcess

fun main() {
    val failures = mutableListOf<String>()

    fun check(name: String, condition: Boolean) {
        if (!condition) {
            failures += name
        }
    }

    check(
        "windows default is PowerShell",
        ShellQuote.kindFor("Windows 11", null) == ShellKind.PowerShell,
    )
    check(
        "linux default is POSIX",
        ShellQuote.kindFor("Linux", null) == ShellKind.Posix,
    )
    check(
        "cmd.exe is cmd",
        ShellQuote.kindFor("Windows 11", "C:\\Windows\\System32\\cmd.exe") == ShellKind.WindowsCmd,
    )
    check(
        "Git Bash is POSIX even on Windows",
        ShellQuote.kindFor("Windows 11", "C:\\Program Files\\Git\\bin\\bash.exe") == ShellKind.Posix,
    )
    check(
        "wsl is POSIX",
        ShellQuote.kindFor("Windows 11", "wsl.exe --distribution Ubuntu") == ShellKind.Posix,
    )

    val connection = "Host=localhost;Database=shop;Username=postgres;Password=sec\$(touch pwned)"
    check(
        "PowerShell quotes semicolons and dollar",
        ShellQuote.quote(connection, ShellKind.PowerShell) == "'$connection'",
    )
    check(
        "PowerShell escapes embedded single quotes",
        ShellQuote.quote("it's", ShellKind.PowerShell) == "'it''s'",
    )
    check(
        "cmd quotes semicolon and ampersand",
        ShellQuote.quote("Host=localhost;Database=shop&x", ShellKind.WindowsCmd) ==
            "\"Host=localhost;Database=shop&x\"",
    )
    check(
        "cmd escapes embedded double quotes",
        ShellQuote.quote("say \"hi\"", ShellKind.WindowsCmd) == "\"say \"\"hi\"\"\"",
    )
    check(
        "POSIX quotes dollar and semicolon",
        ShellQuote.quote(connection, ShellKind.Posix) == "'$connection'",
    )
    check(
        "POSIX escapes embedded single quotes",
        ShellQuote.quote("it's", ShellKind.Posix) == "'it'\\''s'",
    )

    val powershellLine = ShellQuote.prependWorkingDirectory(
        "C:\\work",
        ShellQuote.quote("efvibe", ShellKind.PowerShell),
        ShellKind.PowerShell,
    )
    check("PowerShell launch avoids &&", "&&" !in powershellLine && "if ($" in powershellLine)

    val marker = Files.createTempFile("efvibe-shell-quote", ".txt")
    val script = File.createTempFile("efvibe-args", ".sh")
    try {
        script.writeText(
            """
            #!/bin/bash
            printf '%s\n' "${'$'}#"
            printf '%s\n' "${'$'}2"

            """.trimIndent(),
        )
        script.setExecutable(true)
        Files.deleteIfExists(marker)

        val payload = "Host=localhost;Database=shop;Password=sec${'$'}(touch ${marker})"
        val unquoted = "cd /tmp && ${script.absolutePath} --connection-string $payload"
        runBash(unquoted)
        check("unquoted semicolon splits and runs command substitution", Files.exists(marker))

        Files.deleteIfExists(marker)
        val quotedArgs = listOf(script.absolutePath, "--connection-string", payload)
            .joinToString(" ") { ShellQuote.quote(it, ShellKind.Posix) }
        val quoted = ShellQuote.prependWorkingDirectory("/tmp", quotedArgs, ShellKind.Posix)
        val quotedOutput = runBash(quoted)
        val quotedLines = quotedOutput.lineSequence().filter { it.isNotEmpty() }.toList()
        check("quoted launch keeps two arguments", quotedLines.firstOrNull() == "2")
        check("quoted launch preserves the connection string", quotedLines.getOrNull(1) == payload)
        check("quoted launch does not run command substitution", !Files.exists(marker))
    } finally {
        script.delete()
        marker.deleteIfExists()
    }

    if (failures.isNotEmpty()) {
        System.err.println(failures.joinToString("\n"))
        exitProcess(1)
    }
}

private fun runBash(command: String): String {
    val process = ProcessBuilder("bash", "-c", command)
        .redirectErrorStream(true)
        .start()
    val output = process.inputStream.bufferedReader().readText()
    val exit = process.waitFor()
    if (exit != 0 && output.isNotBlank()) {
        System.err.println(output)
    }
    return output
}
