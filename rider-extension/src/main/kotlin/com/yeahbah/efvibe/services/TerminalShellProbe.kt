package com.yeahbah.efvibe.services

internal object TerminalShellProbe {
    fun shellPath(project: Any): String? {
        return runCatching {
            val clazz = Class.forName("org.jetbrains.plugins.terminal.TerminalProjectOptionsProvider")
            val getInstance = clazz.methods.firstOrNull { method ->
                method.name == "getInstance" && method.parameterCount == 1
            } ?: return null

            val provider = getInstance.invoke(null, project) ?: return null
            val getter = provider.javaClass.methods.firstOrNull { method ->
                method.name == "getShellPath" && method.parameterCount == 0
            } ?: return null

            getter.invoke(provider) as? String
        }.getOrNull()?.takeIf { it.isNotBlank() }
    }
}
