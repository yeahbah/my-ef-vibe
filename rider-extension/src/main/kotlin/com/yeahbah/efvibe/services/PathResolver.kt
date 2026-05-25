package com.yeahbah.efvibe.services

import com.intellij.openapi.project.Project
import java.nio.file.Path
import kotlin.io.path.absolutePathString
import kotlin.io.path.isAbsolute
import kotlin.io.path.normalize

object PathResolver {
    fun solutionDirectory(project: Project): Path =
        Path.of(project.basePath ?: System.getProperty("user.dir"))

    fun resolve(value: String, project: Project): String {
        val trimmed = value.trim()
        if (trimmed.isEmpty()) return ""

        val base = solutionDirectory(project)
        val expanded = trimmed
            .replace("\${workspaceFolder}", base.absolutePathString())
            .replace("\$(SolutionDir)", ensureTrailingSeparator(base.absolutePathString()))

        val path = Path.of(expanded)
        return if (path.isAbsolute()) {
            path.normalize().absolutePathString()
        } else {
            base.resolve(path).normalize().absolutePathString()
        }
    }

    private fun ensureTrailingSeparator(path: String): String =
        if (path.endsWith('/') || path.endsWith('\\')) path else "$path/"
}
