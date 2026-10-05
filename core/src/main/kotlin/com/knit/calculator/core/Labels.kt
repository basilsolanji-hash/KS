package com.knit.calculator.core

import com.google.zxing.oned.EAN13Writer

/** Штрихкод EAN-13: проверка контрольной цифры и полосы (95 модулей, без свободных зон). */
object Ean13 {
    fun checkDigit(first12: String): Int {
        require(first12.length == 12 && first12.all { it.isDigit() })
        val sum = first12.mapIndexed { i, c -> (c - '0') * if (i % 2 == 0) 1 else 3 }.sum()
        return (10 - sum % 10) % 10
    }

    fun isValid(code: String): Boolean =
        code.length == 13 && code.all { it.isDigit() } && checkDigit(code.take(12)) == code.last() - '0'

    /** Модули штрихкода: true — тёмная полоса. */
    fun modules(code: String): BooleanArray {
        require(isValid(code)) { "Неверный штрихкод EAN-13: $code" }
        return EAN13Writer().encode(code)
    }

    /** Цифры под штрихкодом: «4 600000 000017». */
    fun human(code: String): String = "${code[0]} ${code.substring(1, 7)} ${code.substring(7)}"
}

/** Язык команд термопринтера. */
enum class PrinterLanguage(val title: String) {
    TSPL("TSPL (Xprinter, TSC, HPRT)"),
    ZPL("ZPL (Zebra)"),
}

/**
 * Параметры этикетки и принтера. Этикетка 120×75 мм: на 4-дюймовом принтере (Xprinter, ширина печати до 108 мм)
 * рулон идёт узкой стороной (75 мм) — тогда [rotated] и картинка поворачивается на 90°.
 */
data class LabelSpec(
    val widthMm: Int = 120,
    val heightMm: Int = 75,
    val dpi: Int = 203,
    val gapMm: Int = 2,
    /** Плотность (нагрев), 1–15. */
    val density: Int = 8,
    val language: PrinterLanguage = PrinterLanguage.TSPL,
    val rotated: Boolean = true,
) {
    /** Размер бумаги в принтере (ширина рулона × длина этикетки). */
    val paperWidthMm: Int get() = if (rotated) heightMm else widthMm
    val paperHeightMm: Int get() = if (rotated) widthMm else heightMm
    val paperWidthDots: Int get() = dots(paperWidthMm.toDouble()).let { it - it % 8 }
    val paperHeightDots: Int get() = dots(paperHeightMm.toDouble())

    /** Размер макета (всегда альбомный 120×75) в точках. */
    val widthDots: Int get() = if (rotated) paperHeightDots else paperWidthDots
    val heightDots: Int get() = if (rotated) paperWidthDots else paperHeightDots
    fun dots(mm: Double): Int = Math.round(mm * dpi / 25.4).toInt()
}

/** Чёрно-белая картинка: строки по [bytesPerRow] байт, старший бит — левая точка, 1 — чёрная. */
class MonoBitmap(val width: Int, val height: Int, val data: ByteArray) {
    val bytesPerRow: Int get() = (width + 7) / 8

    fun isBlack(x: Int, y: Int): Boolean = data[y * bytesPerRow + x / 8].toInt() and (0x80 ushr (x % 8)) != 0

    /** Поворот на 90° по часовой стрелке. */
    fun rotate90(): MonoBitmap {
        val w = height
        val h = width
        val row = (w + 7) / 8
        val out = ByteArray(row * h)
        for (y in 0 until h) for (x in 0 until w) {
            if (isBlack(y, height - 1 - x)) out[y * row + x / 8] = (out[y * row + x / 8].toInt() or (0x80 ushr (x % 8))).toByte()
        }
        return MonoBitmap(w, h, out)
    }

    companion object {
        /** Из точек ARGB/RGB: темнее половины яркости — чёрная. */
        fun fromPixels(width: Int, height: Int, pixels: IntArray): MonoBitmap {
            val row = (width + 7) / 8
            val out = ByteArray(row * height)
            for (y in 0 until height) for (x in 0 until width) {
                val c = pixels[y * width + x]
                val alpha = c ushr 24 and 0xFF
                val lum = (299 * (c shr 16 and 0xFF) + 587 * (c shr 8 and 0xFF) + 114 * (c and 0xFF)) / 1000
                val dark = alpha > 127 && lum < 128
                if (dark) out[y * row + x / 8] = (out[y * row + x / 8].toInt() or (0x80 ushr (x % 8))).toByte()
            }
            return MonoBitmap(width, height, out)
        }
    }
}

/** Команды печати: этикетка — картинкой (кириллица не зависит от шрифтов принтера), копии — командой принтера. */
object LabelPrinter {
    /** [design] — макет 120×75 (альбомный); при [LabelSpec.rotated] поворачивается под рулон. */
    fun commands(spec: LabelSpec, design: MonoBitmap, copies: Int): ByteArray {
        val n = copies.coerceIn(1, 999)
        val bitmap = if (spec.rotated) design.rotate90() else design
        return when (spec.language) {
            PrinterLanguage.TSPL -> tspl(spec, bitmap, n)
            PrinterLanguage.ZPL -> zpl(spec, bitmap, n)
        }
    }

    private fun tspl(spec: LabelSpec, b: MonoBitmap, copies: Int): ByteArray {
        val head = buildString {
            append("SIZE ${spec.paperWidthMm} mm,${spec.paperHeightMm} mm\r\n")
            append("GAP ${spec.gapMm} mm,0 mm\r\n")
            append("DIRECTION 1,0\r\n")
            append("REFERENCE 0,0\r\n")
            append("DENSITY ${spec.density.coerceIn(0, 15)}\r\n")
            append("SET TEAR ON\r\n")
            append("CLS\r\n")
            append("BITMAP 0,0,${b.bytesPerRow},${b.height},0,")
        }.toByteArray(Charsets.US_ASCII)
        // В TSPL бит 0 — печать (чёрная точка), поэтому инвертируем.
        val body = ByteArray(b.data.size) { (b.data[it].toInt() xor 0xFF).toByte() }
        val tail = "\r\nPRINT $copies,1\r\n".toByteArray(Charsets.US_ASCII)
        return head + body + tail
    }

    private fun zpl(spec: LabelSpec, b: MonoBitmap, copies: Int): ByteArray {
        val hex = StringBuilder(b.data.size * 2)
        for (byte in b.data) {
            val v = byte.toInt() and 0xFF
            hex.append(HEX[v ushr 4]).append(HEX[v and 0x0F])
        }
        val total = b.data.size
        val darkness = (spec.density.coerceIn(0, 15) * 2).coerceAtMost(30)
        return ("^XA^PW${spec.paperWidthDots}^LL${spec.paperHeightDots}^LH0,0~SD${"%02d".format(darkness)}" +
            "^FO0,0^GFA,$total,$total,${b.bytesPerRow},$hex^FS^PQ$copies^XZ").toByteArray(Charsets.US_ASCII)
    }

    private const val HEX = "0123456789ABCDEF"
}

/** Текст этикетки товара. */
data class LabelContent(
    val brand: String,
    val website: String,
    val title: String,
    /** «Артикул» → «11-001», «Цвет» → «белый»… */
    val details: List<Pair<String, String>>,
    val barcode: String,
    val maker: List<String>,
)

object Labels {
    /** Характеристики на этикетке — в этом порядке; «Состав / материала» печатается как «Состав». */
    val DETAIL_ORDER = listOf("Артикул", "Цвет", "Размер", "Состав / материала", "Тип резинки", "Тип")

    fun content(
        product: Product,
        brand: String,
        website: String,
        packQuantity: String,
        madeDate: String,
        makerName: String,
        makerInn: String,
        makerAddress: String,
    ): LabelContent {
        val attrs = product.attributes.filterValues { it.isNotBlank() && it != "-" }
        val details = buildList {
            val article = attrs["Артикул"] ?: product.code.takeIf { it.isNotBlank() }
            article?.let { add("Артикул" to it) }
            DETAIL_ORDER.drop(1).forEach { k -> attrs[k]?.let { add((if (k == "Состав / материала") "Состав" else k) to it) } }
            packQuantity.trim().takeIf { it.isNotEmpty() && it != "0" }?.let { add("Кол-во" to "$it ${product.unit}") }
            madeDate.trim().takeIf { it.isNotEmpty() }?.let { add("Дата изготовления" to it) }
        }
        val maker = buildList {
            add("Изготовитель: " + listOf(makerName, makerInn.takeIf { it.isNotBlank() }?.let { "ИНН $it" }).filterNotNull().filter { it.isNotBlank() }.joinToString(", "))
            if (makerAddress.isNotBlank()) add(makerAddress)
            add("Сделано в России")
        }
        return LabelContent(
            brand = brand,
            website = website,
            title = product.baseName.ifBlank { product.name },
            details = details,
            barcode = product.barcode,
            maker = maker,
        )
    }
}
