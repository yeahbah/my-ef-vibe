package com.yeahbah.efvibe.services

import com.intellij.openapi.project.Project
import java.nio.file.Path

object PathResolver {
    fun solutionDirectory(project: Project): Path =
        Path.of(project.basePath ?: System.getProperty("user.dir"))

    fun resolve(value: String, project: Project): String {
        val trimmed = value.trim()
        if (trimmed.isEmpty()) return ""

        val base = solutionDirectory(project)
        val expanded = trimmed
            .replace("\${workspaceFolder}", absolutePath(base))
            .replace("\$(SolutionDir)", ensureTrailingSeparator(absolutePath(base)))

        val path = Path.of(expanded)
        return if (path.isAbsolute) {
            absolutePath(path.normalize())
        } else {
            absolutePath(base.resolve(path).normalize())
        }
    }

    private fun absolutePath(path: Path): String =
        path.toAbsolutePath().normalize().toString()

    private fun ensureTrailingSeparator(path: String): String =
        if (path.endsWith('/') || path.endsWith('\\')) path else "$path/"
}
