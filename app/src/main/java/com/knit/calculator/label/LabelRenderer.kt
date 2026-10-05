package com.knit.calculator.label

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import com.knit.calculator.core.Ean13
import com.knit.calculator.core.LabelContent
import com.knit.calculator.core.LabelSpec
import com.knit.calculator.core.MonoBitmap

/**
 * Макет этикетки 120×75 мм (альбомный) в точках принтера:
 * сверху бренд и сайт, название; слева характеристики, справа EAN-13; внизу изготовитель.
 */
object LabelRenderer {
    fun render(content: LabelContent, spec: LabelSpec): Bitmap {
        val w = spec.widthDots
        val h = spec.heightDots
        fun mm(v: Double) = (v * spec.dpi / 25.4).toFloat()
        val bitmap = Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888)
        val c = Canvas(bitmap)
        c.drawColor(Color.WHITE)
        val regular = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.BLACK; typeface = Typeface.DEFAULT }
        val bold = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.BLACK; typeface = Typeface.DEFAULT_BOLD }
        val fill = Paint().apply { color = Color.BLACK; style = Paint.Style.FILL }
        val margin = mm(3.0)
        val right = w - margin

        // Шапка: бренд — сайт, линия.
        bold.textSize = mm(4.6)
        c.drawText(fit(content.brand, bold, w / 2f), margin, mm(7.5), bold)
        regular.textSize = mm(3.4)
        val site = fit(content.website, regular, w / 2f - margin)
        c.drawText(site, right - regular.measureText(site), mm(7.5), regular)
        c.drawRect(margin, mm(9.3), right, mm(9.3) + mm(0.4), fill)

        // Название — до двух строк.
        bold.textSize = mm(5.0)
        var y = mm(15.3)
        wrap(content.title, bold, right - margin, 2).forEach { line ->
            c.drawText(line, margin, y, bold)
            y += mm(5.8)
        }

        // Характеристики слева.
        val split = mm(66.0)
        val bottomLine = mm(60.5)
        regular.textSize = mm(3.5)
        bold.textSize = mm(3.5)
        var dy = y + mm(1.2)
        for ((name, value) in content.details) {
            if (dy > bottomLine - mm(1.5)) break
            val label = "$name: "
            c.drawText(label, margin, dy, regular)
            val x = margin + regular.measureText(label)
            c.drawText(fit(value, bold, split - mm(2.0) - x), x, dy, bold)
            dy += mm(4.7)
        }

        // Штрихкод справа.
        drawBarcode(c, content.barcode, split, y - mm(3.0), right, bottomLine - mm(1.5), spec, fill, regular)

        // Изготовитель внизу.
        c.drawRect(margin, bottomLine, right, bottomLine + mm(0.3), fill)
        regular.textSize = mm(2.8)
        var my = bottomLine + mm(3.9)
        content.maker.take(3).forEach { line ->
            c.drawText(fit(line, regular, right - margin), margin, my, regular)
            my += mm(3.5)
        }
        return bitmap
    }

    /** EAN-13: модуль — целое число точек (чёткие полосы), цифры под штрихкодом. */
    private fun drawBarcode(c: Canvas, code: String, left: Float, top: Float, right: Float, bottom: Float, spec: LabelSpec, fill: Paint, text: Paint) {
        fun mm(v: Double) = (v * spec.dpi / 25.4).toFloat()
        if (!Ean13.isValid(code)) {
            val frame = Paint().apply { color = Color.BLACK; style = Paint.Style.STROKE; strokeWidth = mm(0.3) }
            c.drawRect(left, top, right, bottom, frame)
            text.textSize = mm(3.2)
            val msg = "Нет штрихкода"
            c.drawText(msg, (left + right - text.measureText(msg)) / 2, (top + bottom) / 2, text)
            return
        }
        val modules = Ean13.modules(code)
        // 95 модулей + свободные зоны 11 и 7 модулей.
        val module = maxOf(2, ((right - left) / (95 + 18)).toInt())
        val barsWidth = module * 95
        val x0 = (left + (right - left - barsWidth) / 2 + module * 2).toInt() // свободная зона слева шире (11 и 7 модулей)
        val digits = mm(3.4)
        val barBottom = bottom - digits - mm(0.6)
        val guardBottom = barBottom + digits / 2
        modules.forEachIndexed { i, dark ->
            if (!dark) return@forEachIndexed
            val guard = i < 3 || i in 45..49 || i >= 92
            val x = (x0 + i * module).toFloat()
            c.drawRect(x, top, x + module, if (guard) guardBottom else barBottom, fill)
        }
        text.textSize = digits
        val baseline = bottom - mm(0.3)
        c.drawText(code.substring(0, 1), x0 - module * 2 - text.measureText(code.substring(0, 1)), baseline, text)
        fun centered(s: String, from: Int, to: Int) {
            val cx = x0 + (from + to) / 2f * module
            c.drawText(s, cx - text.measureText(s) / 2, baseline, text)
        }
        centered(code.substring(1, 7), 3, 45)
        centered(code.substring(7, 13), 50, 92)
    }

    fun toMono(bitmap: Bitmap): MonoBitmap {
        val px = IntArray(bitmap.width * bitmap.height)
        bitmap.getPixels(px, 0, bitmap.width, 0, 0, bitmap.width, bitmap.height)
        return MonoBitmap.fromPixels(bitmap.width, bitmap.height, px)
    }

    private fun fit(text: String, paint: Paint, width: Float): String {
        if (paint.measureText(text) <= width) return text
        var end = text.length
        while (end > 0 && paint.measureText(text.substring(0, end) + "…") > width) end--
        return text.substring(0, end).trimEnd() + "…"
    }

    private fun wrap(text: String, paint: Paint, width: Float, maxLines: Int): List<String> {
        val lines = mutableListOf<String>()
        var current = ""
        val words = text.split(' ').filter { it.isNotEmpty() }
        for ((i, word) in words.withIndex()) {
            val candidate = if (current.isEmpty()) word else "$current $word"
            if (paint.measureText(candidate) <= width || current.isEmpty()) {
                current = candidate
            } else {
                lines += current
                current = word
                if (lines.size == maxLines - 1) {
                    current = words.drop(i).joinToString(" ")
                    break
                }
            }
        }
        if (current.isNotEmpty()) lines += current
        return lines.take(maxLines).map { fit(it, paint, width) }
    }
}
