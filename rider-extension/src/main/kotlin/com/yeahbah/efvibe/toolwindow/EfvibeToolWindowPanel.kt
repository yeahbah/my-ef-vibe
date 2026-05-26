package com.yeahbah.efvibe.toolwindow

import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.components.service
import com.intellij.openapi.fileEditor.OpenFileDescriptor
import com.intellij.openapi.fileEditor.FileEditorManager
import com.intellij.openapi.ide.CopyPasteManager
import com.intellij.openapi.project.Project
import com.intellij.openapi.ui.Messages
import com.intellij.openapi.vfs.LocalFileSystem
import com.google.gson.JsonParser
import com.intellij.ui.JBColor
import com.intellij.ui.components.JBScrollPane
import com.yeahbah.efvibe.services.CliRunner
import com.yeahbah.efvibe.services.DbInfoPayload
import com.yeahbah.efvibe.services.EvaluationPayload
import com.yeahbah.efvibe.services.EfvibeProjectService
import com.yeahbah.efvibe.services.EfvibeSettingsService
import com.yeahbah.efvibe.services.ExpressionGuard
import com.yeahbah.efvibe.services.ScanFinding
import com.yeahbah.efvibe.services.ScanPayload
import com.yeahbah.efvibe.services.TablesPayload
import java.awt.BorderLayout
import java.awt.Color
import java.awt.Component
import java.awt.FlowLayout
import java.awt.Font
import java.awt.datatransfer.StringSelection
import java.io.File
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import javax.swing.BorderFactory
import javax.swing.JButton
import javax.swing.JFileChooser
import javax.swing.JLabel
import javax.swing.JOptionPane
import javax.swing.JPanel
import javax.swing.JSplitPane
import javax.swing.JTabbedPane
import javax.swing.JTextArea
import javax.swing.SwingUtilities
import javax.swing.JTable
import javax.swing.ListSelectionModel
import javax.swing.UIManager
import javax.swing.table.DefaultTableCellRenderer
import javax.swing.table.DefaultTableModel

class EfvibeToolWindowPanel(private val project: Project) : JPanel(BorderLayout()) {
    private val expression = JTextArea("db.Set<Product>().Take(10)", 6, 80)
    private val status = JLabel("Ready")
    private val tabs = JTabbedPane()
    private val resultTable = JTable()
    private val sqlText = readOnlyTextArea()
    private val planText = readOnlyTextArea()
    private val messagesText = readOnlyTextArea()
    private val sessionText = readOnlyTextArea()
    private val modelText = readOnlyTextArea()
    private val modelTable = JTable()
    private val scanDetails = readOnlyTextArea()
    private val historyText = readOnlyTextArea()
    private val notebookCells = JTextArea(
        """
        db.Products.Take(10)

        ---
        :dbinfo
        """.trimIndent(),
        10,
        80,
    )
    private val notebookOutput = readOnlyTextArea()

    private var lastPayload: EvaluationPayload? = null
    private var lastScan: ScanPayload? = null
    private var scanIndex: Int = 0

    init {
        background = PanelBackground
        expression.lineWrap = true
        expression.wrapStyleWord = true
        expression.border = BorderFactory.createCompoundBorder(
            BorderFactory.createLineBorder(BorderColor),
            BorderFactory.createEmptyBorder(8, 10, 8, 10),
        )
        expression.background = EditorBackground
        expression.foreground = Foreground
        expression.caretColor = Foreground
        expression.font = Font(Font.MONOSPACED, Font.PLAIN, expression.font.size)
        resultTable.autoResizeMode = JTable.AUTO_RESIZE_OFF
        modelTable.autoResizeMode = JTable.AUTO_RESIZE_OFF
        modelTable.setSelectionMode(ListSelectionModel.SINGLE_SELECTION)
        modelTable.selectionModel.addListSelectionListener {
            if (!it.valueIsAdjusting) renderSelectedModelRow()
        }
        styleStatus(status)
        listOf(resultTable, modelTable).forEach(::styleTable)
        scanDetails.lineWrap = true
        scanDetails.wrapStyleWord = true

        val toolbar = JPanel(FlowLayout(FlowLayout.LEFT)).apply {
            background = HeaderBackground
            border = BorderFactory.createEmptyBorder(8, 10, 8, 10)
            add(JButton("Run").apply {
                addActionListener { runCurrentExpression(withPlan = false) }
            })
            add(JButton("Run Plan").apply {
                addActionListener { runCurrentExpression(withPlan = true) }
            })
            add(JButton("Scan Lite").apply {
                addActionListener { runScan("lite") }
            })
            add(JButton("Scan Deep").apply {
                addActionListener { runScan("deep") }
            })
            add(JButton("Copy Tab").apply {
                addActionListener { copyActiveTab() }
            })
            styleButtons(this)
        }

        tabs.addTab("Result", buildResultPanel())
        tabs.addTab("SQL", JBScrollPane(sqlText))
        tabs.addTab("Plan", JBScrollPane(planText))
        tabs.addTab("Messages", JBScrollPane(messagesText))
        tabs.addTab("Session", JBScrollPane(sessionText))
        tabs.addTab("Model", buildModelPanel())
        tabs.addTab("Scan Review", buildScanPanel())
        tabs.addTab("History", JBScrollPane(historyText))
        tabs.addTab("Notebook", buildNotebookPanel())

        val top = JPanel(BorderLayout()).apply {
            background = PanelBackground
            add(toolbar, BorderLayout.NORTH)
            add(JBScrollPane(expression), BorderLayout.CENTER)
            border = BorderFactory.createMatteBorder(0, 0, 1, 0, BorderColor)
            minimumSize = java.awt.Dimension(0, 120)
        }

        add(
            JSplitPane(JSplitPane.VERTICAL_SPLIT, top, tabs).apply {
                resizeWeight = 0.22
                setDividerLocation(190)
                border = null
                minimumSize = java.awt.Dimension(0, 240)
            },
            BorderLayout.CENTER,
        )
        add(status, BorderLayout.SOUTH)
        renderSession()
    }

    fun setExpression(value: String) {
        expression.text = value
    }

    fun appendOutput(title: String, text: String) {
        messagesText.text = appendBlock(messagesText.text, title, text)
        tabs.selectedIndex = tabs.indexOfTab("Messages")
    }

    fun showError(message: String) {
        appendOutput("Error", message)
    }

    fun evaluateExpression(source: String, withPlan: Boolean) {
        ExpressionGuard.validate(source)?.let {
            Messages.showWarningDialog(project, it, "My EF Vibe")
            return
        }

        setBusy(if (withPlan) "Running query plan..." else "Running query...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val run = runCatching {
                CliRunner(project).runExpressionPayload(source, withPlan)
            }

            SwingUtilities.invokeLater {
                run.fold(
                    onSuccess = { result ->
                        val payload = result.payload
                        if (payload != null) {
                            project.service<EfvibeProjectService>().recordEvaluation(source, payload)
                            renderEvaluation(source, payload, withPlan)
                        } else {
                            appendOutput(
                                "Evaluation failed",
                                result.result.stderr.ifBlank { result.result.stdout.ifBlank { "No JSON payload returned." } },
                            )
                        }
                        setReady()
                    },
                    onFailure = {
                        showError(it.message ?: it.toString())
                        setReady()
                    },
                )
            }
        }
    }

    private fun buildResultPanel(): JPanel =
        JPanel(BorderLayout()).apply {
            background = PanelBackground
            val toolbar = JPanel(FlowLayout(FlowLayout.LEFT)).apply {
                background = ToolbarBackground
                border = BorderFactory.createEmptyBorder(6, 8, 6, 8)
                add(JButton("Export CSV").apply {
                    addActionListener { exportLast("csv") }
                })
                add(JButton("Export JSON").apply {
                    addActionListener { exportLast("json") }
                })
                styleButtons(this)
            }
            add(toolbar, BorderLayout.NORTH)
            add(JBScrollPane(resultTable), BorderLayout.CENTER)
        }

    private fun runCurrentExpression(withPlan: Boolean) {
        val source = expression.text.trim()
        if (source.isBlank()) {
            Messages.showWarningDialog(project, "Enter a LINQ expression first.", "My EF Vibe")
            return
        }

        evaluateExpression(source, withPlan)
    }

    fun runDbInfo() {
        setBusy("Loading DbInfo...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val result = runCatching { CliRunner(project).runDbInfoPayload() }
            SwingUtilities.invokeLater {
                result.fold(
                    onSuccess = { (cli, payload) ->
                        if (payload != null) {
                            renderDbInfo(payload)
                        } else {
                            appendOutput("DbInfo", cli.stderr.ifBlank { cli.stdout })
                        }
                        setReady()
                    },
                    onFailure = {
                        showError(it.message ?: it.toString())
                        setReady()
                    },
                )
            }
        }
    }

    fun runTables() {
        setBusy("Loading tables...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val result = runCatching { CliRunner(project).runTablesPayload() }
            SwingUtilities.invokeLater {
                result.fold(
                    onSuccess = { (cli, payload) ->
                        if (payload != null) {
                            renderTables(payload)
                        } else {
                            appendOutput("Tables", cli.stderr.ifBlank { cli.stdout })
                        }
                        setReady()
                    },
                    onFailure = {
                        showError(it.message ?: it.toString())
                        setReady()
                    },
                )
            }
        }
    }

    fun runScan(mode: String) {
        setBusy("Running scan $mode...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val result = runCatching { CliRunner(project).runScanPayload(mode) }
            SwingUtilities.invokeLater {
                result.fold(
                    onSuccess = { (cli, payload) ->
                        if (payload != null) {
                            renderScan(payload)
                        } else {
                            appendOutput("Scan $mode", cli.stderr.ifBlank { cli.stdout })
                        }
                        setReady()
                    },
                    onFailure = {
                        showError(it.message ?: it.toString())
                        setReady()
                    },
                )
            }
        }
    }

    private fun renderEvaluation(source: String, payload: EvaluationPayload, showPlan: Boolean) {
        lastPayload = payload
        expression.text = source
        renderRows(payload)
        sqlText.text = payload.sql.ifEmpty {
            listOfNotNull(payload.translatedSql)
        }.joinToString("\n\n---\n\n").ifBlank { "No SQL captured for this run." }
        planText.text = payload.queryPlan ?: payload.queryPlanNote ?: "No query plan captured for this run."
        messagesText.text = buildString {
            appendLine(if (payload.success) "Success" else "Failed")
            appendLine("Total: ${payload.metrics.totalMs} ms")
            payload.metrics.databaseMs?.let { appendLine("Database: $it ms") }
            payload.metrics.rowCount?.let { appendLine("Rows: $it") }
            appendLine("SQL commands: ${payload.metrics.sqlCommandCount}")
            if (payload.metrics.resultKind.isNotBlank()) appendLine("Result kind: ${payload.metrics.resultKind}")
            if (!payload.error.isNullOrBlank()) {
                appendLine()
                appendLine(payload.error)
            }
            if (payload.warnings.isNotEmpty()) {
                appendLine()
                appendLine("Warnings:")
                payload.warnings.forEach { appendLine("- $it") }
            }
        }
        renderHistory()
        tabs.selectedIndex = if (showPlan) tabs.indexOfTab("Plan") else 0
    }

    private fun renderRows(payload: EvaluationPayload) {
        val rows = if (payload.rows.isNotEmpty()) {
            payload.rows
        } else {
            listOf(mapOf("value" to (payload.value ?: "")))
        }

        val columns = rows.flatMap { it.keys }.distinct().ifEmpty { listOf("value") }
        val model = DefaultTableModel(columns.toTypedArray(), 0)
        rows.forEach { row ->
            model.addRow(columns.map { row[it].orEmpty() }.toTypedArray())
        }
        resultTable.model = model
    }

    private fun renderDbInfo(payload: DbInfoPayload) {
        modelText.text = buildString {
            appendLine("DbContext: ${payload.dbContext}")
            appendLine()
            payload.entries.forEach { appendLine("${it.key}: ${it.value}") }
        }
        tabs.selectedIndex = tabs.indexOfTab("Model")
    }

    private fun renderTables(payload: TablesPayload) {
        val tableModel = DefaultTableModel(arrayOf("DbSet", "Entity", "Full Type"), 0)
        payload.tables.forEach {
            tableModel.addRow(arrayOf(it.dbSet, it.entityType, it.entityTypeFullName))
        }
        modelTable.model = tableModel
        modelText.text = if (payload.tables.isEmpty()) {
            "No DbSets returned for ${payload.dbContext}."
        } else {
            "Select a DbSet above, then use Run Count, Run Sample, or Describe."
        }
        if (payload.tables.isNotEmpty()) {
            modelTable.setRowSelectionInterval(0, 0)
        }
        tabs.selectedIndex = tabs.indexOfTab("Model")
    }

    private fun buildModelPanel(): JPanel =
        JPanel(BorderLayout()).apply {
            background = PanelBackground
            val toolbar = JPanel(FlowLayout(FlowLayout.LEFT)).apply {
                background = ToolbarBackground
                border = BorderFactory.createEmptyBorder(6, 8, 6, 8)
                add(JButton("Db Info").apply {
                    addActionListener { runDbInfo() }
                })
                add(JButton("Tables").apply {
                    addActionListener { runTables() }
                })
                add(JButton("Run Count").apply {
                    addActionListener { selectedDbSet()?.let { evaluateExpression("db.$it.Count()", withPlan = false) } }
                })
                add(JButton("Run Sample").apply {
                    addActionListener { selectedDbSet()?.let { evaluateExpression("db.$it.Take(10)", withPlan = false) } }
                })
                add(JButton("Describe").apply {
                    addActionListener { selectedDbSet()?.let { runDescribe(it) } }
                })
                styleButtons(this)
            }
            add(toolbar, BorderLayout.NORTH)
            add(
                JSplitPane(
                    JSplitPane.VERTICAL_SPLIT,
                    JBScrollPane(modelTable),
                    JBScrollPane(modelText),
                ).apply {
                    resizeWeight = 0.65
                },
                BorderLayout.CENTER,
            )
        }

    private fun selectedDbSet(): String? {
        if (modelTable.model.columnCount == 0 || modelTable.rowCount == 0) {
            Messages.showWarningDialog(project, "Select a DbSet in the Model tab first.", "My EF Vibe")
            return null
        }

        val selected = modelTable.selectedRow.takeIf { it >= 0 } ?: 0
        if (modelTable.selectedRow < 0) {
            modelTable.setRowSelectionInterval(selected, selected)
        }

        val modelRow = modelTable.convertRowIndexToModel(selected)
        return modelTable.model.getValueAt(modelRow, 0)?.toString()?.takeIf { it.isNotBlank() }
    }

    private fun renderSelectedModelRow() {
        val row = modelTable.selectedRow
        if (row < 0 || modelTable.model.columnCount < 3) return

        val modelRow = modelTable.convertRowIndexToModel(row)
        val dbSet = modelTable.model.getValueAt(modelRow, 0)?.toString().orEmpty()
        val entity = modelTable.model.getValueAt(modelRow, 1)?.toString().orEmpty()
        val fullType = modelTable.model.getValueAt(modelRow, 2)?.toString().orEmpty()
        modelText.text = buildString {
            appendLine("DbSet: $dbSet")
            appendLine("Entity: $entity")
            if (fullType.isNotBlank()) appendLine("Full type: $fullType")
            appendLine()
            appendLine("Actions:")
            appendLine("- Run Count: db.$dbSet.Count()")
            appendLine("- Run Sample: db.$dbSet.Take(10)")
            appendLine("- Describe: efvibe --describe-json $dbSet")
        }
    }

    private fun runDescribe(entity: String) {
        setBusy("Describing $entity...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val result = CliRunner(project).runDescribe(entity)
            SwingUtilities.invokeLater {
                modelText.text = result.stdout.ifBlank { result.stderr.ifBlank { "No describe output." } }
                tabs.selectedIndex = tabs.indexOfTab("Model")
                setReady()
            }
        }
    }

    private fun renderScan(payload: ScanPayload, selectedIndex: Int = 0) {
        lastScan = payload
        scanIndex = if (payload.findings.isEmpty()) {
            0
        } else {
            selectedIndex.coerceIn(0, payload.findings.lastIndex)
        }
        renderSelectedScanFinding()
        tabs.selectedIndex = tabs.indexOfTab("Scan Review")
    }

    private fun renderSelectedScanFinding() {
        val payload = lastScan ?: return
        val finding = payload.findings.getOrNull(scanIndex)
        scanDetails.text = if (finding == null) {
            "No scan findings."
        } else {
            formatFinding(finding, scanIndex + 1, payload.findings.size)
        }
        scanDetails.setCaretPosition(0)
    }

    private fun formatFinding(finding: ScanFinding, index: Int, total: Int): String =
        buildString {
            appendLine("Finding $index of $total")
            appendLine("${finding.severity.uppercase()} ${finding.ruleId}")
            appendLine("${finding.filePath}:${finding.line}")
            appendLine()
            appendLine(finding.message)
            if (finding.recommendation.isNotBlank()) {
                appendLine()
                appendLine("Recommendation:")
                appendLine(finding.recommendation)
            }
            if (finding.code.isNotBlank()) {
                appendLine()
                appendLine("Code:")
                appendLine(finding.code)
            }
            if (finding.translatedSql.isNotBlank()) {
                appendLine()
                appendLine("Translated SQL:")
                appendLine(finding.translatedSql)
            } else if (finding.sqlTranslationNote.isNotBlank()) {
                appendLine()
                appendLine("SQL note:")
                appendLine(finding.sqlTranslationNote)
            }
            if (finding.queryPlan.isNotBlank()) {
                appendLine()
                appendLine("Query plan:")
                appendLine(finding.queryPlan)
            } else if (finding.queryPlanNote.isNotBlank()) {
                appendLine()
                appendLine("Plan note:")
                appendLine(finding.queryPlanNote)
            }
            if (finding.savedNote.isNotBlank()) {
                appendLine()
                appendLine("Saved note:")
                appendLine(finding.savedNote)
            }
        }

    private fun buildScanPanel(): JPanel =
        JPanel(BorderLayout()).apply {
            background = PanelBackground
            val toolbar = JPanel(FlowLayout(FlowLayout.LEFT)).apply {
                background = ToolbarBackground
                border = BorderFactory.createEmptyBorder(6, 8, 6, 8)
                add(JButton("Previous").apply {
                    addActionListener { moveScanSelection(-1) }
                })
                add(JButton("Next").apply {
                    addActionListener { moveScanSelection(1) }
                })
                add(JButton("Go to code").apply {
                    addActionListener { openSelectedScanSource() }
                })
                add(JButton("Note").apply {
                    addActionListener { saveSelectedScanNote() }
                })
                add(JButton("Dismiss").apply {
                    addActionListener { dismissSelectedScanFinding() }
                })
                add(JButton("Copy Finding").apply {
                    addActionListener {
                        CopyPasteManager.getInstance().setContents(StringSelection(scanDetails.text))
                        status.text = "Copied finding to clipboard."
                    }
                })
                styleButtons(this)
            }
            add(toolbar, BorderLayout.NORTH)
            add(
                JBScrollPane(scanDetails).apply {
                    setVerticalScrollBarPolicy(javax.swing.ScrollPaneConstants.VERTICAL_SCROLLBAR_ALWAYS)
                    setHorizontalScrollBarPolicy(javax.swing.ScrollPaneConstants.HORIZONTAL_SCROLLBAR_AS_NEEDED)
                },
                BorderLayout.CENTER,
            )
        }

    private fun moveScanSelection(delta: Int) {
        val count = lastScan?.findings?.size ?: 0
        if (count == 0) return
        scanIndex = (scanIndex + delta + count) % count
        renderSelectedScanFinding()
    }

    private fun openSelectedScanSource() {
        val payload = lastScan ?: return
        val finding = payload.findings.getOrNull(scanIndex) ?: return
        val file = LocalFileSystem.getInstance().findFileByPath(finding.filePath) ?: return
        FileEditorManager.getInstance(project).openTextEditor(
            OpenFileDescriptor(project, file, (finding.line - 1).coerceAtLeast(0), 0),
            true,
        )
    }

    private fun selectedScanFinding(): Pair<Int, ScanFinding>? {
        val payload = lastScan ?: return null
        val row = scanIndex
        val finding = payload.findings.getOrNull(row) ?: return null
        return row to finding
    }

    private fun saveSelectedScanNote() {
        val (row, finding) = selectedScanFinding() ?: run {
            Messages.showWarningDialog(project, "Select a finding first.", "My EF Vibe")
            return
        }

        val note = Messages.showInputDialog(
            project,
            "Note for ${finding.ruleId}:",
            "Save Finding Note",
            null,
            finding.savedNote,
            null,
        )?.trim()

        if (note.isNullOrBlank()) return

        setBusy("Saving note...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val result = CliRunner(project).runScanNote(finding, note)
            SwingUtilities.invokeLater {
                if (result.succeeded) {
                    val payload = lastScan
                    if (payload != null) {
                        val updated = payload.findings.toMutableList()
                        updated[row] = finding.copy(savedNote = note)
                        renderScan(payload.copy(findings = updated), row)
                    }
                    status.text = "Note saved."
                } else {
                    appendOutput("Save note failed", result.stderr.ifBlank { result.stdout })
                    setReady()
                }
            }
        }
    }

    private fun dismissSelectedScanFinding() {
        val (row, finding) = selectedScanFinding() ?: run {
            Messages.showWarningDialog(project, "Select a finding first.", "My EF Vibe")
            return
        }

        val noteInput = JOptionPane.showInputDialog(
            this,
            "Optional dismissal note:",
            "Dismiss Finding",
            JOptionPane.QUESTION_MESSAGE,
        ) ?: return
        val note = noteInput.trim()

        setBusy("Dismissing finding...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val result = CliRunner(project).runScanDismiss(finding, note)
            SwingUtilities.invokeLater {
                if (result.succeeded) {
                    val payload = lastScan
                    if (payload != null) {
                        val updated = payload.findings.toMutableList().apply { removeAt(row) }
                        val next = if (updated.isEmpty()) 0 else row.coerceAtMost(updated.lastIndex)
                        renderScan(payload.copy(findings = updated, totalFindings = updated.size), next)
                    }
                    status.text = "Finding dismissed."
                } else {
                    appendOutput("Dismiss failed", result.stderr.ifBlank { result.stdout })
                    setReady()
                }
            }
        }
    }

    private fun buildNotebookPanel(): JPanel =
        JPanel(BorderLayout()).apply {
            background = PanelBackground
            val toolbar = JPanel(FlowLayout(FlowLayout.LEFT)).apply {
                background = ToolbarBackground
                border = BorderFactory.createEmptyBorder(6, 8, 6, 8)
                add(JButton("Open").apply {
                    addActionListener { openNotebook() }
                })
                add(JButton("Save").apply {
                    addActionListener { saveNotebook() }
                })
                add(JButton("Run All").apply {
                    addActionListener { runNotebook() }
                })
                styleButtons(this)
            }
            add(toolbar, BorderLayout.NORTH)
            add(JBScrollPane(notebookCells), BorderLayout.CENTER)
            add(JBScrollPane(notebookOutput), BorderLayout.SOUTH)
        }

    private fun openNotebook() {
        val chooser = JFileChooser().apply {
            fileFilter = javax.swing.filechooser.FileNameExtensionFilter("efvibe notebook", "efvibe-notebook")
        }
        if (chooser.showOpenDialog(this) != JFileChooser.APPROVE_OPTION) return

        val text = chooser.selectedFile.readText()
        notebookCells.text = runCatching {
            val root = JsonParser.parseString(text).asJsonObject
            root.getAsJsonArray("cells")
                .mapNotNull { cell -> cell.asJsonObject.get("value")?.asString }
                .joinToString("\n\n---\n")
        }.getOrElse { text }
        tabs.selectedIndex = tabs.indexOfTab("Notebook")
    }

    private fun saveNotebook() {
        val chooser = JFileChooser().apply {
            selectedFile = File("myefvibe.efvibe-notebook")
            fileFilter = javax.swing.filechooser.FileNameExtensionFilter("efvibe notebook", "efvibe-notebook")
        }
        if (chooser.showSaveDialog(this) != JFileChooser.APPROVE_OPTION) return

        val cells = notebookCells.text
            .split(Regex("""(?m)^\s*---\s*$"""))
            .map { it.trim() }
            .filter { it.isNotBlank() }
        val json = buildString {
            appendLine("{")
            appendLine("  \"cells\": [")
            cells.forEachIndexed { index, cell ->
                append("    { \"kind\": \"code\", \"languageId\": \"csharp\", \"value\": \"")
                append(jsonEscape(cell))
                append("\" }")
                if (index != cells.lastIndex) append(",")
                appendLine()
            }
            appendLine("  ]")
            appendLine("}")
        }
        chooser.selectedFile.writeText(json)
        status.text = "Saved notebook ${chooser.selectedFile.absolutePath}"
    }

    private fun runNotebook() {
        val cells = notebookCells.text
            .split(Regex("""(?m)^\s*---\s*$"""))
            .map { it.trim() }
            .filter { it.isNotBlank() }
        if (cells.isEmpty()) return

        setBusy("Running notebook cells...")
        ApplicationManager.getApplication().executeOnPooledThread {
            val output = buildString {
                cells.forEachIndexed { index, cell ->
                    appendLine("## Cell ${index + 1}")
                    when (cell.lowercase()) {
                        ":dbinfo" -> {
                            val (_, payload) = CliRunner(project).runDbInfoPayload()
                            appendLine(payload?.let { formatDbInfoText(it) } ?: "No DbInfo payload.")
                        }
                        ":tables" -> {
                            val (_, payload) = CliRunner(project).runTablesPayload()
                            appendLine(payload?.let { formatTablesText(it) } ?: "No tables payload.")
                        }
                        else -> {
                            val result = CliRunner(project).runExpressionPayload(cell, withPlan = false)
                            appendLine(result.payload?.let { formatEvaluationSummary(it) } ?: result.result.stderr.ifBlank { result.result.stdout })
                        }
                    }
                    appendLine()
                }
            }
            SwingUtilities.invokeLater {
                notebookOutput.text = output
                tabs.selectedIndex = tabs.indexOfTab("Notebook")
                setReady()
            }
        }
    }

    private fun renderHistory() {
        val service = project.service<EfvibeProjectService>()
        historyText.text = service.history.joinToString("\n\n") {
            "${it.expression}\n${formatEvaluationSummary(it.payload)}"
        }
    }

    private fun renderSession() {
        val settings = project.service<EfvibeSettingsService>().state
        sessionText.text = buildString {
            appendLine("Project: ${project.name}")
            appendLine("Base path: ${project.basePath.orEmpty()}")
            appendLine()
            appendLine("EF project: ${settings.project.ifBlank { "(auto)" }}")
            appendLine("Startup project: ${settings.startupProject.ifBlank { "(auto)" }}")
            appendLine("DbContext: ${settings.context.ifBlank { "(auto)" }}")
            appendLine("Workspace root: ${settings.workspaceRoot.ifBlank { "(default)" }}")
            appendLine("Provider: ${settings.provider.ifBlank { "(from project)" }}")
            appendLine("Tool path: ${settings.toolPath.ifBlank { "(PATH/local tool)" }}")
            appendLine("Framework: ${settings.dotnetFramework.ifBlank { "(default)" }}")
            appendLine("Database log: ${settings.dbLog}")
            appendLine()
            appendLine("Resolved REPL command:")
            appendLine(CliRunner(project).buildReplCommandLine())
        }
    }

    private fun formatEvaluationSummary(payload: EvaluationPayload): String =
        buildString {
            append(if (payload.success) "Success" else "Failed")
            append(" · ${payload.metrics.totalMs} ms")
            payload.metrics.rowCount?.let { append(" · $it row(s)") }
            if (!payload.error.isNullOrBlank()) append("\n${payload.error}")
            if (payload.rows.isNotEmpty()) {
                append("\n")
                payload.rows.take(10).forEach { appendLine(it.entries.joinToString(", ") { entry -> "${entry.key}=${entry.value}" }) }
            } else if (!payload.value.isNullOrBlank()) {
                append("\n${payload.value}")
            }
        }

    private fun formatDbInfoText(payload: DbInfoPayload): String =
        "DbContext: ${payload.dbContext}\n" + payload.entries.joinToString("\n") { "${it.key}: ${it.value}" }

    private fun formatTablesText(payload: TablesPayload): String =
        "DbContext: ${payload.dbContext}\n" + payload.tables.joinToString("\n") { "${it.dbSet} -> ${it.entityType}" }

    private fun copyActiveTab() {
        val title = tabs.getTitleAt(tabs.selectedIndex)
        val text = when (title) {
            "SQL" -> sqlText.text
            "Plan" -> planText.text
            "Messages" -> messagesText.text
            "Session" -> sessionText.text
            "Model" -> modelText.text
            "Scan Review" -> scanDetails.text
            "History" -> historyText.text
            "Notebook" -> notebookOutput.text
            else -> tableText(resultTable)
        }
        CopyPasteManager.getInstance().setContents(StringSelection(text))
        status.text = "Copied $title to clipboard."
    }

    private fun exportLast(format: String) {
        val payload = lastPayload ?: run {
            Messages.showWarningDialog(project, "Run a successful query before exporting.", "My EF Vibe")
            return
        }

        val content = if (format == "json") buildJson(payload) else buildCsv(payload)
        val chooser = JFileChooser().apply {
            selectedFile = File("myefvibe-export-${timestamp()}.$format")
        }
        if (chooser.showSaveDialog(this) == JFileChooser.APPROVE_OPTION) {
            chooser.selectedFile.writeText(content)
            status.text = "Exported ${chooser.selectedFile.absolutePath}"
        }
    }

    private fun buildCsv(payload: EvaluationPayload): String {
        val rows = payload.rows.ifEmpty { listOf(mapOf("value" to payload.value.orEmpty())) }
        val columns = rows.flatMap { it.keys }.distinct()
        return buildString {
            appendLine(columns.joinToString(",") { csvEscape(it) })
            rows.forEach { row -> appendLine(columns.joinToString(",") { csvEscape(row[it].orEmpty()) }) }
        }
    }

    private fun buildJson(payload: EvaluationPayload): String {
        val rows = payload.rows.ifEmpty { listOf(mapOf("value" to payload.value.orEmpty())) }
        return rows.joinToString(prefix = "[\n", postfix = "\n]\n", separator = ",\n") { row ->
            row.entries.joinToString(prefix = "  {", postfix = "}") {
                "\"${jsonEscape(it.key)}\": \"${jsonEscape(it.value)}\""
            }
        }
    }

    private fun setBusy(text: String) {
        status.text = text
    }

    private fun setReady() {
        status.text = "Ready"
    }
}

private fun readOnlyTextArea(): JTextArea =
    JTextArea().apply {
        isEditable = false
        lineWrap = false
        background = EditorBackground
        foreground = Foreground
        caretColor = Foreground
        font = Font(Font.MONOSPACED, Font.PLAIN, font.size)
        border = BorderFactory.createEmptyBorder(10, 12, 10, 12)
    }

private fun styleStatus(label: JLabel) {
    label.isOpaque = true
    label.background = StatusBackground
    label.foreground = StatusForeground
    label.border = BorderFactory.createCompoundBorder(
        BorderFactory.createMatteBorder(1, 0, 0, 0, BorderColor),
        BorderFactory.createEmptyBorder(6, 10, 6, 10),
    )
    label.font = label.font.deriveFont(Font.BOLD)
}

private fun styleTable(table: JTable) {
    table.background = TableBackground
    table.foreground = Foreground
    table.selectionBackground = SelectionBackground
    table.selectionForeground = SelectionForeground
    table.gridColor = BorderColor
    table.rowHeight = 26
    table.intercellSpacing = java.awt.Dimension(0, 1)
    table.showHorizontalLines = true
    table.showVerticalLines = false
    table.tableHeader.background = HeaderBackground
    table.tableHeader.foreground = HeaderForeground
    table.tableHeader.font = table.tableHeader.font.deriveFont(Font.BOLD)
    table.tableHeader.border = BorderFactory.createMatteBorder(0, 0, 1, 0, BorderColor)
    table.setDefaultRenderer(Object::class.java, ZebraTableRenderer())
}

private fun styleButtons(panel: JPanel) {
    for (component in panel.components) {
        if (component is JButton) {
            val kind = when (component.text) {
                "Run", "Run Plan", "Run All" -> ButtonKind.Primary
                "Dismiss" -> ButtonKind.Danger
                else -> ButtonKind.Secondary
            }
            styleButton(component, kind)
        }
    }
}

private fun styleButton(button: JButton, kind: ButtonKind) {
    button.isOpaque = true
    button.isContentAreaFilled = true
    button.isBorderPainted = false
    button.setFocusPainted(false)
    button.font = button.font.deriveFont(Font.BOLD, button.font.size2D)
    button.border = BorderFactory.createEmptyBorder(6, 12, 6, 12)
    when (kind) {
        ButtonKind.Primary -> {
            button.background = Accent
            button.foreground = Color.WHITE
        }
        ButtonKind.Secondary -> {
            button.background = ButtonBackground
            button.foreground = Foreground
        }
        ButtonKind.Danger -> {
            button.background = Danger
            button.foreground = Color.WHITE
        }
    }
}

private open class ZebraTableRenderer : DefaultTableCellRenderer() {
    override fun getTableCellRendererComponent(
        table: JTable,
        value: Any?,
        isSelected: Boolean,
        hasFocus: Boolean,
        row: Int,
        column: Int,
    ): Component {
        val component = super.getTableCellRendererComponent(table, value, isSelected, hasFocus, row, column)
        border = BorderFactory.createEmptyBorder(4, 8, 4, 8)
        if (!isSelected) {
            component.background = if (row % 2 == 0) TableBackground else TableStripeBackground
            component.foreground = Foreground
        }
        return component
    }
}

private class ScanSeverityRenderer : ZebraTableRenderer() {
    override fun getTableCellRendererComponent(
        table: JTable,
        value: Any?,
        isSelected: Boolean,
        hasFocus: Boolean,
        row: Int,
        column: Int,
    ): Component {
        val component = super.getTableCellRendererComponent(table, value, isSelected, hasFocus, row, column)
        if (!isSelected && column == 0) {
            val severity = value?.toString()?.lowercase().orEmpty()
            component.foreground = when (severity) {
                "critical", "error" -> Danger
                "warning" -> Warning
                "info" -> Info
                else -> Foreground
            }
            component.font = component.font.deriveFont(Font.BOLD)
        }
        return component
    }
}

private enum class ButtonKind {
    Primary,
    Secondary,
    Danger,
}

private val PanelBackground = JBColor(Color(0xF8FAFC), Color(0x1F232A))
private val HeaderBackground = JBColor(Color(0xEEF2FF), Color(0x2B2342))
private val ToolbarBackground = JBColor(Color(0xF1F5F9), Color(0x252A33))
private val EditorBackground = UIManager.getColor("TextArea.background") ?: JBColor(Color.WHITE, Color(0x1E1F22))
private val TableBackground = UIManager.getColor("Table.background") ?: JBColor(Color.WHITE, Color(0x1E1F22))
private val TableStripeBackground = JBColor(Color(0xF8FAFC), Color(0x25272D))
private val BorderColor = JBColor(Color(0xCBD5E1), Color(0x3C3F46))
private val ButtonBackground = JBColor(Color(0xE2E8F0), Color(0x3A3F4B))
private val SelectionBackground = JBColor(Color(0xDBEAFE), Color(0x3B4A6B))
private val SelectionForeground = UIManager.getColor("Table.selectionForeground") ?: JBColor(Color(0x0F172A), Color.WHITE)
private val Foreground = UIManager.getColor("Label.foreground") ?: JBColor(Color(0x0F172A), Color(0xE5E7EB))
private val HeaderForeground = JBColor(Color(0x312E81), Color(0xEDE9FE))
private val Accent = JBColor(Color(0x4F46E5), Color(0x7C3AED))
private val Danger = JBColor(Color(0xDC2626), Color(0xEF4444))
private val Warning = JBColor(Color(0xB45309), Color(0xFBBF24))
private val Info = JBColor(Color(0x2563EB), Color(0x60A5FA))
private val StatusBackground = JBColor(Color(0xECFDF5), Color(0x123524))
private val StatusForeground = JBColor(Color(0x047857), Color(0xA7F3D0))

private fun appendBlock(current: String, title: String, text: String): String {
    val separator = if (current.isBlank()) "" else "\n\n"
    return "$current$separator## $title\n$text"
}

private fun tableText(table: JTable): String {
    val model = table.model
    return buildString {
        val columns = (0 until model.columnCount).map { model.getColumnName(it) }
        appendLine(columns.joinToString("\t"))
        for (row in 0 until model.rowCount) {
            appendLine((0 until model.columnCount).joinToString("\t") { model.getValueAt(row, it)?.toString().orEmpty() })
        }
    }
}

private fun csvEscape(value: String): String =
    if (value.any { it == '"' || it == ',' || it == '\n' || it == '\r' }) {
        "\"${value.replace("\"", "\"\"")}\""
    } else {
        value
    }

private fun jsonEscape(value: String): String =
    value
        .replace("\\", "\\\\")
        .replace("\"", "\\\"")
        .replace("\n", "\\n")
        .replace("\r", "\\r")

private fun timestamp(): String =
    LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss"))
