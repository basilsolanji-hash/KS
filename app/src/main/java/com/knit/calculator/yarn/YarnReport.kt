package com.knit.calculator.yarn

import android.content.Context
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import com.knit.calculator.R
import com.knit.calculator.core.OrderUnit
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.core.YarnResult
import java.io.File
import java.math.BigDecimal
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Данные заказа, готовые для отчёта. */
data class YarnReportData(
    val form: YarnForm,
    val result: YarnResult,
    val createdAt: Date = Date(),
)

/** Формирует PDF-отчёт (A4) и текстовую сводку для письма. */
object YarnReport {
    private const val PAGE_WIDTH = 595 // A4 в пунктах
    private const val PAGE_HEIGHT = 842
    private const val MARGIN = 40f

    private val TURQUOISE = Color.parseColor("#30D5C8")
    private val DARK = Color.parseColor("#1E1E20")
    private val GRAY = Color.parseColor("#D1D5DB")
    private val MUTED = Color.argb(170, 0x1E, 0x1E, 0x20)
    private val ROW_TINT = Color.argb(70, 0xD1, 0xD5, 0xDB)

    fun fileName(data: YarnReportData): String {
        val stamp = SimpleDateFormat("yyyyMMdd_HHmm", Locale.US).format(data.createdAt)
        val order = data.form.orderNumber.filter { it.isLetterOrDigit() || it == '-' || it == '_' }.take(30)
        return if (order.isEmpty()) "Rashod_pryazhi_$stamp.pdf" else "Rashod_pryazhi_${order}_$stamp.pdf"
    }

    fun createPdf(context: Context, data: YarnReportData): File {
        val dir = File(context.cacheDir, "reports").apply { mkdirs() }
        // Старые отчёты больше не нужны: держим в кэше только последний.
        dir.listFiles()?.forEach { it.delete() }
        val file = File(dir, fileName(data))
        val document = PdfDocument()
        try {
            PdfWriter(context, document, data).draw()
            file.outputStream().use { document.writeTo(it) }
        } finally {
            document.close()
        }
        return file
    }

    fun subject(context: Context, data: YarnReportData): String {
        val parts = listOfNotNull(
            context.getString(R.string.yarn_report_title),
            data.form.orderNumber.takeIf { it.isNotBlank() }?.let { "№ $it" },
            data.form.productName.takeIf { it.isNotBlank() },
        )
        return parts.joinToString(" — ")
    }

    /** Текстовая сводка: для тела письма и копирования. */
    fun summaryText(context: Context, data: YarnReportData): String = buildString {
        val r = data.result
        appendLine(subject(context, data))
        appendLine(context.getString(R.string.yarn_report_date, dateText(data.createdAt)))
        appendLine()
        infoRows(context, data).forEach { (k, v) -> appendLine("$k: $v") }
        appendLine()
        r.lines.forEach { line ->
            appendLine(
                "${line.name} (${pct(line.percent)} %): " +
                    "${YarnCalculator.format(line.gramsPerItem, 1)} г/шт, " +
                    "${YarnCalculator.format(line.orderKg, 3)} кг на заказ",
            )
        }
        appendLine()
        appendLine(context.getString(R.string.yarn_total_line, YarnCalculator.format(r.totalKg, 3), YarnCalculator.format(r.wasteKg, 3)))
    }

    private fun dateText(date: Date) = SimpleDateFormat("dd.MM.yyyy HH:mm", Locale.getDefault()).format(date)

    private fun pct(value: BigDecimal) = YarnCalculator.formatCompact(value, 2)

    private fun infoRows(context: Context, data: YarnReportData): List<Pair<String, String>> {
        val f = data.form
        val r = data.result
        val order = when (f.orderUnit) {
            OrderUnit.PIECES -> context.getString(R.string.yarn_report_order_pieces, YarnCalculator.formatCompact(r.pieces, 2), YarnCalculator.format(r.productKg, 3))
            OrderUnit.KILOGRAMS -> context.getString(R.string.yarn_report_order_kg, YarnCalculator.format(r.productKg, 3), YarnCalculator.formatCompact(r.pieces, 1))
        }
        return listOfNotNull(
            f.productName.takeIf { it.isNotBlank() }?.let { context.getString(R.string.yarn_product) to it },
            f.orderNumber.takeIf { it.isNotBlank() }?.let { context.getString(R.string.yarn_order_number) to it },
            context.getString(R.string.yarn_item_weight) to "${YarnCalculator.formatCompact(r.lines.fold(BigDecimal.ZERO) { a, l -> a + l.gramsPerItemNet }, 2)} г",
            context.getString(R.string.yarn_order) to order,
            context.getString(R.string.yarn_waste) to "${pct(YarnCalculator.parseDecimal(f.waste) ?: BigDecimal.ZERO)} %",
        )
    }

    private class PdfWriter(
        private val context: Context,
        private val document: PdfDocument,
        private val data: YarnReportData,
    ) {
        private var pageNumber = 0
        private lateinit var page: PdfDocument.Page
        private var y = 0f

        private val title = paint(18f, bold = true)
        private val normal = paint(10.5f)
        private val bold = paint(10.5f, bold = true)
        private val muted = paint(9f).apply { color = MUTED }
        private val fill = Paint().apply { style = Paint.Style.FILL }
        private val line = Paint().apply { color = GRAY; strokeWidth = 0.8f }

        private fun paint(size: Float, bold: Boolean = false) = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            textSize = size
            color = DARK
            typeface = Typeface.create(Typeface.SANS_SERIF, if (bold) Typeface.BOLD else Typeface.NORMAL)
        }

        private val canvas get() = page.canvas
        private val contentWidth = PAGE_WIDTH - 2 * MARGIN

        // Колонки таблицы: название и пять числовых колонок.
        private val columnWidths = floatArrayOf(155f, 55f, 70f, 70f, 82f, 83f)
        private val headers = listOf(
            R.string.yarn_col_yarn, R.string.yarn_col_percent, R.string.yarn_col_net_g,
            R.string.yarn_col_gross_g, R.string.yarn_col_net_kg, R.string.yarn_col_gross_kg,
        ).map { context.getString(it) }

        fun draw() {
            newPage()
            header()
            info()
            table()
            summary()
            document.finishPage(page)
        }

        private fun newPage() {
            if (pageNumber > 0) document.finishPage(page)
            pageNumber++
            page = document.startPage(PdfDocument.PageInfo.Builder(PAGE_WIDTH, PAGE_HEIGHT, pageNumber).create())
            y = MARGIN
            fill.color = TURQUOISE
            canvas.drawRect(0f, 0f, PAGE_WIDTH.toFloat(), 6f, fill)
            val footer = context.getString(R.string.yarn_report_footer, pageNumber)
            canvas.drawText(footer, MARGIN, PAGE_HEIGHT - 24f, muted)
        }

        private fun ensureSpace(height: Float) {
            if (y + height > PAGE_HEIGHT - MARGIN - 20f) newPage()
        }

        private fun header() {
            y += 14f
            canvas.drawText(context.getString(R.string.yarn_report_title), MARGIN, y, title)
            val date = context.getString(R.string.yarn_report_date, dateText(data.createdAt))
            canvas.drawText(date, PAGE_WIDTH - MARGIN - muted.measureText(date), y, muted)
            y += 16f
            canvas.drawText(context.getString(R.string.app_name), MARGIN, y, muted)
            y += 22f
        }

        private fun info() {
            infoRows(context, data).forEach { (label, value) ->
                ensureSpace(16f)
                canvas.drawText("$label:", MARGIN, y, muted)
                canvas.drawText(fit(value, contentWidth - 130f, bold), MARGIN + 130f, y, bold)
                y += 16f
            }
            y += 12f
        }

        private fun table() {
            val rowHeight = 22f
            ensureSpace(rowHeight * 3)
            fill.color = TURQUOISE
            canvas.drawRect(MARGIN, y, MARGIN + contentWidth, y + rowHeight + 6f, fill)
            drawRow(headers, y, rowHeight + 6f, paint(8.5f, bold = true))
            y += rowHeight + 6f

            data.result.lines.forEachIndexed { index, l ->
                ensureSpace(rowHeight)
                if (index % 2 == 1) {
                    fill.color = ROW_TINT
                    canvas.drawRect(MARGIN, y, MARGIN + contentWidth, y + rowHeight, fill)
                }
                drawRow(
                    listOf(
                        l.name,
                        YarnCalculator.formatCompact(l.percent, 2),
                        YarnCalculator.format(l.gramsPerItemNet, 1),
                        YarnCalculator.format(l.gramsPerItem, 1),
                        YarnCalculator.format(l.orderKgNet, 3),
                        YarnCalculator.format(l.orderKg, 3),
                    ),
                    y, rowHeight, normal,
                )
                y += rowHeight
            }

            ensureSpace(rowHeight)
            canvas.drawLine(MARGIN, y, MARGIN + contentWidth, y, line)
            val r = data.result
            drawRow(
                listOf(
                    context.getString(R.string.yarn_total),
                    "100",
                    YarnCalculator.format(r.lines.fold(BigDecimal.ZERO) { a, l -> a + l.gramsPerItemNet }, 1),
                    YarnCalculator.format(r.gramsPerItem, 1),
                    YarnCalculator.format(r.totalKgNet, 3),
                    YarnCalculator.format(r.totalKg, 3),
                ),
                y, rowHeight, bold,
            )
            y += rowHeight + 18f
        }

        private fun summary() {
            val r = data.result
            ensureSpace(80f)
            fill.color = ROW_TINT
            canvas.drawRect(MARGIN, y, MARGIN + contentWidth, y + 48f, fill)
            fill.color = TURQUOISE
            canvas.drawRect(MARGIN, y, MARGIN + 4f, y + 48f, fill)
            val big = paint(14f, bold = true)
            canvas.drawText(context.getString(R.string.yarn_report_total, YarnCalculator.format(r.totalKg, 3)), MARGIN + 14f, y + 20f, big)
            canvas.drawText(
                context.getString(R.string.yarn_report_waste, YarnCalculator.format(r.wasteKg, 3), YarnCalculator.format(r.totalKgNet, 3)),
                MARGIN + 14f, y + 38f, normal,
            )
            y += 68f
            context.getString(R.string.yarn_report_method).split('\n').forEach {
                ensureSpace(14f)
                canvas.drawText(it, MARGIN, y, muted)
                y += 13f
            }
        }

        private fun drawRow(cells: List<String>, top: Float, height: Float, p: Paint) {
            var x = MARGIN
            val baseline = top + height / 2 + p.textSize / 3
            cells.forEachIndexed { i, text ->
                val w = columnWidths[i]
                val t = fit(text, w - 10f, p)
                if (i == 0) {
                    canvas.drawText(t, x + 6f, baseline, p)
                } else {
                    canvas.drawText(t, x + w - 6f - p.measureText(t), baseline, p)
                }
                x += w
            }
        }

        /** Обрезает текст с многоточием, чтобы он поместился в ширину. */
        private fun fit(text: String, width: Float, p: Paint): String {
            if (p.measureText(text) <= width) return text
            val count = p.breakText(text, true, width - p.measureText("…"), null)
            return text.take(count) + "…"
        }
    }
}
