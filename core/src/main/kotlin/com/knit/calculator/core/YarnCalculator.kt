package com.knit.calculator.core

import java.math.BigDecimal
import java.math.MathContext
import java.math.RoundingMode

/** Как задан объём заказа. */
enum class OrderUnit {
    /** Количество изделий, шт. */
    PIECES,

    /** Общий вес готовых изделий, кг. */
    KILOGRAMS,
}

/** Нить в составе изделия: основная, доп. 1, доп. 2, спандекс (резинка) и т. д. */
data class YarnComponent(val name: String, val percent: BigDecimal)

/**
 * Исходные данные расчёта.
 *
 * @property itemWeightGrams вес одного готового изделия, г.
 * @property orderAmount объём заказа: штуки или килограммы — см. [orderUnit].
 * @property wastePercent технологические отходы и брак, % сверх чистого веса.
 * @property mainName название основной нити; её доля — остаток до 100 % после доп. нитей.
 * @property extras дополнительные нити с долями в процентах от веса изделия.
 */
data class YarnInput(
    val itemWeightGrams: BigDecimal,
    val orderAmount: BigDecimal,
    val orderUnit: OrderUnit,
    val wastePercent: BigDecimal,
    val mainName: String,
    val extras: List<YarnComponent>,
)

/** Строка результата по одной нити. */
data class YarnLine(
    val name: String,
    val percent: BigDecimal,
    /** Чистый вес нити в одном изделии, г. */
    val gramsPerItemNet: BigDecimal,
    /** Расход нити на одно изделие с учётом брака, г. */
    val gramsPerItem: BigDecimal,
    /** Чистый вес нити на весь заказ, кг. */
    val orderKgNet: BigDecimal,
    /** Расход нити на заказ с учётом брака, кг. */
    val orderKg: BigDecimal,
)

data class YarnResult(
    val lines: List<YarnLine>,
    /** Количество изделий (при заказе в кг — расчётное, может быть дробным). */
    val pieces: BigDecimal,
    /** Вес готовых изделий в заказе, кг. */
    val productKg: BigDecimal,
    val gramsPerItem: BigDecimal,
    val totalKgNet: BigDecimal,
    val totalKg: BigDecimal,
    /** Запас на брак, кг. */
    val wasteKg: BigDecimal,
)

enum class YarnIssue {
    WEIGHT_REQUIRED,
    ORDER_REQUIRED,
    WASTE_OUT_OF_RANGE,
    EXTRA_PERCENT_INVALID,
    PERCENT_SUM_EXCEEDED,
}

sealed interface YarnOutcome {
    data class Success(val result: YarnResult) : YarnOutcome
    data class Invalid(val issues: Set<YarnIssue>) : YarnOutcome
}

/**
 * Расчёт расхода пряжи.
 *
 * Чистый вес нити = вес изделия × доля нити. Расход с учётом брака = чистый вес × (1 + брак / 100).
 * Доля основной нити — всё, что остаётся до 100 % после дополнительных нитей,
 * поэтому сумма долей всегда равна 100 %. Однотонное изделие — только основная нить (100 %).
 * Внутренние вычисления ведутся без округления; округляется только отображение.
 */
object YarnCalculator {
    private val MC = MathContext(34, RoundingMode.HALF_EVEN)
    private val HUNDRED = BigDecimal(100)
    private val THOUSAND = BigDecimal(1000)

    fun calculate(input: YarnInput): YarnOutcome {
        val issues = mutableSetOf<YarnIssue>()
        if (input.itemWeightGrams.signum() <= 0) issues += YarnIssue.WEIGHT_REQUIRED
        if (input.orderAmount.signum() <= 0) issues += YarnIssue.ORDER_REQUIRED
        if (input.wastePercent.signum() < 0 || input.wastePercent > HUNDRED) issues += YarnIssue.WASTE_OUT_OF_RANGE
        if (input.extras.any { it.percent.signum() <= 0 || it.percent >= HUNDRED }) issues += YarnIssue.EXTRA_PERCENT_INVALID
        val extrasSum = input.extras.fold(BigDecimal.ZERO) { acc, c -> acc + c.percent }
        if (extrasSum >= HUNDRED) issues += YarnIssue.PERCENT_SUM_EXCEEDED
        if (issues.isNotEmpty()) return YarnOutcome.Invalid(issues)

        val pieces = when (input.orderUnit) {
            OrderUnit.PIECES -> input.orderAmount
            OrderUnit.KILOGRAMS -> input.orderAmount.multiply(THOUSAND).divide(input.itemWeightGrams, MC)
        }
        val wasteFactor = BigDecimal.ONE + input.wastePercent.divide(HUNDRED, MC)
        val components = listOf(YarnComponent(input.mainName, HUNDRED - extrasSum)) + input.extras

        val lines = components.map { c ->
            val net = input.itemWeightGrams.multiply(c.percent, MC).divide(HUNDRED, MC)
            val gross = net.multiply(wasteFactor, MC)
            YarnLine(
                name = c.name,
                percent = c.percent,
                gramsPerItemNet = net,
                gramsPerItem = gross,
                orderKgNet = net.multiply(pieces, MC).divide(THOUSAND, MC),
                orderKg = gross.multiply(pieces, MC).divide(THOUSAND, MC),
            )
        }
        val productKg = input.itemWeightGrams.multiply(pieces, MC).divide(THOUSAND, MC)
        val totalKg = productKg.multiply(wasteFactor, MC)
        return YarnOutcome.Success(
            YarnResult(
                lines = lines,
                pieces = pieces,
                productKg = productKg,
                gramsPerItem = input.itemWeightGrams.multiply(wasteFactor, MC),
                totalKgNet = productKg,
                totalKg = totalKg,
                wasteKg = totalKg - productKg,
            ),
        )
    }

    /** Разбор числа из поля ввода: допускает запятую, пробелы; пустое или некорректное → `null`. */
    fun parseDecimal(text: String): BigDecimal? {
        val cleaned = text.trim()
            .replace(',', '.')
            .replace(" ", "")
            .replace(NumberFormatter.GROUP_SEPARATOR.toString(), "")
            .replace(' '.toString(), "")
        if (cleaned.isEmpty()) return null
        return cleaned.toBigDecimalOrNull()
    }

    /** Число для отчёта: фиксированное количество знаков, запятая, разделение разрядов. */
    fun format(value: BigDecimal, decimals: Int): String {
        val rounded = value.setScale(decimals, RoundingMode.HALF_UP)
        val raw = if (rounded.signum() == 0) rounded.abs().toPlainString() else rounded.toPlainString()
        return NumberFormatter.toDisplay(raw.replace('-', Symbols.MINUS))
    }

    /** Как [format], но без лишних нулей в дробной части (для процентов и штук). */
    fun formatCompact(value: BigDecimal, maxDecimals: Int): String {
        val rounded = value.setScale(maxDecimals, RoundingMode.HALF_UP).stripTrailingZeros()
        val plain = if (rounded.signum() == 0) "0" else rounded.toPlainString()
        return NumberFormatter.toDisplay(plain.replace('-', Symbols.MINUS))
    }
}
