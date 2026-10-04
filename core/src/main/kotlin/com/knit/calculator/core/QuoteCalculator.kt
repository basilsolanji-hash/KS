package com.knit.calculator.core

import java.math.BigDecimal
import java.math.RoundingMode

/** Вариант параметра изделия с надбавкой к цене за единицу (например, «Ширина 3 см», +4 ₽). */
data class PriceChoice(val id: Long, val name: String, val priceAdd: BigDecimal)

/** Параметр изделия: ширина, размер, рисунок, цвет… */
data class OptionGroup(val id: Long, val name: String, val choices: List<PriceChoice>)

/** Скидка от объёма: начиная с [fromQuantity] единиц цена снижается на [percent] %. */
data class DiscountTier(val fromQuantity: BigDecimal, val percent: BigDecimal)

/** Позиция ассортимента. Все цены — за единицу [unit]. */
data class Product(
    val id: Long,
    val name: String,
    val unit: String,
    val basePrice: BigDecimal,
    val minOrder: BigDecimal,
    /** Разовая стоимость подготовки (наладка, образец) на позицию заказа. */
    val setupFee: BigDecimal,
    val options: List<OptionGroup>,
    val tiers: List<DiscountTier>,
)

/** Выбор клиента: изделие, варианты параметров (id группы → id варианта) и количество. */
data class QuoteLineInput(
    val product: Product,
    val selected: Map<Long, Long>,
    val quantity: BigDecimal,
)

data class QuoteLine(
    val product: Product,
    val choices: List<Pair<OptionGroup, PriceChoice>>,
    val quantity: BigDecimal,
    /** Цена за единицу до скидки. */
    val listPrice: BigDecimal,
    val discountPercent: BigDecimal,
    /** Цена за единицу со скидкой, округлена до копеек. */
    val unitPrice: BigDecimal,
    val setupFee: BigDecimal,
    val total: BigDecimal,
    val belowMinimum: Boolean,
) {
    /** «Подвязы (Ширина: 3 см; Рисунок: 2 полосы)». */
    val description: String
        get() = if (choices.isEmpty()) product.name
        else product.name + " (" + choices.joinToString("; ") { (g, c) -> "${g.name}: ${c.name}" } + ")"
}

/**
 * Ставка НДС и как заданы цены: [included] = `true` — цены в справочнике уже с НДС,
 * в КП выделяется «в т. ч. НДС»; `false` — НДС начисляется сверху.
 */
data class VatSettings(val ratePercent: BigDecimal, val included: Boolean)

data class QuoteTotals(
    val lines: List<QuoteLine>,
    /** Сумма позиций (как в справочнике: с НДС или без — см. [VatSettings.included]). */
    val subtotal: BigDecimal,
    val vat: BigDecimal,
    /** Итого к оплате с НДС. */
    val total: BigDecimal,
    /** Итого без НДС. */
    val totalWithoutVat: BigDecimal,
)

/**
 * Расчёт стоимости заказа для коммерческого предложения.
 *
 * Цена за единицу = (базовая цена + надбавки выбранных вариантов) × (1 − скидка/100),
 * где скидка — наибольшая ступень, порог которой не превышает количество.
 * Сумма позиции = цена за единицу × количество + разовая подготовка.
 * Денежные значения округляются до копеек (половина — вверх).
 */
object QuoteCalculator {
    private val HUNDRED = BigDecimal(100)

    fun line(input: QuoteLineInput): QuoteLine {
        val product = input.product
        val choices = product.options.mapNotNull { group ->
            val choice = group.choices.firstOrNull { it.id == input.selected[group.id] } ?: group.choices.firstOrNull()
            choice?.let { group to it }
        }
        val listPrice = choices.fold(product.basePrice) { acc, (_, c) -> acc + c.priceAdd }.max(BigDecimal.ZERO)
        val discount = discountFor(product, input.quantity)
        val unitPrice = money((listPrice * (HUNDRED - discount)).over(HUNDRED))
        val total = money(unitPrice * input.quantity) + money(product.setupFee)
        return QuoteLine(
            product = product,
            choices = choices,
            quantity = input.quantity,
            listPrice = money(listPrice),
            discountPercent = discount,
            unitPrice = unitPrice,
            setupFee = money(product.setupFee),
            total = total,
            belowMinimum = input.quantity < product.minOrder,
        )
    }

    fun discountFor(product: Product, quantity: BigDecimal): BigDecimal =
        product.tiers
            .filter { it.fromQuantity <= quantity }
            .maxByOrNull { it.fromQuantity }
            ?.percent
            ?.coerceIn(BigDecimal.ZERO, HUNDRED)
            ?: BigDecimal.ZERO

    fun totals(lines: List<QuoteLine>, vat: VatSettings): QuoteTotals {
        val subtotal = lines.fold(BigDecimal.ZERO) { acc, l -> acc + l.total }
        val rate = vat.ratePercent.max(BigDecimal.ZERO)
        return if (vat.included) {
            val vatAmount = money((subtotal * rate).over(HUNDRED + rate))
            QuoteTotals(lines, subtotal, vatAmount, subtotal, subtotal - vatAmount)
        } else {
            val vatAmount = money((subtotal * rate).over(HUNDRED))
            QuoteTotals(lines, subtotal, vatAmount, subtotal + vatAmount, subtotal)
        }
    }

    private fun BigDecimal.over(other: BigDecimal): BigDecimal = divide(other, 10, RoundingMode.HALF_EVEN)

    fun money(value: BigDecimal): BigDecimal = value.setScale(2, RoundingMode.HALF_UP)

    /** «12 345,50» — всегда две цифры копеек, разряды от тысячи. */
    fun formatMoney(value: BigDecimal): String {
        val v = money(value)
        val negative = v.signum() < 0
        val plain = v.abs().toPlainString()
        val dot = plain.indexOf('.')
        val intPart = plain.substring(0, dot)
        val grouped = StringBuilder()
        intPart.forEachIndexed { i, c ->
            if (i > 0 && (intPart.length - i) % 3 == 0) grouped.append(NumberFormatter.GROUP_SEPARATOR)
            grouped.append(c)
        }
        return (if (negative) Symbols.MINUS.toString() else "") + grouped + NumberFormatter.DECIMAL_SEPARATOR + plain.substring(dot + 1)
    }

    /** Количество без лишних нулей: «150», «12,5». */
    fun formatQuantity(value: BigDecimal): String = YarnCalculator.formatCompact(value, 3)
}
