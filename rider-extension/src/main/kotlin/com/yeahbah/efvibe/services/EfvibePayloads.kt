package com.yeahbah.efvibe.services

import com.google.gson.JsonArray
import com.google.gson.JsonElement
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import com.google.gson.JsonPrimitive

data class EvaluationMetrics(
    val totalMs: Double = 0.0,
    val databaseMs: Double? = null,
    val rowCount: Int? = null,
    val sqlCommandCount: Int = 0,
    val resultKind: String = "",
    val estimatedBytes: Long? = null,
)

data class EvaluationPayload(
    val success: Boolean = false,
    val value: String? = null,
    val rows: List<Map<String, String>> = emptyList(),
    val sql: List<String> = emptyList(),
    val translatedSql: String? = null,
    val queryPlan: String? = null,
    val queryPlanNote: String? = null,
    val metrics: EvaluationMetrics = EvaluationMetrics(),
    val warnings: List<String> = emptyList(),
    val error: String? = null,
    val snippet: String? = null,
)

data class DbInfoEntry(val key: String, val value: String)

data class DbInfoPayload(
    val dbContext: String = "",
    val entries: List<DbInfoEntry> = emptyList(),
)

data class TablesEntry(
    val dbSet: String = "",
    val entityType: String = "",
    val entityTypeFullName: String = "",
)

data class TablesPayload(
    val dbContext: String = "",
    val tables: List<TablesEntry> = emptyList(),
)

data class ScanFinding(
    val filePath: String = "",
    val line: Int = 0,
    val code: String = "",
    val ruleId: String = "",
    val message: String = "",
    val severity: String = "warning",
    val recommendation: String = "",
    val translatedSql: String = "",
    val sqlTranslationNote: String = "",
    val queryPlan: String = "",
    val queryPlanNote: String = "",
    val savedNote: String = "",
)

data class ScanPayload(
    val scanMode: String = "",
    val savedPath: String = "",
    val totalFindings: Int = 0,
    val ciFailed: Boolean = false,
    val filesScanned: Int = 0,
    val projectsScanned: Int = 0,
    val displayRootDirectory: String = "",
    val findings: List<ScanFinding> = emptyList(),
)

object EfvibeJsonParser {
    fun parseEvaluation(stdout: String): EvaluationPayload? =
        firstJsonObject(stdout)?.let(::parseEvaluationObject)

    fun parseDbInfo(stdout: String): DbInfoPayload? =
        firstJsonObject(stdout)?.let { root ->
            DbInfoPayload(
                dbContext = root.string("dbContext"),
                entries = root.array("entries").mapObjects {
                    DbInfoEntry(
                        key = it.string("key"),
                        value = it.string("value"),
                    )
                },
            )
        }

    fun parseTables(stdout: String): TablesPayload? =
        firstJsonObject(stdout)?.let { root ->
            TablesPayload(
                dbContext = root.string("dbContext"),
                tables = root.array("tables").mapObjects {
                    TablesEntry(
                        dbSet = it.string("dbSet"),
                        entityType = it.string("entityType"),
                        entityTypeFullName = it.string("entityTypeFullName"),
                    )
                },
            )
        }

    fun parseScan(stdout: String): ScanPayload? =
        firstJsonObject(stdout)?.let { root ->
            ScanPayload(
                scanMode = root.string("scanMode"),
                savedPath = root.string("savedPath"),
                totalFindings = root.int("totalFindings"),
                ciFailed = root.bool("ciFailed"),
                filesScanned = root.int("filesScanned"),
                projectsScanned = root.int("projectsScanned"),
                displayRootDirectory = root.string("displayRootDirectory"),
                findings = root.array("findings").mapObjects(::parseScanFinding),
            )
        }

    private fun parseEvaluationObject(root: JsonObject): EvaluationPayload {
        val metrics = root.obj("metrics")
        return EvaluationPayload(
            success = root.bool("success"),
            value = root.element("value")?.stringValue(),
            rows = root.array("rows").mapObjects { row ->
                row.entrySet().associate { it.key to it.value.stringValue() }
            },
            sql = root.array("sql").map { it.stringValue() },
            translatedSql = root.element("translatedSql")?.stringValue(),
            queryPlan = root.element("queryPlan")?.stringValue(),
            queryPlanNote = root.element("queryPlanNote")?.stringValue(),
            metrics = EvaluationMetrics(
                totalMs = metrics?.double("totalMs") ?: 0.0,
                databaseMs = metrics?.optionalDouble("databaseMs"),
                rowCount = metrics?.optionalInt("rowCount"),
                sqlCommandCount = metrics?.int("sqlCommandCount") ?: 0,
                resultKind = metrics?.string("resultKind").orEmpty(),
                estimatedBytes = metrics?.optionalLong("estimatedBytes"),
            ),
            warnings = root.array("warnings").map { it.stringValue() },
            error = root.element("error")?.stringValue(),
            snippet = root.element("snippet")?.stringValue(),
        )
    }

    private fun parseScanFinding(root: JsonObject): ScanFinding =
        ScanFinding(
            filePath = root.string("filePath"),
            line = root.int("line"),
            code = root.string("code"),
            ruleId = root.string("ruleId"),
            message = root.string("message"),
            severity = root.string("severity").ifBlank { "warning" },
            recommendation = root.string("recommendation"),
            translatedSql = root.string("translatedSql"),
            sqlTranslationNote = root.string("sqlTranslationNote"),
            queryPlan = root.string("queryPlan"),
            queryPlanNote = root.string("queryPlanNote"),
            savedNote = root.string("savedNote"),
        )

    fun firstJsonObject(text: String): JsonObject? {
        val start = text.indexOf('{')
        if (start < 0) return null

        var depth = 0
        var inString = false
        var escaped = false

        for (index in start until text.length) {
            val char = text[index]

            if (escaped) {
                escaped = false
                continue
            }

            when {
                char == '\\' && inString -> escaped = true
                char == '"' -> inString = !inString
                !inString && char == '{' -> depth++
                !inString && char == '}' -> {
                    depth--
                    if (depth == 0) {
                        val json = text.substring(start, index + 1)
                        return runCatching { JsonParser.parseString(json).asJsonObject }.getOrNull()
                    }
                }
            }
        }

        return null
    }
}

private fun JsonObject.element(name: String): JsonElement? =
    get(name)?.takeUnless { it.isJsonNull }

private fun JsonObject.obj(name: String): JsonObject? =
    element(name)?.takeIf { it.isJsonObject }?.asJsonObject

private fun JsonObject.array(name: String): JsonArray =
    element(name)?.takeIf { it.isJsonArray }?.asJsonArray ?: JsonArray()

private fun JsonObject.string(name: String): String =
    element(name)?.stringValue().orEmpty()

private fun JsonObject.bool(name: String): Boolean =
    element(name)?.asBooleanOrNull() ?: false

private fun JsonObject.int(name: String): Int =
    element(name)?.asIntOrNull() ?: 0

private fun JsonObject.double(name: String): Double =
    element(name)?.asDoubleOrNull() ?: 0.0

private fun JsonObject.optionalInt(name: String): Int? =
    element(name)?.asIntOrNull()

private fun JsonObject.optionalLong(name: String): Long? =
    element(name)?.asLongOrNull()

private fun JsonObject.optionalDouble(name: String): Double? =
    element(name)?.asDoubleOrNull()

private fun <T> JsonArray.mapObjects(transform: (JsonObject) -> T): List<T> =
    mapNotNull { it.takeIf { entry -> entry.isJsonObject }?.asJsonObject?.let(transform) }

private fun JsonElement.stringValue(): String =
    when {
        isJsonNull -> ""
        isJsonPrimitive -> asJsonPrimitive.primitiveString()
        else -> toString()
    }

private fun JsonPrimitive.primitiveString(): String =
    when {
        isString -> asString
        isBoolean -> asBoolean.toString()
        isNumber -> asNumber.toString()
        else -> asString
    }

private fun JsonElement.asBooleanOrNull(): Boolean? =
    runCatching { asBoolean }.getOrNull()

private fun JsonElement.asIntOrNull(): Int? =
    runCatching { asInt }.getOrNull()

private fun JsonElement.asLongOrNull(): Long? =
    runCatching { asLong }.getOrNull()

private fun JsonElement.asDoubleOrNull(): Double? =
    runCatching { asDouble }.getOrNull()
