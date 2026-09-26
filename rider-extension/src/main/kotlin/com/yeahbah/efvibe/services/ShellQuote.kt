package com.yeahbah.efvibe.services

/**
 * Quotes argv tokens for the Rider terminal shell.
 *
 * Connection strings use `;` as a separator. Left unquoted, bash, zsh, and PowerShell
 * treat that as a statement break, so `efvibe` receives only the first segment.
 */
internal enum class ShellKind {
    Posix,
    WindowsCmd,
    PowerShell,
}

internal object ShellQuote {
    fun kindFor(osName: String, shellPath: String?): ShellKind {
        val shell = shellPath.orEmpty().lowercase()

        if (shell.contains("powershell") || shell.contains("pwsh")) {
            return ShellKind.PowerShell
        }

        if (shell.contains("cmd")) {
            return ShellKind.WindowsCmd
        }

        if (isPosixShell(shell)) {
            return ShellKind.Posix
        }

        // Rider's terminal defaults to powershell.exe on Windows and the user shell elsewhere.
        return if (osName.contains("windows", ignoreCase = true)) {
            ShellKind.PowerShell
        } else {
            ShellKind.Posix
        }
    }

    fun quote(value: String, kind: ShellKind): String =
        when (kind) {
            ShellKind.Posix -> quotePosix(value)
            ShellKind.WindowsCmd -> quoteCmd(value)
            ShellKind.PowerShell -> quotePowerShell(value)
        }

    fun prependWorkingDirectory(workingDirectory: String, command: String, kind: ShellKind): String {
        val quotedDir = quote(workingDirectory, kind)
        val dollar = '$'

        return when (kind) {
            // Windows PowerShell 5.1 rejects `&&`. `$?` is valid in 5.1 and 7.
            ShellKind.PowerShell -> "cd $quotedDir; if ($dollar?) { $command }"
            else -> "cd $quotedDir && $command"
        }
    }

    private fun quotePosix(value: String): String =
        "'" + value.replace("'", "'\\''") + "'"

    private fun quotePowerShell(value: String): String =
        "'" + value.replace("'", "''") + "'"

    private fun quoteCmd(value: String): String =
        "\"" + value.replace("\"", "\"\"") + "\""

    private fun isPosixShell(shell: String): Boolean {
        if (shell.isEmpty()) {
            return false
        }

        return shell.contains("bash")
            || shell.contains("zsh")
            || shell.contains("fish")
            || shell.contains("wsl")
            || shell.endsWith("/sh")
            || shell.endsWith("\\sh.exe")
            || shell.endsWith("/sh.exe")
    }
}
