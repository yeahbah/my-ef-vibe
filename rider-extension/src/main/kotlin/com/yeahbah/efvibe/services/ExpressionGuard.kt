package com.yeahbah.efvibe.services

object ExpressionGuard {
    private const val optionalTypeArgs = """(?:\s*<[^>(]+>)?"""

    private val blockedEfPatterns = listOf(
        Regex("""\bSaveChanges(?:Async)?$optionalTypeArgs\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\b(?:Add|AddRange|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange)\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bExecuteDelete(?:Async)?$optionalTypeArgs\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bExecuteUpdate(?:Async)?$optionalTypeArgs\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bExecuteSql(?:Raw|Interpolated)?(?:Async)?$optionalTypeArgs\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\b(?:EnsureDeleted|EnsureCreated|Migrate)(?:Async)?$optionalTypeArgs\s*\(""", RegexOption.IGNORE_CASE),
        Regex(
            """\bDatabase\s*\.\s*(?:ExecuteSql(?:Raw|Interpolated)?(?:Async)?|EnsureDeleted(?:Async)?|EnsureCreated(?:Async)?|Migrate(?:Async)?)\s*\(""",
            RegexOption.IGNORE_CASE,
        ),
        Regex("""\bGetService\s*<[^>]*>\s*\(\s*\)\s*\.\s*Delete\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bFromSqlRaw\s*<""", RegexOption.IGNORE_CASE),
    )

    private val blockedSql = Regex("""\b(?:DROP|DELETE|INSERT|UPDATE|TRUNCATE|ALTER|CREATE|EXEC(?:UTE)?|CALL|MERGE)\b""", RegexOption.IGNORE_CASE)
    private val scriptReferenceDirective = Regex("""^\s*#(?:load|r)\b""", setOf(RegexOption.IGNORE_CASE, RegexOption.MULTILINE))

    fun validate(expression: String): String? {
        val trimmed = expression.trim()
        if (trimmed.isEmpty()) return "Expression is empty."

        val stripped = stripComments(trimmed)
        if (scriptReferenceDirective.containsMatchIn(stripped)) {
            return "Read-only mode: #load and #r directives are not allowed from guarded UI execution paths."
        }

        if (blockedEfPatterns.any { it.containsMatchIn(stripped) }) {
            return "Read-only mode: SaveChanges, Add/Update/Remove, ExecuteSql, ExecuteDelete/Update, and schema changes are not allowed."
        }

        if (blockedSql.containsMatchIn(stripped) && Regex("""\b(?:ExecuteSql|FromSql|SqlQuery)\b""", RegexOption.IGNORE_CASE).containsMatchIn(stripped)) {
            return "Destructive SQL keywords are not allowed in ExecuteSql / FromSql / SqlQuery expressions."
        }

        return null
    }

    private fun stripComments(expression: String): String =
        expression
            .replace(Regex("""/\*[\s\S]*?\*/"""), " ")
            .replace(Regex("""//.*$""", setOf(RegexOption.MULTILINE)), " ")
}
