package com.knit.calculator.core

import java.math.BigDecimal
import java.math.RoundingMode
import java.util.Calendar
import java.util.TimeZone

/** Движение денег в платёжном календаре: дата, сумма (всегда > 0), приход или расход. */
data class CashItem(val date: Long, val amount: BigDecimal, val label: String, val inflow: Boolean, val category: String = "")

/** Неделя платёжного календаря: приходы, расходы и остаток на конец недели. */
data class CashWeek(
    val start: Long,
    val inflow: BigDecimal,
    val outflow: BigDecimal,
    val balance: BigDecimal,
    val items: List<CashItem>,
) {
    /** Кассовый разрыв: денег на конец недели меньше нуля. */
    val gap: Boolean get() = balance.signum() < 0
}

/** Регулярный платёж из листа «Регулярные платежи». */
data class RegularPayment(val name: String, val amount: BigDecimal, val day: Int, val category: String = "")

/**
 * Платёжный календарь (ДДС) на N недель вперёд: остаток сейчас + ожидаемые поступления по КП
 * (предоплата при согласовании, остаток к отгрузке) − регулярные платежи − счета поставщиков.
 */
object CashFlow {
    private const val DAY = 86_400_000L
    private val TZ: TimeZone = TimeZone.getTimeZone("Europe/Moscow")

    private fun cal(t: Long) = Calendar.getInstance(TZ).apply { timeInMillis = t }

    /** Понедельник 00:00 недели, в которой лежит [t]. */
    fun weekStart(t: Long): Long = cal(t).apply {
        set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
        val dow = (get(Calendar.DAY_OF_WEEK) + 5) % 7 // понедельник = 0
        add(Calendar.DAY_OF_YEAR, -dow)
    }.timeInMillis

    /** Регулярные платежи в периоде [from, to): по дню месяца (31-е в коротком месяце — последний день). */
    fun regular(payments: List<RegularPayment>, from: Long, to: Long): List<CashItem> {
        val out = mutableListOf<CashItem>()
        val month = cal(from).apply { set(Calendar.DAY_OF_MONTH, 1); set(Calendar.HOUR_OF_DAY, 12); set(Calendar.MINUTE, 0) }
        while (month.timeInMillis < to) {
            payments.forEach { p ->
                val d = (month.clone() as Calendar).apply { set(Calendar.DAY_OF_MONTH, minOf(p.day, getActualMaximum(Calendar.DAY_OF_MONTH))) }
                if (d.timeInMillis in from until to) out += CashItem(d.timeInMillis, p.amount, p.name, inflow = false, category = p.category)
            }
            month.add(Calendar.MONTH, 1)
        }
        return out
    }

    /**
     * Ожидаемые поступления по согласованным КП и КП «в работе»: предоплата — сейчас (если ещё не внесена),
     * остаток — к сроку отгрузки заказа (или через [leadDays] дней, если заказа нет).
     */
    fun expectedInflows(
        deals: List<Deal>,
        payments: List<Payment>,
        orders: List<ProductionOrder>,
        prepayPercent: BigDecimal,
        now: Long,
        leadDays: Int = 21,
    ): List<CashItem> {
        val paidBy = payments.groupBy { it.quoteId }.mapValues { (_, ps) -> ps.fold(BigDecimal.ZERO) { a, p -> a + p.amount } }
        val dueBy = orders.associate { it.quoteId to it.due }
        return deals.filter { it.status == QuoteStatus.APPROVED || it.status == QuoteStatus.IN_WORK }.flatMap { d ->
            val paid = paidBy[d.quoteId] ?: BigDecimal.ZERO
            val prepay = Debts.prepayment(d.total, prepayPercent)
            val items = mutableListOf<CashItem>()
            if (paid < prepay) items += CashItem(now, prepay - paid, "Предоплата КП № ${d.quoteNumber} · ${d.client}", inflow = true)
            val rest = d.total - paid.max(prepay)
            if (rest.signum() > 0) {
                val due = (dueBy[d.quoteId] ?: (now + leadDays * DAY)).coerceAtLeast(now)
                items += CashItem(due, rest, "Остаток КП № ${d.quoteNumber} · ${d.client}", inflow = true)
            }
            items
        }
    }

    /** Недели с остатком на конец каждой; просроченные движения (раньше [now]) попадают в первую неделю. */
    fun weeks(balance: BigDecimal, items: List<CashItem>, now: Long, count: Int = 12): List<CashWeek> {
        val first = weekStart(now)
        var running = balance
        return (0 until count).map { i ->
            val start = first + i * 7 * DAY
            val end = start + 7 * DAY
            val inWeek = items.filter { (if (i == 0) it.date < end else it.date in start until end) }.sortedBy { it.date }
            val inflow = inWeek.filter { it.inflow }.fold(BigDecimal.ZERO) { a, x -> a + x.amount }
            val outflow = inWeek.filter { !it.inflow }.fold(BigDecimal.ZERO) { a, x -> a + x.amount }
            running = running + inflow - outflow
            CashWeek(start, inflow, outflow, running, inWeek)
        }
    }
}

/** Суммы по месяцам: последние [count] месяцев, от старого к новому («2026-10» → сумма). */
fun monthlySums(items: List<Pair<Long, BigDecimal>>, now: Long, count: Int = 6): List<Pair<String, BigDecimal>> {
    val tz = TimeZone.getTimeZone("Europe/Moscow")
    fun key(t: Long) = Calendar.getInstance(tz).apply { timeInMillis = t }.let { "%04d-%02d".format(it.get(Calendar.YEAR), it.get(Calendar.MONTH) + 1) }
    val keys = (count - 1 downTo 0).map { i -> Calendar.getInstance(tz).apply { timeInMillis = now; set(Calendar.DAY_OF_MONTH, 1); add(Calendar.MONTH, -i) }.timeInMillis }.map(::key)
    val sums = items.filter { it.first > 0 }.groupBy { key(it.first) }.mapValues { (_, xs) -> xs.fold(BigDecimal.ZERO) { a, x -> a + x.second } }
    return keys.map { it to (sums[it] ?: BigDecimal.ZERO) }
}

/** КП для отчёта директора. */
data class Sale(
    val id: String,
    val number: Int,
    val client: String,
    val total: BigDecimal,
    val status: QuoteStatus,
    val date: Long,
    val author: String,
    val profit: BigDecimal? = null,
    /** Товар → сумма в КП. */
    val products: Map<String, BigDecimal> = emptyMap(),
)

data class AbcRow(val name: String, val value: BigDecimal, val share: BigDecimal, val group: Char)

data class ManagerRow(val name: String, val quotes: Int, val won: Int, val wonSum: BigDecimal, val profit: BigDecimal?) {
    val conversion: Int get() = if (quotes == 0) 0 else won * 100 / quotes
}

data class MonthRow(val month: String, val quotes: Int, val wonSum: BigDecimal, val paid: BigDecimal, val plan: BigDecimal) {
    /** Выполнение плана по оплатам, %. */
    val planPercent: Int get() = if (plan.signum() == 0) 0 else paid.multiply(BigDecimal(100)).divide(plan, 0, RoundingMode.HALF_UP).toInt()
}

/** Отчёт директора: ABC клиентов и товаров, менеджеры, план/факт по месяцам. */
object DirectorReport {
    private val WON = setOf(QuoteStatus.APPROVED, QuoteStatus.IN_WORK, QuoteStatus.PAID)

    /** ABC: A — первые 80 % суммы, B — следующие 15 %, C — остальное. */
    fun abc(values: Map<String, BigDecimal>): List<AbcRow> {
        val rows = values.filter { it.value.signum() > 0 }.entries.sortedByDescending { it.value }
        val total = rows.fold(BigDecimal.ZERO) { a, e -> a + e.value }
        if (total.signum() == 0) return emptyList()
        var cum = BigDecimal.ZERO
        return rows.map { (name, v) ->
            val before = cum
            cum += v
            val beforePct = before.multiply(BigDecimal(100)).divide(total, 4, RoundingMode.HALF_UP)
            val group = when {
                beforePct < BigDecimal(80) -> 'A'
                beforePct < BigDecimal(95) -> 'B'
                else -> 'C'
            }
            AbcRow(name, v, v.multiply(BigDecimal(100)).divide(total, 1, RoundingMode.HALF_UP), group)
        }
    }

    fun clients(sales: List<Sale>): List<AbcRow> =
        abc(sales.filter { it.status in WON }.groupBy { it.client.trim() }.mapValues { (_, s) -> s.fold(BigDecimal.ZERO) { a, x -> a + x.total } })

    fun products(sales: List<Sale>): List<AbcRow> {
        val sums = mutableMapOf<String, BigDecimal>()
        sales.filter { it.status in WON }.forEach { s -> s.products.forEach { (p, v) -> sums[p] = (sums[p] ?: BigDecimal.ZERO) + v } }
        return abc(sums)
    }

    fun managers(sales: List<Sale>): List<ManagerRow> = sales.groupBy { it.author.substringBefore(" / ").trim().ifBlank { "—" } }.map { (name, s) ->
        val won = s.filter { it.status in WON }
        val profits = won.mapNotNull { it.profit }
        ManagerRow(name, s.size, won.size, won.fold(BigDecimal.ZERO) { a, x -> a + x.total }, if (profits.isEmpty()) null else profits.fold(BigDecimal.ZERO) { a, x -> a + x })
    }.sortedByDescending { it.wonSum }

    /** Последние [count] месяцев: КП, согласовано, оплачено (по датам оплат) и план. */
    fun months(sales: List<Sale>, payments: List<Payment>, plan: BigDecimal, now: Long, count: Int = 6): List<MonthRow> {
        val tz = TimeZone.getTimeZone("Europe/Moscow")
        fun key(t: Long) = Calendar.getInstance(tz).apply { timeInMillis = t }.let { "%04d-%02d".format(it.get(Calendar.YEAR), it.get(Calendar.MONTH) + 1) }
        val keys = (0 until count).map { i -> Calendar.getInstance(tz).apply { timeInMillis = now; set(Calendar.DAY_OF_MONTH, 1); add(Calendar.MONTH, -i) }.timeInMillis }.map(::key)
        return keys.map { k ->
            val inMonth = sales.filter { key(it.date) == k }
            MonthRow(
                month = k,
                quotes = inMonth.size,
                wonSum = inMonth.filter { it.status in WON }.fold(BigDecimal.ZERO) { a, x -> a + x.total },
                paid = payments.filter { it.date > 0 && key(it.date) == k }.fold(BigDecimal.ZERO) { a, p -> a + p.amount },
                plan = plan,
            )
        }
    }

    /** Средний чек согласованных КП. */
    fun averageCheck(sales: List<Sale>): BigDecimal {
        val won = sales.filter { it.status in WON }
        return if (won.isEmpty()) BigDecimal.ZERO else won.fold(BigDecimal.ZERO) { a, x -> a + x.total }.divide(BigDecimal(won.size), 2, RoundingMode.HALF_UP)
    }
}

/** Расход по статье (МойСклад: исходящий платёж или расходный ордер). */
data class Expense(val date: Long, val amount: BigDecimal, val category: String)

/** Строка отчёта по статьям: сумма за период, доля, сумма за предыдущий такой же период. */
data class ExpenseRow(val category: String, val sum: BigDecimal, val share: BigDecimal, val previous: BigDecimal) {
    /** Изменение к предыдущему периоду, %; `null` — раньше расходов не было. */
    val changePercent: Int?
        get() = if (previous.signum() <= 0) null else sum.subtract(previous).multiply(BigDecimal(100)).divide(previous, 0, RoundingMode.HALF_UP).toInt()
}

/** Периоды анализа расходов. */
enum class ExpensePeriod(val title: String) { THIS_MONTH("Этот месяц"), LAST_MONTH("Прошлый месяц"), QUARTER("3 месяца"), HALF_YEAR("6 месяцев") }

/** Расходы по статьям за период с сравнением с предыдущим периодом той же длины. */
object ExpenseReport {
    private val TZ: TimeZone = TimeZone.getTimeZone("Europe/Moscow")

    private fun monthStart(now: Long, back: Int): Long = Calendar.getInstance(TZ).apply {
        timeInMillis = now
        set(Calendar.DAY_OF_MONTH, 1); set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
        add(Calendar.MONTH, -back)
    }.timeInMillis

    /** Период [from, to) и предыдущий такой же [prevFrom, from). */
    fun range(period: ExpensePeriod, now: Long): Triple<Long, Long, Long> = when (period) {
        // Этот месяц сравниваем с тем же числом прошлого месяца — честно для неполного месяца.
        ExpensePeriod.THIS_MONTH -> Triple(monthStart(now, 0), now + 1, monthStart(now, 1))
        ExpensePeriod.LAST_MONTH -> Triple(monthStart(now, 1), monthStart(now, 0), monthStart(now, 2))
        ExpensePeriod.QUARTER -> Triple(monthStart(now, 2), now + 1, monthStart(now, 5))
        ExpensePeriod.HALF_YEAR -> Triple(monthStart(now, 5), now + 1, monthStart(now, 11))
    }

    fun byCategory(items: List<Expense>, period: ExpensePeriod, now: Long): List<ExpenseRow> {
        val (from, to, prevFrom) = range(period, now)
        // Для текущего месяца предыдущий период — до того же дня прошлого месяца.
        val prevTo = if (period == ExpensePeriod.THIS_MONTH) {
            Calendar.getInstance(TZ).apply { timeInMillis = now; add(Calendar.MONTH, -1) }.timeInMillis + 1
        } else from
        fun sums(a: Long, b: Long) = items.filter { it.date in a until b }.groupBy { it.category.ifBlank { "Без статьи" } }
            .mapValues { (_, xs) -> xs.fold(BigDecimal.ZERO) { s, x -> s + x.amount } }
        val now1 = sums(from, to)
        val before = sums(prevFrom, prevTo)
        val total = now1.values.fold(BigDecimal.ZERO, BigDecimal::add)
        return now1.map { (cat, sum) ->
            ExpenseRow(
                cat, sum,
                if (total.signum() == 0) BigDecimal.ZERO else sum.multiply(BigDecimal(100)).divide(total, 1, RoundingMode.HALF_UP),
                before[cat] ?: BigDecimal.ZERO,
            )
        }.sortedByDescending { it.sum }
    }
}
