package com.knit.calculator.quote

import android.content.Context
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import com.knit.calculator.R
import com.knit.calculator.report.BrandLogo
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.QuoteLine
import com.knit.calculator.core.QuoteTotals
import com.knit.calculator.core.YarnCalculator
import java.io.File
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

/** Всё, что нужно для КП: реквизиты, клиент, позиции и итоги. */
data class QuoteDocument(
    val settings: CompanySettings,
    val draft: QuoteDraft,
    val totals: QuoteTotals,
    val createdAt: Date = Date(),
    /** Фото образцов по позициям [totals].lines (путь к файлу или `null`). */
    val photoPaths: List<String?> = emptyList(),
)

/** Формирует коммерческое предложение в PDF (A4) и текст письма. */
object QuotePdf {
    private const val PAGE_WIDTH = 595
    private const val PAGE_HEIGHT = 842
    private const val MARGIN = 40f

    private val TURQUOISE = Color.parseColor("#30D5C8")
    private val DARK = Color.parseColor("#1E1E20")
    private val GRAY = Color.parseColor("#D1D5DB")
    private val MUTED = Color.argb(175, 0x1E, 0x1E, 0x20)
    private val ROW_TINT = Color.argb(70, 0xD1, 0xD5, 0xDB)

    private fun date(d: Date) = SimpleDateFormat("dd.MM.yyyy", Locale.getDefault()).format(d)

    fun validUntil(doc: QuoteDocument): Date? {
        val days = YarnCalculator.parseDecimal(doc.settings.validityDays)?.toInt()?.takeIf { it > 0 } ?: return null
        return Calendar.getInstance().apply {
            time = doc.createdAt
            add(Calendar.DAY_OF_YEAR, days)
        }.time
    }

    fun subject(context: Context, doc: QuoteDocument): String =
        if (doc.draft.number > 0) context.getString(R.string.kp_subject, doc.draft.number, doc.settings.brand)
        else context.getString(R.string.kp_subject_no_number, doc.settings.brand)

    fun fileName(doc: QuoteDocument): String {
        val stamp = SimpleDateFormat("yyyyMMdd", Locale.US).format(doc.createdAt)
        return if (doc.draft.number > 0) "KP_${doc.draft.number}_KS_$stamp.pdf" else "KP_KS_$stamp.pdf"
    }

    fun vatLabel(context: Context, doc: QuoteDocument): String {
        val rate = YarnCalculator.formatCompact(doc.settings.vat().ratePercent, 2)
        return context.getString(if (doc.settings.vatIncluded) R.string.quote_vat_included else R.string.quote_vat, rate)
    }

    /** Текст письма/сообщения с кратким содержанием КП. */
    fun emailText(context: Context, doc: QuoteDocument): String = buildString {
        val s = doc.settings
        val t = doc.totals
        appendLine(context.getString(R.string.kp_email_greeting))
        appendLine()
        appendLine(
            if (doc.draft.number > 0) context.getString(R.string.kp_email_body, doc.draft.number)
            else context.getString(R.string.kp_email_body_no_number),
        )
        appendLine()
        t.lines.forEachIndexed { i, l ->
            appendLine(
                "${i + 1}. ${l.description} — ${QuoteCalculator.formatQuantity(l.quantity)} ${l.product.unit} × " +
                    "${QuoteCalculator.formatMoney(l.unitPrice)} ₽ = ${QuoteCalculator.formatMoney(l.total)} ₽",
            )
        }
        appendLine()
        appendLine("${context.getString(R.string.quote_total)}: ${QuoteCalculator.formatMoney(t.total)} ₽ (${vatLabel(context, doc)}: ${QuoteCalculator.formatMoney(t.vat)} ₽)")
        if (s.leadTime.isNotBlank()) appendLine(context.getString(R.string.kp_lead_time, s.leadTime))
        deliveryText(t, s).takeIf { it.isNotBlank() }?.let { appendLine(context.getString(R.string.kp_delivery, it.replaceFirstChar(Char::lowercaseChar))) }
        validUntil(doc)?.let { appendLine(context.getString(R.string.kp_validity, s.validityDays, date(it))) }
        appendLine()
        appendLine(s.signature)
        // Название уже в подписи — добавляем только город и контакты.
        appendLine(listOf(s.city, s.phone, s.email, s.website).filter { it.isNotBlank() }.joinToString(" · "))
        if (s.emailDisclaimer.isNotBlank()) {
            appendLine()
            appendLine(s.emailDisclaimer.trim())
        }
    }

    fun create(context: Context, doc: QuoteDocument): File {
        val dir = File(context.cacheDir, "reports").apply { mkdirs() }
        dir.listFiles()?.forEach { it.delete() }
        val file = File(dir, fileName(doc))
        val pdf = PdfDocument()
        try {
            Writer(context, pdf, doc).draw()
            file.outputStream().use { pdf.writeTo(it) }
        } finally {
            pdf.close()
        }
        return file
    }

    private class Writer(private val context: Context, private val pdf: PdfDocument, private val doc: QuoteDocument) {
        private var pageNumber = 0
        private lateinit var page: PdfDocument.Page
        private var y = 0f
        private val canvas get() = page.canvas
        private val width = PAGE_WIDTH - 2 * MARGIN

        private fun paint(size: Float, bold: Boolean = false, color: Int = DARK) = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            textSize = size
            this.color = color
            typeface = Typeface.create(Typeface.SANS_SERIF, if (bold) Typeface.BOLD else Typeface.NORMAL)
        }

        private val brand = paint(17f, bold = true)
        private val title = paint(15f, bold = true)
        private val normal = paint(10f)
        private val bold = paint(10f, bold = true)
        private val small = paint(8.5f, color = MUTED)
        private val muted = paint(9.5f, color = MUTED)
        private val header = paint(8.5f, bold = true)
        private val fill = Paint().apply { style = Paint.Style.FILL }
        private val rule = Paint().apply { color = GRAY; strokeWidth = 0.8f }

        // № | Наименование | Кол-во | Ед. | Цена | Сумма — всего 515 pt.
        private val cols = floatArrayOf(24f, 226f, 62f, 38f, 75f, 90f)
        // В PDF фото 54 pt — хватает копии до 320 px (целые фото с камеры переполняют память).
        private val photos = doc.photoPaths.map { PhotoStore.loadScaled(it, 320) }

        fun draw() {
            newPage()
            companyHeader()
            titleBlock()
            table()
            totals()
            conditions()
            legal()
            requisites()
            signature()
            pdf.finishPage(page)
        }

        private fun newPage() {
            if (pageNumber > 0) pdf.finishPage(page)
            pageNumber++
            page = pdf.startPage(PdfDocument.PageInfo.Builder(PAGE_WIDTH, PAGE_HEIGHT, pageNumber).create())
            y = MARGIN
            fill.color = TURQUOISE
            canvas.drawRect(0f, 0f, PAGE_WIDTH.toFloat(), 8f, fill)
            // Реквизиты уже есть в шапке — в колонтитуле только номер страницы.
            val footer = context.getString(R.string.kp_footer, pageNumber)
            canvas.drawLine(MARGIN, PAGE_HEIGHT - 36f, PAGE_WIDTH - MARGIN, PAGE_HEIGHT - 36f, rule)
            canvas.drawText(fit(footer, width, small), MARGIN, PAGE_HEIGHT - 22f, small)
        }

        private fun ensure(height: Float) {
            if (y + height > PAGE_HEIGHT - MARGIN - 24f) newPage()
        }

        private fun companyHeader() {
            val s = doc.settings
            val logoWidth = BrandLogo.draw(context, canvas, MARGIN, y, 44f)
            val textLeft = MARGIN + if (logoWidth > 0) logoWidth + 14f else 0f
            y += 18f
            canvas.drawText(fit(s.brand, width * 0.6f - (textLeft - MARGIN), brand), textLeft, y, brand)
            val right = listOf(s.phone, s.email, s.website).filter { it.isNotBlank() }
            var ry = y - 4f
            right.forEach {
                canvas.drawText(it, PAGE_WIDTH - MARGIN - normal.measureText(it), ry, normal)
                ry += 13f
            }
            y += 16f
            // Строки под контактами справа могут занимать всю ширину.
            val contactsBottom = ry - 13f
            fun headerLine(text: String) {
                if (text.isBlank()) return
                val available = if (y > contactsBottom + 4f) PAGE_WIDTH - MARGIN - textLeft else width * 0.6f - (textLeft - MARGIN)
                canvas.drawText(fit(text, available, muted), textLeft, y, muted)
                y += 13f
            }
            headerLine(
                listOf(s.legalName, s.inn.takeIf { it.isNotBlank() }?.let { context.getString(R.string.kp_inn, it) })
                    .filter { !it.isNullOrBlank() }.joinToString(", "),
            )
            headerLine(
                listOf(
                    s.kpp.takeIf { it.isNotBlank() }?.let { context.getString(R.string.kp_kpp, it) },
                    s.ogrn.takeIf { it.isNotBlank() }?.let { context.getString(R.string.kp_ogrn, it) },
                ).filterNotNull().joinToString(", "),
            )
            headerLine(if (s.factAddress.isNotBlank()) context.getString(R.string.kp_fact_address, s.factAddress) else s.city)
            y = maxOf(y, ry, MARGIN + 44f) + 8f
            canvas.drawLine(MARGIN, y, PAGE_WIDTH - MARGIN, y, rule)
            y += 28f
        }

        private fun titleBlock() {
            val heading = if (doc.draft.number > 0) context.getString(R.string.kp_title, doc.draft.number, date(doc.createdAt))
            else context.getString(R.string.kp_title_no_number, date(doc.createdAt))
            canvas.drawText(heading, MARGIN, y, title)
            y += 22f
            val client = listOf(
                doc.draft.clientCompany,
                doc.draft.clientInn.takeIf { it.isNotBlank() }?.let { context.getString(R.string.kp_inn_client, it) }.orEmpty(),
                doc.draft.clientContact,
                doc.draft.clientPhone,
            ).filter { it.isNotBlank() }.joinToString(", ")
            if (client.isNotBlank()) {
                wrap(context.getString(R.string.kp_to, client), width, bold).forEach {
                    canvas.drawText(it, MARGIN, y, bold)
                    y += 14f
                }
                y += 4f
            }
            wrap(context.getString(R.string.kp_intro), width, normal).forEach {
                canvas.drawText(it, MARGIN, y, normal)
                y += 14f
            }
            y += 8f
        }

        private fun table() {
            val headers = listOf(R.string.kp_col_no, R.string.kp_col_name, R.string.kp_col_qty, R.string.kp_col_unit, R.string.kp_col_price, R.string.kp_col_sum)
                .map { context.getString(it) }
            ensure(60f)
            fill.color = TURQUOISE
            canvas.drawRect(MARGIN, y, MARGIN + width, y + 24f, fill)
            drawCells(headers.map { listOf(it) }, y, 24f, header)
            y += 24f

            doc.totals.lines.forEachIndexed { i, line ->
                val extras = extraInfo(line)
                val photo = photos.getOrNull(i)
                val photoSize = 54f
                val nameWidth = cols[1] - 12f - if (photo != null) photoSize + 8f else 0f
                val nameLines = wrap(line.description, nameWidth, normal) + extras
                val rowHeight = maxOf(nameLines.size * 13f + 10f, if (photo != null) photoSize + 10f else 0f)
                ensure(rowHeight)
                if (i % 2 == 1) {
                    fill.color = ROW_TINT
                    canvas.drawRect(MARGIN, y, MARGIN + width, y + rowHeight, fill)
                }
                drawCells(
                    listOf(
                        listOf("${i + 1}"),
                        nameLines,
                        listOf(QuoteCalculator.formatQuantity(line.quantity)),
                        listOf(line.product.unit),
                        listOf(QuoteCalculator.formatMoney(line.unitPrice)),
                        listOf(QuoteCalculator.formatMoney(line.total)),
                    ),
                    y, rowHeight, normal,
                    smallTail = extras.size,
                    nameOffset = if (photo != null) photoSize + 8f else 0f,
                )
                if (photo != null) {
                    val left = MARGIN + cols[0] + 5f
                    val side = minOf(photo.width, photo.height)
                    val src = android.graphics.Rect((photo.width - side) / 2, (photo.height - side) / 2, (photo.width + side) / 2, (photo.height + side) / 2)
                    canvas.drawBitmap(photo, src, android.graphics.RectF(left, y + 5f, left + photoSize, y + 5f + photoSize), null)
                }
                y += rowHeight
            }
            canvas.drawLine(MARGIN, y, MARGIN + width, y, rule)
            y += 16f
        }

        // Внутренние коэффициенты клиенту не показываем — только скидку и разовую подготовку.
        private fun extraInfo(line: QuoteLine): List<String> = buildList {
            if (line.discountPercent.signum() > 0) {
                add(context.getString(R.string.kp_discount, YarnCalculator.formatCompact(line.discountPercent, 2)))
            }
            if (line.setupFee.signum() > 0) {
                add(context.getString(R.string.quote_setup, QuoteCalculator.formatMoney(line.setupFee)))
            }
        }

        private fun totals() {
            val t = doc.totals
            val rows = buildList {
                if (!doc.settings.vatIncluded) add(context.getString(R.string.quote_sum_without_vat) to QuoteCalculator.formatMoney(t.subtotal))
                add(vatLabel(context, doc) to QuoteCalculator.formatMoney(t.vat))
            }
            ensure(rows.size * 15f + 40f)
            val labelRight = PAGE_WIDTH - MARGIN - 110f
            rows.forEach { (label, value) ->
                canvas.drawText(label, labelRight - normal.measureText(label), y, normal)
                canvas.drawText("$value ₽", PAGE_WIDTH - MARGIN - normal.measureText("$value ₽"), y, normal)
                y += 15f
            }
            y += 4f
            val totalPaint = paint(13f, bold = true)
            val label = context.getString(R.string.quote_total)
            val value = "${QuoteCalculator.formatMoney(t.total)} ₽"
            fill.color = TURQUOISE
            val boxLeft = PAGE_WIDTH - MARGIN - 250f
            canvas.drawRect(boxLeft, y - 15f, PAGE_WIDTH - MARGIN, y + 9f, fill)
            canvas.drawText(label, boxLeft + 10f, y + 1f, totalPaint)
            canvas.drawText(value, PAGE_WIDTH - MARGIN - 10f - totalPaint.measureText(value), y + 1f, totalPaint)
            y += 36f
        }

        private fun conditions() {
            val s = doc.settings
            val lines = buildList {
                if (s.leadTime.isNotBlank()) add(context.getString(R.string.kp_lead_time, s.leadTime))
                deliveryText(doc.totals, s).takeIf { it.isNotBlank() }?.let { add(context.getString(R.string.kp_delivery, it.replaceFirstChar(Char::lowercaseChar))) }
                validUntil(doc)?.let { add(context.getString(R.string.kp_validity, s.validityDays.trim(), date(it))) }
                if (s.terms.isNotBlank()) add(s.terms)
                if (doc.draft.comment.isNotBlank()) add(doc.draft.comment)
            }
            lines.forEach { text ->
                wrap(text, width, normal).forEach {
                    ensure(14f)
                    canvas.drawText(it, MARGIN, y, normal)
                    y += 14f
                }
            }
            y += 20f
        }

        /** Юридические условия — мелким шрифтом, по пунктам. */
        private fun legal() {
            val terms = doc.settings.legalTerms
            if (terms.isEmpty()) return
            ensure(40f)
            canvas.drawText(context.getString(R.string.kp_legal_title), MARGIN, y, bold)
            y += 14f
            terms.forEachIndexed { i, term ->
                val prefix = "${i + 1}. "
                val indent = small.measureText(prefix)
                wrap(term, width - indent, small).forEachIndexed { li, line ->
                    ensure(11f)
                    if (li == 0) canvas.drawText(prefix, MARGIN, y, small)
                    canvas.drawText(line, MARGIN + indent, y, small)
                    y += 11f
                }
                y += 2f
            }
            y += 12f
        }

        /** Реквизиты для договора и счёта. ИНН, КПП и ОГРН — уже в шапке. */
        private fun requisites() {
            val s = doc.settings
            val lines = listOfNotNull(
                s.legalAddress.takeIf { it.isNotBlank() }?.let { context.getString(R.string.kp_legal_address, it) },
                s.account.takeIf { it.isNotBlank() }?.let {
                    listOf(
                        context.getString(R.string.kp_account, it, s.bank),
                        s.bik.takeIf { b -> b.isNotBlank() }?.let { b -> context.getString(R.string.kp_bik, b) },
                        s.corrAccount.takeIf { c -> c.isNotBlank() }?.let { c -> context.getString(R.string.kp_corr, c) },
                    ).filterNotNull().joinToString(", ")
                },
            )
            if (lines.isEmpty()) return
            ensure(40f)
            canvas.drawText(context.getString(R.string.kp_requisites_title), MARGIN, y, bold)
            y += 14f
            lines.forEach { text ->
                wrap(text, width, small).forEach {
                    ensure(11f)
                    canvas.drawText(it, MARGIN, y, small)
                    y += 11f
                }
            }
            y += 16f
        }

        private fun signature() {
            val s = doc.settings
            ensure(60f)
            // Контакты не повторяем: они в шапке КП.
            canvas.drawText(fit(s.signature, width, bold), MARGIN, y, bold)
            y += 15f
            if (s.director.isNotBlank()) {
                canvas.drawText(fit(context.getString(R.string.kp_director, s.legalName, s.director), width, normal), MARGIN, y, normal)
                y += 14f
            }
        }

        /** Ячейки строки: первая колонка «№» и название — слева, числа — справа. */
        private fun drawCells(cells: List<List<String>>, top: Float, height: Float, p: Paint, smallTail: Int = 0, nameOffset: Float = 0f) {
            var x = MARGIN
            cells.forEachIndexed { i, lines ->
                val w = cols[i]
                var ly = top + 15f
                if (lines.size == 1) ly = top + height / 2 + p.textSize / 3
                lines.forEachIndexed { li, raw ->
                    // Скидка и подготовка — мелким шрифтом под названием.
                    val lp = if (i == 1 && li >= lines.size - smallTail) small else p
                    val t = fit(raw, w - 10f - if (i == 1) nameOffset else 0f, lp)
                    val tx = when {
                        i == 1 -> x + 5f + nameOffset
                        i == 0 -> x + 5f
                        else -> x + w - 5f - lp.measureText(t)
                    }
                    canvas.drawText(t, tx, ly, lp)
                    ly += 13f
                }
                x += w
            }
        }

        private fun wrap(text: String, maxWidth: Float, p: Paint): List<String> {
            val result = mutableListOf<String>()
            var current = ""
            text.split(' ').forEach { word ->
                val candidate = if (current.isEmpty()) word else "$current $word"
                if (p.measureText(candidate) <= maxWidth) {
                    current = candidate
                } else {
                    if (current.isNotEmpty()) result += current
                    current = word
                }
            }
            if (current.isNotEmpty()) result += current
            return result.ifEmpty { listOf("") }
        }

        private fun fit(text: String, maxWidth: Float, p: Paint): String {
            if (p.measureText(text) <= maxWidth) return text
            val count = p.breakText(text, true, maxWidth - p.measureText("…"), null)
            return text.take(count) + "…"
        }
    }
}
