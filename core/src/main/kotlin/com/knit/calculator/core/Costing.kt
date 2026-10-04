package com.knit.calculator.core

import java.math.BigDecimal
import java.math.RoundingMode

/** Доля нити в изделии: «Хлопок» 95 %. */
data class YarnShare(val yarn: String, val percent: BigDecimal)

/**
 * Состав нитей в ячейке таблицы: «Хлопок 95; Спандекс 5» или «Полиэстер 100%».
 * Пусто → пустой список. Некорректный текст → `null`.
 */
object Composition {
    fun parse(text: String): List<YarnShare>? {
        if (text.isBlank()) return emptyList()
        return text.split(';', '\n').filter { it.isNotBlank() }.map { part ->
            val cleaned = part.replace("%", "").trim()
            val idx = cleaned.lastIndexOf(' ')
            if (idx <= 0) return null
            val name = cleaned.substring(0, idx).trim()
            val percent = YarnCalculator.parseDecimal(cleaned.substring(idx + 1)) ?: return null
            if (name.isEmpty() || percent.signum() <= 0) return null
            YarnShare(name, percent)
        }
    }

    fun format(shares: List<YarnShare>): String =
        shares.joinToString("; ") { "${it.yarn} ${YarnCalculator.formatCompact(it.percent, 2)} %" }
}

/**
 * Прямые затраты на единицу изделия (лист «Себестоимость», модель «База КП»).
 * [yarnPerUnit] используется, если не задан вес изделия и цена пряжи за кг.
 */
data class ProductCost(
    val yarnPerUnit: BigDecimal = BigDecimal.ZERO,
    val knitMinutes: BigDecimal = BigDecimal.ZERO,
    val minutePrice: BigDecimal = BigDecimal.ZERO,
    val handOperations: BigDecimal = BigDecimal.ZERO,
    val operationPrice: BigDecimal = BigDecimal.ZERO,
    val wto: BigDecimal = BigDecimal.ZERO,
    val packaging: BigDecimal = BigDecimal.ZERO,
    val wastePercent: BigDecimal = BigDecimal.ZERO,
)

/** Общие параметры экономики фабрики (лист «Настройки»). */
data class CostSettings(
    /** Постоянные расходы в месяц, ₽ (аренда, зарплата, кредит…). */
    val fixedMonthly: BigDecimal = BigDecimal.ZERO,
    /** План выпуска, шт в месяц — на него делятся постоянные расходы. */
    val planQuantity: BigDecimal = BigDecimal.ZERO,
    /** Комиссия с цены продажи, %. */
    val commissionPercent: BigDecimal = BigDecimal.ZERO,
    /** Желаемая рентабельность для рекомендованной цены, %. */
    val targetMarginPercent: BigDecimal = BigDecimal.ZERO,
    val vat: VatSettings = VatSettings(BigDecimal(22), included = true),
)

/** Себестоимость единицы: из чего она складывается. */
data class UnitCost(
    val yarn: BigDecimal,
    val labour: BigDecimal,
    val other: BigDecimal,
    val waste: BigDecimal,
    val fixed: BigDecimal,
) {
    val total: BigDecimal get() = yarn + labour + other + waste + fixed
}

/** Экономика позиции КП (видит только менеджер). */
data class LineEconomics(
    val unitCost: UnitCost,
    /** Выручка без НДС и комиссии на единицу. */
    val netUnitRevenue: BigDecimal,
    val unitProfit: BigDecimal,
    val totalCost: BigDecimal,
    val totalProfit: BigDecimal,
    /** Прибыль к себестоимости, %. */
    val marginPercent: BigDecimal,
    /** Цена (как в КП), ниже которой продажа убыточна. */
    val breakEvenPrice: BigDecimal,
    /** Цена (как в КП) для целевой рентабельности. */
    val targetPrice: BigDecimal,
)

data class QuoteEconomics(val lines: List<LineEconomics?>, val totalCost: BigDecimal, val totalProfit: BigDecimal) {
    val marginPercent: BigDecimal
        get() = if (totalCost.signum() == 0) BigDecimal.ZERO
        else totalProfit.multiply(BigDecimal(100)).divide(totalCost, 1, RoundingMode.HALF_UP)
}

object CostCalculator {
    private val HUNDRED = BigDecimal(100)
    private val THOUSAND = BigDecimal(1000)
    private fun div(a: BigDecimal, b: BigDecimal) = a.divide(b, 10, RoundingMode.HALF_UP)

    /** Стоимость пряжи на единицу: вес × доли × цена за кг; если чего-то нет — «Пряжа, ₽/шт». */
    fun yarnCost(cost: ProductCost, weightGrams: BigDecimal?, composition: List<YarnShare>, yarnPrices: Map<String, BigDecimal>): BigDecimal {
        if (weightGrams == null || weightGrams.signum() <= 0 || composition.isEmpty()) return cost.yarnPerUnit
        var sum = BigDecimal.ZERO
        for (share in composition) {
            val price = yarnPrices[share.yarn.trim().lowercase()] ?: return cost.yarnPerUnit
            sum += div(weightGrams * share.percent, HUNDRED * THOUSAND) * price
        }
        return sum
    }

    fun unitCost(cost: ProductCost, settings: CostSettings, yarn: BigDecimal): UnitCost {
        val labour = cost.knitMinutes * cost.minutePrice + cost.handOperations * cost.operationPrice
        val other = cost.wto + cost.packaging
        val waste = div((yarn + labour + other) * cost.wastePercent, HUNDRED)
        val fixed = if (settings.planQuantity.signum() > 0) div(settings.fixedMonthly, settings.planQuantity) else BigDecimal.ZERO
        return UnitCost(yarn, labour, other, waste, fixed)
    }

    /** Доля цены, которая остаётся фабрике: без НДС и комиссии. */
    private fun netShare(settings: CostSettings): BigDecimal {
        val commission = div(settings.commissionPercent, HUNDRED)
        val vat = div(settings.vat.ratePercent, HUNDRED)
        // Цены с НДС: из цены вычитаем НДС; цены без НДС: НДС сверху и фабрике не достаётся.
        return if (settings.vat.included) div(BigDecimal.ONE, BigDecimal.ONE + vat) - commission
        else BigDecimal.ONE - commission
    }

    fun line(
        line: QuoteLine,
        cost: ProductCost?,
        settings: CostSettings,
        yarnPrices: Map<String, BigDecimal>,
    ): LineEconomics? {
        cost ?: return null
        val unit = unitCost(cost, settings, yarnCost(cost, line.weightGrams, line.composition, yarnPrices))
        val share = netShare(settings)
        val netRevenue = line.unitPrice * share
        val unitProfit = netRevenue - unit.total
        val qty = line.quantity
        val totalCost = unit.total * qty
        val setupNet = line.setupFee * share
        val totalProfit = unitProfit * qty + setupNet
        val margin = if (totalCost.signum() == 0) BigDecimal.ZERO else div(totalProfit * HUNDRED, totalCost)
        val breakEven = if (share.signum() > 0) div(unit.total, share) else BigDecimal.ZERO
        val target = breakEven * (BigDecimal.ONE + div(settings.targetMarginPercent, HUNDRED))
        return LineEconomics(
            unitCost = unit,
            netUnitRevenue = netRevenue.setScale(2, RoundingMode.HALF_UP),
            unitProfit = unitProfit.setScale(2, RoundingMode.HALF_UP),
            totalCost = totalCost.setScale(2, RoundingMode.HALF_UP),
            totalProfit = totalProfit.setScale(2, RoundingMode.HALF_UP),
            marginPercent = margin.setScale(1, RoundingMode.HALF_UP),
            breakEvenPrice = breakEven.setScale(0, RoundingMode.CEILING),
            targetPrice = target.setScale(0, RoundingMode.CEILING),
        )
    }

    fun quote(lines: List<LineEconomics?>): QuoteEconomics {
        val known = lines.filterNotNull()
        return QuoteEconomics(
            lines,
            known.fold(BigDecimal.ZERO) { a, l -> a + l.totalCost },
            known.fold(BigDecimal.ZERO) { a, l -> a + l.totalProfit },
        )
    }
}

/** Потребность в пряже на заказ по одной нити. */
data class YarnNeed(
    val yarn: String,
    val netKg: BigDecimal,
    val totalKg: BigDecimal,
    /** Стоимость с учётом брака, если известна цена за кг. */
    val cost: BigDecimal?,
)

data class OrderYarn(val needs: List<YarnNeed>, val missingWeight: List<String>) {
    val totalKg: BigDecimal get() = needs.fold(BigDecimal.ZERO) { a, n -> a + n.totalKg }
    val totalCost: BigDecimal? get() = if (needs.any { it.cost == null }) null else needs.fold(BigDecimal.ZERO) { a, n -> a + (n.cost ?: BigDecimal.ZERO) }
}

object OrderYarnCalculator {
    /** Суммирует пряжу по всем позициям КП; позиции без веса или состава попадают в [OrderYarn.missingWeight]. */
    fun calculate(lines: List<QuoteLine>, wastePercent: BigDecimal, yarnPrices: Map<String, BigDecimal>): OrderYarn {
        val grams = linkedMapOf<String, BigDecimal>()
        val names = linkedMapOf<String, String>()
        val missing = mutableListOf<String>()
        lines.forEach { l ->
            val w = l.weightGrams
            if (w == null || w.signum() <= 0 || l.composition.isEmpty()) {
                missing += l.description
                return@forEach
            }
            l.composition.forEach { share ->
                val key = share.yarn.trim().lowercase()
                names.putIfAbsent(key, share.yarn.trim())
                grams[key] = (grams[key] ?: BigDecimal.ZERO) + (w * share.percent * l.quantity).divide(BigDecimal(100), 10, RoundingMode.HALF_UP)
            }
        }
        val factor = BigDecimal.ONE + wastePercent.divide(BigDecimal(100), 10, RoundingMode.HALF_UP)
        val needs = grams.map { (key, g) ->
            val net = g.divide(BigDecimal(1000), 3, RoundingMode.HALF_UP)
            val total = (g * factor).divide(BigDecimal(1000), 3, RoundingMode.HALF_UP)
            YarnNeed(names.getValue(key), net, total, yarnPrices[key]?.let { (total * it).setScale(2, RoundingMode.HALF_UP) })
        }
        return OrderYarn(needs, missing)
    }
}

// ---------------------------------------------------------------- Отчёт за месяц

enum class QuoteStatus(val title: String) {
    SENT("Отправлено"),
    APPROVED("Согласовано"),
    IN_WORK("В работе"),
    PAID("Оплачено"),
    REJECTED("Отказ");

    /** Клиент согласился: согласовано, в работе или оплачено. */
    val isWon: Boolean get() = this == APPROVED || this == IN_WORK || this == PAID

    companion object {
        fun from(text: String): QuoteStatus = entries.firstOrNull { it.title.equals(text.trim(), ignoreCase = true) } ?: SENT
    }
}

/** Краткие данные КП для отчёта. */
data class QuoteSummary(
    val month: String, // «2026-10»
    val status: QuoteStatus,
    val total: BigDecimal,
    val profit: BigDecimal?,
    val manager: String,
    /** Изделие → сумма по нему в этом КП. */
    val products: Map<String, BigDecimal>,
)

data class ReportRow(val name: String, val count: Int, val sum: BigDecimal, val wonSum: BigDecimal)

data class MonthReport(
    val month: String,
    val count: Int,
    val sum: BigDecimal,
    val wonCount: Int,
    val wonSum: BigDecimal,
    val profit: BigDecimal,
    val byManager: List<ReportRow>,
    val byProduct: List<ReportRow>,
) {
    /** Доля согласованных КП, %. */
    val conversionPercent: BigDecimal
        get() = if (count == 0) BigDecimal.ZERO
        else BigDecimal(wonCount * 100).divide(BigDecimal(count), 1, RoundingMode.HALF_UP)
}

object ReportCalculator {
    fun month(quotes: List<QuoteSummary>, month: String): MonthReport {
        val list = quotes.filter { it.month == month }
        val won = list.filter { it.status.isWon }
        fun sum(xs: List<QuoteSummary>) = xs.fold(BigDecimal.ZERO) { a, q -> a + q.total }
        val byManager = list.groupBy { it.manager.ifBlank { "—" } }.map { (m, xs) ->
            ReportRow(m, xs.size, sum(xs), sum(xs.filter { it.status.isWon }))
        }.sortedByDescending { it.sum }
        val products = linkedMapOf<String, Triple<Int, BigDecimal, BigDecimal>>()
        list.forEach { q ->
            q.products.forEach { (name, s) ->
                val (c, total, wonTotal) = products[name] ?: Triple(0, BigDecimal.ZERO, BigDecimal.ZERO)
                products[name] = Triple(c + 1, total + s, wonTotal + if (q.status.isWon) s else BigDecimal.ZERO)
            }
        }
        return MonthReport(
            month = month,
            count = list.size,
            sum = sum(list),
            wonCount = won.size,
            wonSum = sum(won),
            profit = won.fold(BigDecimal.ZERO) { a, q -> a + (q.profit ?: BigDecimal.ZERO) },
            byManager = byManager,
            byProduct = products.map { (n, t) -> ReportRow(n, t.first, t.second, t.third) }.sortedByDescending { it.sum },
        )
    }
}
