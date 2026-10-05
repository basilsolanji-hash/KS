package com.knit.calculator.core

import java.math.BigDecimal
import java.math.RoundingMode

/**
 * Множитель цены в виде точной дроби: «1,04» = 104/100, «13/14» = 13/14.
 * Дробь хранится без потери точности, чтобы округление совпадало с формулами таблиц.
 */
data class Factor(val numerator: BigDecimal, val denominator: BigDecimal) {
    val value: BigDecimal get() = numerator.divide(denominator, 12, RoundingMode.HALF_UP)

    companion object {
        val ONE = Factor(BigDecimal.ONE, BigDecimal.ONE)
    }
}

/**
 * Разбор коэффициентов из ячейки таблицы.
 *
 * «1,04*1,04» → два множителя, «13/14» → дробь, пусто или «1» → без изменения.
 * Разделители множителей: `*`, `×`, `x`. Возвращает `null`, если текст некорректен.
 */
object Coefficients {
    fun parse(text: String): List<Factor>? {
        val cleaned = text.filterNot { it.isWhitespace() || it == '\u00A0' || it == NumberFormatter.GROUP_SEPARATOR }
        if (cleaned.isEmpty()) return emptyList()
        return cleaned.split('*', '×', 'x', 'х').map { part ->
            val pieces = part.split('/')
            if (pieces.size > 2) return null
            val num = YarnCalculator.parseDecimal(pieces[0]) ?: return null
            val den = if (pieces.size == 2) YarnCalculator.parseDecimal(pieces[1]) ?: return null else BigDecimal.ONE
            if (den.signum() == 0 || num.signum() < 0 || den.signum() < 0) return null
            Factor(num, den)
        }.filterNot { it.numerator.compareTo(it.denominator) == 0 }
    }

    /** Произведение множителей одной дробью (для коэффициента объёма — округление один раз). */
    fun product(factors: List<Factor>): Factor =
        factors.fold(Factor.ONE) { acc, f -> Factor(acc.numerator * f.numerator, acc.denominator * f.denominator) }
}

/**
 * Вариант параметра изделия.
 *
 * Цена меняется так: каждый множитель из [factors] применяется по очереди с округлением,
 * затем прибавляется [priceAdd] (тоже с округлением).
 * Пример: «Хлопок 2×2» = «1,04*1,04*1,04» — три шага по +4 % с округлением, как в прайсе.
 */
data class PriceChoice(
    val id: Long,
    val name: String,
    val priceAdd: BigDecimal = BigDecimal.ZERO,
    val factors: List<Factor> = emptyList(),
    val factorText: String = "",
    /** Вес изделия, г, если вариант его меняет (например, размер). */
    val weightGrams: BigDecimal? = null,
    /** Состав нитей, если вариант его меняет (например, «Хлопок 1×1» → хлопок 100 %). */
    val composition: List<YarnShare>? = null,
)

/** Параметр изделия: размер, тип нити, рисунок, цвет… Порядок параметров = порядок применения. */
data class OptionGroup(val id: Long, val name: String, val choices: List<PriceChoice>)

/**
 * Коэффициент объёма: начиная с [fromQuantity] единиц цена умножается на [factor]
 * (меньше 1 — скидка, больше 1 — наценка за малый тираж). Округление — один раз.
 */
data class PriceTier(val fromQuantity: BigDecimal, val factor: Factor, val factorText: String = "")

/** Позиция ассортимента. Все цены — за единицу [unit]. */
data class Product(
    val id: Long,
    val name: String,
    val unit: String,
    val basePrice: BigDecimal,
    val minOrder: BigDecimal = BigDecimal.ZERO,
    /** Разовая стоимость подготовки (наладка, образец) на позицию заказа. */
    val setupFee: BigDecimal = BigDecimal.ZERO,
    val options: List<OptionGroup> = emptyList(),
    val tiers: List<PriceTier> = emptyList(),
    /** Шаг округления цены после каждого шага расчёта: 1 — до рубля, 0,01 — до копейки. */
    val rounding: BigDecimal = BigDecimal.ONE,
    /** Вес одного изделия, г (для расхода пряжи и себестоимости). */
    val weightGrams: BigDecimal? = null,
    /** Состав нитей по умолчанию. */
    val composition: List<YarnShare> = emptyList(),
    /** Код изделия из таблицы (для связи параметров и коэффициентов). */
    val code: String = "",
    /** ID товара в МойСклад; пусто — изделие из таблицы (калькулятор «под заказ»). */
    val externalId: String = "",
    /** Группа товаров МойСклад («Подвязы/Вязка 1х1»). */
    val group: String = "",
    /** Остаток на складе; `null` — не известен. */
    val stock: BigDecimal? = null,
    /** Себестоимость (закупочная цена МойСклад) за единицу. */
    val buyPrice: BigDecimal? = null,
    /** Минимальная цена продажи за единицу. */
    val minPrice: BigDecimal? = null,
    val description: String = "",
    /** Характеристики модификации МойСклад (Цвет, Размер, Артикул…). */
    val attributes: Map<String, String> = emptyMap(),
    /** «Топ-продажа», «Популярный». */
    val badges: List<String> = emptyList(),
    /** «product» или «variant» — тип позиции в МойСклад. */
    val externalType: String = "product",
)

/** Выбор клиента: изделие, варианты параметров (id группы → id варианта) и количество. */
data class QuoteLineInput(
    val product: Product,
    val selected: Map<Long, Long>,
    val quantity: BigDecimal,
    /** Скидка менеджера, %, применяется к цене за единицу после всех шагов. */
    val discountPercent: BigDecimal = BigDecimal.ZERO,
)

data class QuoteLine(
    val product: Product,
    val choices: List<Pair<OptionGroup, PriceChoice>>,
    val quantity: BigDecimal,
    /** Применённый коэффициент объёма (1 — без изменения). */
    val volumeFactor: BigDecimal,
    /** Цена за единицу по прайсу (до скидки менеджера). */
    val listUnitPrice: BigDecimal,
    /** Скидка менеджера, %. */
    val discountPercent: BigDecimal,
    /** Цена за единицу после всех шагов расчёта и скидки. */
    val unitPrice: BigDecimal,
    val setupFee: BigDecimal,
    val total: BigDecimal,
    val belowMinimum: Boolean,
    /** Вес единицы с учётом выбранных вариантов, г (`null` — не задан). */
    val weightGrams: BigDecimal? = null,
    /** Состав нитей с учётом выбранных вариантов. */
    val composition: List<YarnShare> = emptyList(),
) {
    /** «Подвязы (Размер: 115×14; Тип: Хлопок 1×1)». */
    val description: String
        get() = if (choices.isEmpty()) product.name
        else product.name + " (" + parameters + ")"

    /** «Размер: 115×14; Тип: Хлопок 1×1». */
    val parameters: String
        get() = choices.joinToString("; ") { (g, c) -> "${g.name}: ${c.name}" }
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
 * Расчёт стоимости заказа для коммерческого предложения — по системе прайса фабрики:
 *
 * 1. цена = базовая цена × коэффициент объёма (по наибольшему достигнутому порогу), округление;
 * 2. для каждого параметра по порядку: каждый множитель варианта с округлением, затем надбавка в рублях;
 * 3. сумма позиции = цена × количество + разовая подготовка.
 *
 * Округление — «половина вверх» до шага [Product.rounding] (как ROUND в Google Таблицах).
 */
object QuoteCalculator {
    private val HUNDRED = BigDecimal(100)

    fun unitPrice(product: Product, selected: Map<Long, Long>, quantity: BigDecimal): Pair<BigDecimal, BigDecimal> {
        val step = product.rounding.takeIf { it.signum() > 0 } ?: BigDecimal("0.01")
        val tier = tierFor(product, quantity)
        val volume = tier?.factor ?: Factor.ONE
        var price = round(product.basePrice * volume.numerator, volume.denominator, step)
        choicesFor(product, selected).forEach { (_, choice) ->
            choice.factors.forEach { f -> price = round(price * f.numerator, f.denominator, step) }
            if (choice.priceAdd.signum() != 0) price = round(price + choice.priceAdd, BigDecimal.ONE, step)
        }
        return price.max(BigDecimal.ZERO) to volume.value
    }

    fun line(input: QuoteLineInput): QuoteLine {
        val product = input.product
        val (price, volume) = unitPrice(product, input.selected, input.quantity)
        val step = product.rounding.takeIf { it.signum() > 0 } ?: BigDecimal("0.01")
        val discount = input.discountPercent.coerceIn(BigDecimal.ZERO, HUNDRED)
        val discounted = if (discount.signum() == 0) price
        else round(price * (HUNDRED - discount), HUNDRED, step)
        val unitPrice = money(discounted)
        val choices = choicesFor(product, input.selected)
        return QuoteLine(
            product = product,
            choices = choices,
            quantity = input.quantity,
            volumeFactor = volume,
            listUnitPrice = money(price),
            discountPercent = discount,
            unitPrice = unitPrice,
            setupFee = money(product.setupFee),
            total = money(unitPrice * input.quantity) + money(product.setupFee),
            belowMinimum = input.quantity < product.minOrder,
            weightGrams = choices.lastOrNull { it.second.weightGrams != null }?.second?.weightGrams ?: product.weightGrams,
            composition = choices.lastOrNull { it.second.composition != null }?.second?.composition ?: product.composition,
        )
    }

    fun tierFor(product: Product, quantity: BigDecimal): PriceTier? =
        product.tiers.filter { it.fromQuantity <= quantity }.maxByOrNull { it.fromQuantity }

    private fun choicesFor(product: Product, selected: Map<Long, Long>): List<Pair<OptionGroup, PriceChoice>> =
        product.options.mapNotNull { group ->
            val choice = group.choices.firstOrNull { it.id == selected[group.id] } ?: group.choices.firstOrNull()
            choice?.let { group to it }
        }

    /** Точное `numerator / denominator`, округлённое до шага `step` (половина — вверх). */
    private fun round(numerator: BigDecimal, denominator: BigDecimal, step: BigDecimal): BigDecimal =
        numerator.divide(denominator * step, 0, RoundingMode.HALF_UP) * step

    fun totals(lines: List<QuoteLine>, vat: VatSettings): QuoteTotals {
        val subtotal = lines.fold(BigDecimal.ZERO) { acc, l -> acc + l.total }
        val rate = vat.ratePercent.max(BigDecimal.ZERO)
        return if (vat.included) {
            val vatAmount = (subtotal * rate).divide(HUNDRED + rate, 2, RoundingMode.HALF_UP)
            QuoteTotals(lines, subtotal, vatAmount, subtotal, subtotal - vatAmount)
        } else {
            val vatAmount = (subtotal * rate).divide(HUNDRED, 2, RoundingMode.HALF_UP)
            QuoteTotals(lines, subtotal, vatAmount, subtotal + vatAmount, subtotal)
        }
    }

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

    /** Коэффициент для показа: «×1,07», «×0,9». */
    fun formatFactor(value: BigDecimal): String = "×" + YarnCalculator.formatCompact(value, 4)
}
