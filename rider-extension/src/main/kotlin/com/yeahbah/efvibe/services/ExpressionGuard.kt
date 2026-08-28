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

        if (blockedSql.containsMatchIn(stripped) && Regex("""\b(?:ExecuteSql|FromSqlRaw|SqlQuery)\b""", RegexOption.IGNORE_CASE).containsMatchIn(stripped)) {
            return "Destructive SQL keywords are not allowed in ExecuteSql / FromSqlRaw expressions."
        }

        return null
    }

    /**
     * Remove C# comments without treating `//` or `/*` inside string/char literals as comments.
     * Naive `//.*$` stripping turns URL filters like `"http://example.com").ExecuteDelete()` into
     * `"http:` and lets guarded UI paths execute the hidden mutation.
     */
    private fun stripComments(expression: String): String = CSharpCommentStripper(expression).strip()

    private class CSharpCommentStripper(private val src: String) {
        private var i = 0
        private val output = StringBuilder(src.length)

        fun strip(): String {
            readCode(stopAtUnmatchedBrace = false)
            return output.toString()
        }

        private fun peek(ahead: Int = 0): Char = if (i + ahead < src.length) src[i + ahead] else '\u0000'

        private fun skipLineComment() {
            i += 2
            while (i < src.length && src[i] != '\n' && src[i] != '\r') {
                i++
            }
        }

        private fun skipBlockComment() {
            i += 2
            while (i < src.length && !(src[i] == '*' && i + 1 < src.length && src[i + 1] == '/')) {
                i++
            }
            if (i < src.length) {
                i += 2
            }
        }

        private fun readCharLiteral() {
            output.append(src[i])
            i++
            if (i < src.length && src[i] == '\\') {
                output.append(src[i])
                i++
                if (i < src.length) {
                    output.append(src[i])
                    i++
                }
            } else if (i < src.length) {
                output.append(src[i])
                i++
            }
            if (i < src.length && src[i] == '\'') {
                output.append(src[i])
                i++
            }
        }

        private fun readCode(stopAtUnmatchedBrace: Boolean) {
            var braceDepth = 0
            while (i < src.length) {
                val c = peek()
                val n = peek(1)

                if (stopAtUnmatchedBrace && c == '}' && braceDepth == 0) {
                    return
                }

                if (c == '/' && n == '/') {
                    skipLineComment()
                    output.append(' ')
                    continue
                }

                if (c == '/' && n == '*') {
                    skipBlockComment()
                    output.append(' ')
                    continue
                }

                if (c == '\'') {
                    readCharLiteral()
                    continue
                }

                if (tryReadString()) {
                    continue
                }

                if (c == '{') {
                    braceDepth++
                } else if (c == '}' && braceDepth > 0) {
                    braceDepth--
                }

                output.append(c)
                i++
            }
        }

        private fun readInterpolationHole() {
            output.append('{')
            i++
            readCode(stopAtUnmatchedBrace = true)
            if (peek() == '}') {
                output.append('}')
                i++
            }
        }

        private fun readQuotedString(verbatim: Boolean, interpolated: Boolean) {
            output.append('"')
            i++
            while (i < src.length) {
                val c = peek()
                val n = peek(1)

                if (interpolated && c == '{') {
                    if (n == '{') {
                        output.append("{{")
                        i += 2
                        continue
                    }
                    readInterpolationHole()
                    continue
                }

                if (interpolated && c == '}' && n == '}') {
                    output.append("}}")
                    i += 2
                    continue
                }

                if (verbatim) {
                    if (c == '"' && n == '"') {
                        output.append("\"\"")
                        i += 2
                        continue
                    }
                    if (c == '"') {
                        output.append('"')
                        i++
                        return
                    }
                    output.append(c)
                    i++
                    continue
                }

                if (c == '\\' && n != '\u0000') {
                    output.append(c).append(n)
                    i += 2
                    continue
                }

                if (c == '"') {
                    output.append('"')
                    i++
                    return
                }

                output.append(c)
                i++
            }
        }

        private fun readRawString(quoteCount: Int, dollars: Int) {
            repeat(quoteCount) {
                output.append('"')
                i++
            }

            val holeOpen = if (dollars > 0) "{".repeat(dollars) else ""

            while (i < src.length) {
                if (holeOpen.isNotEmpty() && src.startsWith(holeOpen, i) && peek(dollars) != '{') {
                    repeat(dollars) {
                        output.append('{')
                        i++
                    }
                    readCode(stopAtUnmatchedBrace = true)
                    var closed = 0
                    while (closed < dollars && peek() == '}') {
                        output.append('}')
                        i++
                        closed++
                    }
                    continue
                }

                var quotes = 0
                while (peek(quotes) == '"') {
                    quotes++
                }

                if (quotes >= quoteCount) {
                    repeat(quoteCount) {
                        output.append('"')
                        i++
                    }
                    return
                }

                output.append(peek())
                i++
            }
        }

        private fun tryReadString(): Boolean {
            var j = i
            var dollars = 0
            var verbatim = false

            if (j < src.length && src[j] == '@' && j + 1 < src.length && src[j + 1] == '$') {
                verbatim = true
                j += 2
                dollars = 1
                while (j < src.length && src[j] == '$') {
                    dollars++
                    j++
                }
            } else {
                while (j < src.length && src[j] == '$') {
                    dollars++
                    j++
                }
                if (j < src.length && src[j] == '@') {
                    verbatim = true
                    j++
                }
            }

            if (j >= src.length || src[j] != '"') {
                return false
            }

            var quoteCount = 0
            while (j + quoteCount < src.length && src[j + quoteCount] == '"') {
                quoteCount++
            }

            while (i < j) {
                output.append(src[i])
                i++
            }

            if (quoteCount >= 3) {
                readRawString(quoteCount, dollars)
                return true
            }

            readQuotedString(verbatim, dollars > 0)
            return true
        }
    }
}
