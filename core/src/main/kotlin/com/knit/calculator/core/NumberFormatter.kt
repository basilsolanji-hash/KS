package com.knit.calculator.core

import java.math.BigDecimal
import java.math.MathContext
import java.math.RoundingMode

/**
 * Форматирование чисел.
 *
 * Результат округляется до [SIGNIFICANT_DIGITS] значащих цифр (половина — вверх),
 * лишние нули отбрасываются. Очень большие и очень малые значения показываются
 * в экспоненциальной записи (`1.5E20`).
 *
 * «Сырой» вид (raw) — это строка, которую понимает [Evaluator] и в которой хранится
 * ввод: точка как десятичный разделитель, без разделителей разрядов.
 * Отображаемый вид (display) — с запятой и узкими пробелами между разрядами.
 */
object NumberFormatter {
    const val SIGNIFICANT_DIGITS = 15
    private val ROUNDING = MathContext(SIGNIFICANT_DIGITS, RoundingMode.HALF_UP)
    private val SCI_UPPER = BigDecimal.ONE.scaleByPowerOfTen(SIGNIFICANT_DIGITS)
    private val SCI_LOWER = BigDecimal.ONE.scaleByPowerOfTen(-9)

    const val GROUP_SEPARATOR = ' ' // узкий неразрывный пробел
    const val DECIMAL_SEPARATOR = ','

    fun toRaw(value: BigDecimal): String {
        val rounded = value.round(ROUNDING).stripTrailingZeros()
        if (rounded.signum() == 0) return "0"
        val abs = rounded.abs()
        val body = if (abs >= SCI_UPPER || abs < SCI_LOWER) scientific(abs) else abs.toPlainString()
        return if (rounded.signum() < 0) "${Symbols.MINUS}$body" else body
    }

    private fun scientific(abs: BigDecimal): String {
        val digits = abs.unscaledValue().toString()
        val exponent = digits.length - 1 - abs.scale()
        val mantissa = if (digits.length > 1) "${digits[0]}.${digits.substring(1)}" else digits
        return "${mantissa}E$exponent"
    }

    /** Превращает сырое выражение в удобочитаемое: `12345.6+7` → `12 345,6+7`. */
    fun toDisplay(raw: String): String {
        val out = StringBuilder(raw.length + 8)
        var i = 0
        while (i < raw.length) {
            val c = raw[i]
            if (c.isDigit() || c == Symbols.DOT) {
                val start = i
                while (i < raw.length && (raw[i].isDigit() || raw[i] == Symbols.DOT)) i++
                appendNumber(out, raw.substring(start, i))
                continue
            }
            out.append(c)
            i++
        }
        return out.toString()
    }

    private fun appendNumber(out: StringBuilder, number: String) {
        val dot = number.indexOf(Symbols.DOT)
        val intPart = if (dot >= 0) number.substring(0, dot) else number
        if (intPart.length > 4) {
            val firstGroup = intPart.length % 3
            intPart.forEachIndexed { index, ch ->
                if (index > 0 && (index - firstGroup) % 3 == 0) out.append(GROUP_SEPARATOR)
                out.append(ch)
            }
        } else {
            out.append(intPart)
        }
        if (dot >= 0) {
            out.append(DECIMAL_SEPARATOR)
            out.append(number, dot + 1, number.length)
        }
    }

    /** Значение для буфера обмена: без разделителей разрядов, с запятой и обычным минусом. */
    fun toClipboard(raw: String): String =
        raw.replace(Symbols.DOT, DECIMAL_SEPARATOR).replace(Symbols.MINUS, '-')
}
