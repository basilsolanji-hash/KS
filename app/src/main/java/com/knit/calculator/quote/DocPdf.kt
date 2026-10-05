package com.knit.calculator.quote

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.graphics.pdf.PdfDocument
import com.knit.calculator.core.ContractTemplate
import com.knit.calculator.core.Invoice
import com.knit.calculator.core.MoneyWords
import com.knit.calculator.core.Product
import com.knit.calculator.core.QrCode
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.report.BrandLogo
import java.io.File
import java.math.BigDecimal
import java.math.RoundingMode
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Сделка для документов: клиент и позиции сохранённого КП с ценами на момент сохранения. */
data class DealDoc(
    val quoteId: String,
    val quoteNumber: Int,
    val quoteDate: String,
    val client: String,
    val clientInn: String,
    val clientEmail: String,
    val lines: List<SnapshotLine>,
    /** Всего к оплате с НДС. */
    val total: BigDecimal,
    val vat: BigDecimal,
    val clientKpp: String = "",
    val clientAddress: String = "",
)

/** Простая вёрстка A4: страницы, текст с переносом, таблицы. Общая для счёта, договора и прайс-листа. */
internal class PageWriter(private val pdf: PdfDocument, private val footer: (Int) -> String) {
    var pageNumber = 0
        private set
    private lateinit var page: PdfDocument.Page
    val canvas: Canvas get() = page.canvas
    var y = 0f
    val width = PAGE_WIDTH - 2 * MARGIN

    fun paint(size: Float, bold: Boolean = false, color: Int = DARK) = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        textSize = size
        this.color = color
        typeface = Typeface.create(Typeface.SANS_SERIF, if (bold) Typeface.BOLD else Typeface.NORMAL)
    }

    val normal = paint(10f)
    val bold = paint(10f, bold = true)
    val small = paint(8.5f, color = MUTED)
    val title = paint(15f, bold = true)
    val header = paint(8.5f, bold = true)
    val fill = Paint().apply { style = Paint.Style.FILL }
    val rule = Paint().apply { color = GRAY; strokeWidth = 0.8f; style = Paint.Style.STROKE }

    fun newPage() {
        if (pageNumber > 0) pdf.finishPage(page)
        pageNumber++
        page = pdf.startPage(PdfDocument.PageInfo.Builder(PAGE_WIDTH, PAGE_HEIGHT, pageNumber).create())
        y = MARGIN
        fill.color = TURQUOISE
        canvas.drawRect(0f, 0f, PAGE_WIDTH.toFloat(), 8f, fill)
        canvas.drawLine(MARGIN, PAGE_HEIGHT - 36f, PAGE_WIDTH - MARGIN, PAGE_HEIGHT - 36f, rule)
        canvas.drawText(fit(footer(pageNumber), width, small), MARGIN, PAGE_HEIGHT - 22f, small)
    }

    fun finish() = pdf.finishPage(page)

    fun ensure(height: Float) {
        if (y + height > PAGE_HEIGHT - MARGIN - 24f) newPage()
    }

    /** Абзац с переносом строк. */
    fun paragraph(text: String, p: Paint = normal, indent: Float = 0f, lineHeight: Float = p.textSize * 1.4f, after: Float = 4f) {
        wrap(text, width - indent, p).forEach {
            ensure(lineHeight)
            canvas.drawText(it, MARGIN + indent, y, p)
            y += lineHeight
        }
        y += after
    }

    /** Таблица: первая колонка «№», вторая — текст с переносом, остальные — числа справа. */
    fun table(widths: FloatArray, headers: List<String>, rows: List<List<String>>) {
        ensure(48f)
        fill.color = TURQUOISE
        canvas.drawRect(MARGIN, y, MARGIN + width, y + 22f, fill)
        drawRow(widths, headers.map { listOf(it) }, y, 22f, header)
        y += 22f
        rows.forEachIndexed { i, row ->
            val cells = row.mapIndexed { c, t -> if (c == 1) wrap(t, widths[1] - 10f, normal) else listOf(t) }
            val h = cells.maxOf { it.size } * 13f + 9f
            ensure(h)
            if (i % 2 == 1) {
                fill.color = ROW_TINT
                canvas.drawRect(MARGIN, y, MARGIN + width, y + h, fill)
            }
            drawRow(widths, cells, y, h, normal)
            y += h
        }
        canvas.drawLine(MARGIN, y, MARGIN + width, y, rule)
        y += 14f
    }

    private fun drawRow(widths: FloatArray, cells: List<List<String>>, top: Float, height: Float, p: Paint) {
        var x = MARGIN
        cells.forEachIndexed { i, lines ->
            val w = widths[i]
            var ly = if (lines.size == 1) top + height / 2 + p.textSize / 3 else top + 14f
            lines.forEach { raw ->
                val t = fit(raw, w - 10f, p)
                val tx = if (i <= 1) x + 5f else x + w - 5f - p.measureText(t)
                canvas.drawText(t, tx, ly, p)
                ly += 13f
            }
            x += w
        }
    }

    /** Строка «подпись — значение» справа (итоги). */
    fun totalLine(label: String, value: String, p: Paint = normal) {
        ensure(16f)
        val right = PAGE_WIDTH - MARGIN
        canvas.drawText(value, right - p.measureText(value), y, p)
        canvas.drawText(label, right - 120f - p.measureText(label), y, p)
        y += 15f
    }

    fun wrap(text: String, maxWidth: Float, p: Paint): List<String> {
        val result = mutableListOf<String>()
        text.split('\n').forEach { line ->
            var current = ""
            line.split(' ').forEach { word ->
                val candidate = if (current.isEmpty()) word else "$current $word"
                if (p.measureText(candidate) <= maxWidth) {
                    current = candidate
                } else {
                    if (current.isNotEmpty()) result += current
                    current = word
                }
            }
            result += current
        }
        return result.ifEmpty { listOf("") }
    }

    fun fit(text: String, maxWidth: Float, p: Paint): String {
        if (p.measureText(text) <= maxWidth) return text
        val count = p.breakText(text, true, maxWidth - p.measureText("…"), null)
        return text.take(count) + "…"
    }

    companion object {
        const val PAGE_WIDTH = 595
        const val PAGE_HEIGHT = 842
        const val MARGIN = 40f
        val TURQUOISE = Color.parseColor("#30D5C8")
        val DARK = Color.parseColor("#1E1E20")
        val GRAY = Color.parseColor("#D1D5DB")
        val MUTED = Color.argb(175, 0x1E, 0x1E, 0x20)
        val ROW_TINT = Color.argb(70, 0xD1, 0xD5, 0xDB)
    }
}

/** Счёт на оплату, договор поставки и прайс-лист в PDF. */
object DocPdf {
    private fun date(ms: Long) = SimpleDateFormat("dd.MM.yyyy", Locale.getDefault()).format(Date(ms))
    private fun money(v: BigDecimal) = QuoteCalculator.formatMoney(v)

    private fun write(context: Context, name: String, footer: (Int) -> String, draw: (PageWriter) -> Unit): File {
        val dir = File(context.cacheDir, "reports").apply { mkdirs() }
        dir.listFiles()?.forEach { it.delete() }
        val file = File(dir, name)
        val pdf = PdfDocument()
        try {
            val w = PageWriter(pdf, footer)
            w.newPage()
            draw(w)
            w.finish()
            file.outputStream().use { pdf.writeTo(it) }
        } finally {
            pdf.close()
        }
        return file
    }

    private fun safeName(text: String) = text.replace(Regex("[^\\p{L}\\p{N}_-]+"), "_")

    /** НДС, который входит в сумму с НДС. */
    fun vatIn(total: BigDecimal, settings: CompanySettings): BigDecimal {
        val rate = settings.vat().ratePercent
        if (rate.signum() <= 0) return BigDecimal.ZERO
        return (total * rate).divide(BigDecimal(100) + rate, 2, RoundingMode.HALF_UP)
    }

    private fun PageWriter.logoHeader(context: Context, s: CompanySettings, right: String) {
        val logo = BrandLogo.draw(context, canvas, PageWriter.MARGIN, y, 36f)
        val left = PageWriter.MARGIN + if (logo > 0) logo + 12f else 0f
        val brand = paint(15f, bold = true)
        canvas.drawText(s.brand, left, y + 15f, brand)
        canvas.drawText(fit("${s.legalName}, ${s.phone}, ${s.email}", width - (left - PageWriter.MARGIN), small), left, y + 30f, small)
        if (right.isNotBlank()) canvas.drawText(right, PageWriter.PAGE_WIDTH - PageWriter.MARGIN - small.measureText(right), y + 15f, small)
        y += 50f
    }

    // ---------------------------------------------------------------- Счёт на оплату

    /**
     * Счёт на оплату. Если сумма счёта меньше суммы КП (предоплата, остаток), в таблице одна строка
     * «Оплата по КП № …», иначе — позиции КП.
     */
    fun invoice(context: Context, s: CompanySettings, inv: Invoice, deal: DealDoc): File =
        write(context, "Счёт_${inv.number}_KS.pdf", { "${s.brand} · Счёт № ${inv.number} · Стр. $it" }) { w ->
            with(w) {
                logoHeader(context, s, "")
                // Банковский блок — как в типовой форме счёта.
                val boxTop = y
                val rows = listOf(
                    "Банк получателя: ${s.bank}" to "БИК ${s.bik}",
                    "" to "Сч. № ${s.corrAccount}",
                    "ИНН ${s.inn}   КПП ${s.kpp}" to "Сч. № ${s.account}",
                    "Получатель: ${s.legalName}" to "",
                )
                rows.forEach { (l, r) ->
                    canvas.drawText(fit(l, width * 0.6f - 10f, normal), PageWriter.MARGIN + 6f, y + 14f, normal)
                    canvas.drawText(r, PageWriter.MARGIN + width * 0.6f + 6f, y + 14f, normal)
                    y += 18f
                }
                canvas.drawRect(PageWriter.MARGIN, boxTop, PageWriter.MARGIN + width, y + 4f, rule)
                canvas.drawLine(PageWriter.MARGIN + width * 0.6f, boxTop, PageWriter.MARGIN + width * 0.6f, y + 4f, rule)
                y += 30f
                canvas.drawText(context.getString(com.knit.calculator.R.string.doc_invoice_title, inv.number, date(inv.date)), PageWriter.MARGIN, y, title)
                y += 24f
                paragraph(
                    context.getString(
                        com.knit.calculator.R.string.doc_supplier,
                        listOf(s.legalName, "ИНН ${s.inn}", "КПП ${s.kpp}", s.legalAddress, s.phone).filter { it.isNotBlank() }.joinToString(", "),
                    ),
                )
                paragraph(
                    context.getString(
                        com.knit.calculator.R.string.doc_buyer,
                        listOf(
                            deal.client,
                            deal.clientInn.takeIf { it.isNotBlank() }?.let { "ИНН $it" },
                            deal.clientKpp.takeIf { it.isNotBlank() }?.let { "КПП $it" },
                            deal.clientAddress.takeIf { it.isNotBlank() },
                        ).filterNotNull().joinToString(", "),
                    ),
                )
                paragraph(context.getString(com.knit.calculator.R.string.doc_basis, deal.quoteNumber, deal.quoteDate), after = 10f)

                val partial = inv.amount.compareTo(deal.total) != 0
                val tableRows = if (partial || deal.lines.isEmpty()) {
                    listOf(listOf("1", inv.purpose, "1", "усл.", money(inv.amount), money(inv.amount)))
                } else {
                    deal.lines.mapIndexed { i, l ->
                        listOf("${i + 1}", l.name, QuoteCalculator.formatQuantity(l.quantity), l.unit, money(l.price), money(l.total))
                    }
                }
                table(floatArrayOf(24f, 241f, 52f, 38f, 75f, 85f), listOf("№", "Товары (работы, услуги)", "Кол-во", "Ед.", "Цена, ₽", "Сумма, ₽"), tableRows)
                val vat = vatIn(inv.amount, s)
                totalLine(context.getString(com.knit.calculator.R.string.doc_total), "${money(inv.amount)} ₽", bold)
                totalLine(context.getString(com.knit.calculator.R.string.doc_vat_in, YarnCalculator.formatCompact(s.vat().ratePercent, 2)), "${money(vat)} ₽")
                totalLine(context.getString(com.knit.calculator.R.string.doc_to_pay), "${money(inv.amount)} ₽", bold)
                y += 6f
                paragraph(context.getString(com.knit.calculator.R.string.doc_items_count, tableRows.size, money(inv.amount)))
                paragraph(MoneyWords.rubles(inv.amount), bold, after = 10f)
                paragraph(context.getString(com.knit.calculator.R.string.doc_invoice_note), small, lineHeight = 11f, after = 24f)
                // QR для оплаты (ГОСТ Р 56042): справа от подписей — клиент платит камерой в приложении банка.
                ensure(130f)
                val qrSize = 104f
                val qrLeft = PageWriter.PAGE_WIDTH - PageWriter.MARGIN - qrSize
                val qrTop = y - 14f
                drawQr(
                    canvas,
                    com.knit.calculator.core.PaymentQr.text(
                        s.legalName, s.account, s.bank, s.bik, s.corrAccount, s.inn, s.kpp,
                        "Оплата по счёту № ${inv.number} от ${date(inv.date)}. ${inv.purpose}", inv.amount,
                    ),
                    qrLeft, qrTop, qrSize,
                )
                canvas.drawText(context.getString(com.knit.calculator.R.string.doc_pay_qr), qrLeft, qrTop + qrSize + 10f, small)
                signatures(s)
                y = maxOf(y, qrTop + qrSize + 20f)
            }
        }

    private fun PageWriter.signatures(s: CompanySettings) {
        ensure(60f)
        listOf("Руководитель", "Бухгалтер").forEach { role ->
            canvas.drawText(role, PageWriter.MARGIN, y, bold)
            canvas.drawLine(PageWriter.MARGIN + 110f, y + 2f, PageWriter.MARGIN + 260f, y + 2f, rule)
            canvas.drawText(s.director, PageWriter.MARGIN + 270f, y, normal)
            y += 26f
        }
    }

    // ---------------------------------------------------------------- Договор поставки

    fun contractValues(s: CompanySettings, deal: DealDoc, prepayPercent: String, now: Long): Map<String, String> = mapOf(
        "номер" to deal.quoteNumber.toString(),
        "город" to s.city,
        "дата" to date(now),
        "поставщик" to s.legalFullName.ifBlank { s.legalName },
        "директор" to s.director,
        "покупатель" to deal.client.ifBlank { "______________________________" },
        "сумма" to "${money(deal.total)} ₽",
        "сумма_прописью" to MoneyWords.rubles(deal.total).replaceFirstChar { it.lowercaseChar() },
        "ставка_ндс" to YarnCalculator.formatCompact(s.vat().ratePercent, 2),
        "ндс" to "${money(deal.vat)} ₽",
        "предоплата" to prepayPercent,
        "срок" to s.leadTime,
        "доставка" to deliveryAreaText(s, deal.total),
    )

    private fun deliveryAreaText(s: CompanySettings, total: BigDecimal): String {
        val threshold = s.freeDeliveryThreshold ?: return "по согласованию сторон"
        val area = s.deliveryArea.trim()
        return if (total >= threshold) listOf("бесплатно", area).filter { it.isNotEmpty() }.joinToString(" ")
        else "по согласованию сторон (бесплатно ${area.ifEmpty { "" }} при заказе от ${money(threshold)} ₽)".replace("  ", " ")
    }

    fun contract(context: Context, s: CompanySettings, deal: DealDoc, paragraphs: List<String>, prepayPercent: String): File {
        val now = System.currentTimeMillis()
        val text = ContractTemplate.fill(paragraphs, contractValues(s, deal, prepayPercent, now))
        return write(context, "Договор_${deal.quoteNumber}_${safeName(deal.client).take(30)}.pdf", { "${s.brand} · Договор поставки № ${deal.quoteNumber} · Стр. $it" }) { w ->
            with(w) {
                logoHeader(context, s, "")
                text.forEachIndexed { i, p ->
                    if (p.startsWith("#")) {
                        val heading = p.removePrefix("#").trim()
                        if (i == 0) {
                            ensure(30f)
                            canvas.drawText(heading, PageWriter.MARGIN + (width - title.measureText(heading)) / 2, y, title)
                            y += 22f
                        } else {
                            y += 4f
                            paragraph(heading, bold, after = 2f)
                        }
                    } else {
                        paragraph(p, normal, lineHeight = 13f, after = 5f)
                    }
                }
                y += 10f
                partiesBlock(s, deal)
                // Приложение № 1 — спецификация.
                newPage()
                canvas.drawText(context.getString(com.knit.calculator.R.string.doc_spec_title, deal.quoteNumber, date(now)), PageWriter.MARGIN, y, title)
                y += 24f
                table(
                    floatArrayOf(24f, 241f, 52f, 38f, 75f, 85f),
                    listOf("№", "Наименование", "Кол-во", "Ед.", "Цена, ₽", "Сумма, ₽"),
                    deal.lines.mapIndexed { i, l -> listOf("${i + 1}", l.name, QuoteCalculator.formatQuantity(l.quantity), l.unit, money(l.price), money(l.total)) },
                )
                totalLine(context.getString(com.knit.calculator.R.string.doc_total), "${money(deal.total)} ₽", bold)
                totalLine(context.getString(com.knit.calculator.R.string.doc_vat_in, YarnCalculator.formatCompact(s.vat().ratePercent, 2)), "${money(deal.vat)} ₽")
                y += 6f
                paragraph(MoneyWords.rubles(deal.total), bold, after = 20f)
                partiesBlock(s, deal)
            }
        }
    }

    private fun PageWriter.partiesBlock(s: CompanySettings, deal: DealDoc) {
        ensure(150f)
        val col = width / 2
        val top = y
        fun column(x: Float, lines: List<String>, sign: String) {
            var ly = top
            lines.forEachIndexed { i, t ->
                wrap(t, col - 14f, if (i == 0) bold else small).forEach {
                    canvas.drawText(it, x, ly, if (i == 0) bold else small)
                    ly += if (i == 0) 14f else 11f
                }
            }
            ly += 18f
            canvas.drawLine(x, ly, x + 120f, ly, rule)
            canvas.drawText(sign, x + 126f, ly, small)
            y = maxOf(y, ly + 20f)
        }
        column(
            PageWriter.MARGIN,
            listOf(
                "Поставщик",
                s.legalName,
                "ИНН ${s.inn}, КПП ${s.kpp}, ОГРН ${s.ogrn}",
                "Юр. адрес: ${s.legalAddress}",
                "Р/с ${s.account} в ${s.bank}, БИК ${s.bik}, к/с ${s.corrAccount}",
                "${s.phone}, ${s.email}",
            ),
            "/ ${s.director} /",
        )
        column(
            PageWriter.MARGIN + col,
            listOf(
                "Покупатель",
                deal.client.ifBlank { "________________________" },
                "ИНН ${deal.clientInn.ifBlank { "____________" }}, КПП ${deal.clientKpp.ifBlank { "____________" }}",
                "Адрес: ${deal.clientAddress.ifBlank { "______________________________" }}",
                "Р/с ____________________ в __________",
                "БИК ____________, к/с ____________________",
            ),
            "/ ______________ /",
        )
    }

    // ---------------------------------------------------------------- Прайс-лист с QR-кодом

    fun priceList(context: Context, s: CompanySettings, products: List<Product>): File {
        val now = System.currentTimeMillis()
        return write(context, "Прайс-лист_KS_${SimpleDateFormat("yyyyMMdd", Locale.US).format(Date(now))}.pdf", { "${s.brand} · Прайс-лист · Стр. $it" }) { w ->
            with(w) {
                val top = y
                logoHeader(context, s, "")
                // QR-код на каталог сайта — справа в шапке.
                val url = s.shopUrl.trim()
                if (url.isNotEmpty()) {
                    val size = 64f
                    drawQr(canvas, url, PageWriter.PAGE_WIDTH - PageWriter.MARGIN - size, top - 4f, size)
                    val caption = context.getString(com.knit.calculator.R.string.doc_qr_caption)
                    canvas.drawText(caption, PageWriter.PAGE_WIDTH - PageWriter.MARGIN - size / 2 - small.measureText(caption) / 2, top + size + 6f, small)
                    y = maxOf(y, top + size + 18f)
                }
                canvas.drawText(context.getString(com.knit.calculator.R.string.doc_price_title, date(now)), PageWriter.MARGIN, y, title)
                y += 18f
                paragraph(
                    context.getString(
                        if (s.vatIncluded) com.knit.calculator.R.string.doc_price_vat_in else com.knit.calculator.R.string.doc_price_vat_out,
                        YarnCalculator.formatCompact(s.vat().ratePercent, 2),
                    ),
                    small, after = 10f,
                )
                products.forEach { p ->
                    ensure(70f)
                    paragraph(p.name, paint(12f, bold = true), after = 2f)
                    val defaults = p.options.mapNotNull { g -> g.choices.firstOrNull()?.let { "${g.name}: ${it.name}" } }
                    if (defaults.isNotEmpty()) paragraph(defaults.joinToString("; "), small, lineHeight = 11f, after = 4f)
                    val quantities = (listOf(p.minOrder.max(BigDecimal.ONE)) + p.tiers.map { it.fromQuantity })
                        .distinctBy { it.stripTrailingZeros() }.sortedBy { it }
                    val rows = quantities.mapIndexed { i, q ->
                        val next = quantities.getOrNull(i + 1)
                        val range = if (next != null) "${QuoteCalculator.formatQuantity(q)}–${QuoteCalculator.formatQuantity(next - BigDecimal.ONE)}"
                        else "от ${QuoteCalculator.formatQuantity(q)}"
                        val price = QuoteCalculator.unitPrice(p, emptyMap(), q).first
                        listOf("${i + 1}", "$range ${p.unit}", money(price))
                    }
                    table(floatArrayOf(24f, 300f, 191f), listOf("№", "Тираж", "Цена за ${p.unit}, ₽"), rows)
                    p.options.forEach { g ->
                        val variants = g.choices.joinToString(", ") { c ->
                            val add = if (c.priceAdd.signum() != 0) " (+${money(c.priceAdd)} ₽)" else ""
                            c.name + add
                        }
                        paragraph("${g.name}: $variants", small, lineHeight = 11f, after = 2f)
                    }
                    y += 10f
                }
                paragraph(context.getString(com.knit.calculator.R.string.doc_price_note, s.phone, s.email, s.website), normal, after = 0f)
            }
        }
    }

    /** QR-код векторными квадратами (резкий при печати). */
    fun drawQr(canvas: Canvas, text: String, left: Float, top: Float, size: Float) {
        val m = QrCode.matrix(text)
        val quiet = 2
        val cell = size / (m.size + 2 * quiet)
        val paint = Paint().apply { color = Color.WHITE; style = Paint.Style.FILL }
        canvas.drawRect(left, top, left + size, top + size, paint)
        paint.color = Color.BLACK
        m.forEachIndexed { yy, row ->
            row.forEachIndexed { xx, dark ->
                if (dark) {
                    val x0 = left + (xx + quiet) * cell
                    val y0 = top + (yy + quiet) * cell
                    canvas.drawRect(x0, y0, x0 + cell, y0 + cell, paint)
                }
            }
        }
    }

    /** QR-код картинкой для экрана. */
    fun qrBitmap(text: String, sizePx: Int): android.graphics.Bitmap {
        val bmp = android.graphics.Bitmap.createBitmap(sizePx, sizePx, android.graphics.Bitmap.Config.ARGB_8888)
        drawQr(Canvas(bmp), text, 0f, 0f, sizePx.toFloat())
        return bmp
    }
}
