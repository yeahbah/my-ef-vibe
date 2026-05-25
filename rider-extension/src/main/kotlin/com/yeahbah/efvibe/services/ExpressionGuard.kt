package com.yeahbah.efvibe.services

object ExpressionGuard {
    private val blockedEfPatterns = listOf(
        Regex("""\bSaveChanges(?:Async)?\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\b(?:Add|AddRange|Update|UpdateRange|Remove|RemoveRange|Attach|AttachRange)\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bExecuteDelete(?:Async)?\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bExecuteUpdate(?:Async)?\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bExecuteSql(?:Raw)?(?:Async)?\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bDatabase\s*\.\s*(?:ExecuteSql(?:Raw)?|EnsureDeleted|EnsureCreated|Migrate)\s*\(""", RegexOption.IGNORE_CASE),
        Regex("""\bFromSqlRaw\s*<""", RegexOption.IGNORE_CASE),
    )

    private val blockedSql = Regex("""\b(?:DROP|DELETE|INSERT|UPDATE|TRUNCATE|ALTER|CREATE)\b""", RegexOption.IGNORE_CASE)

    fun validate(expression: String): String? {
        val trimmed = expression.trim()
        if (trimmed.isEmpty()) return "Expression is empty."

        val stripped = stripComments(trimmed)
        if (blockedEfPatterns.any { it.containsMatchIn(stripped) }) {
            return "Read-only mode: SaveChanges, Add/Update/Remove, ExecuteSql, ExecuteDelete/Update, and schema changes are not allowed."
        }

        if (blockedSql.containsMatchIn(stripped) && Regex("""\b(?:ExecuteSql|FromSqlRaw|SqlQuery)\b""", RegexOption.IGNORE_CASE).containsMatchIn(stripped)) {
            return "Destructive SQL keywords are not allowed in ExecuteSql / FromSqlRaw expressions."
        }

        return null
    }

    private fun stripComments(expression: String): String =
        expression
            .replace(Regex("""/\*[\s\S]*?\*/"""), " ")
            .replace(Regex("""//.*$""", setOf(RegexOption.MULTILINE)), " ")
}
