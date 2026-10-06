package com.knit.calculator.core

import java.math.BigDecimal
import java.util.Calendar
import java.util.TimeZone

/** Период графиков динамики на главном экране. */
enum class DynPeriod(val title: String) {
    TODAY("Сегодня"), WEEK("7 дней"), MONTH("30 дней"), MONTHS("По месяцам"), YEARS("По годам"),
}

/** Точность исходных данных (отгрузки МойСклад приходят уже суммами по часам, дням или месяцам). */
enum class Grain { HOUR, DAY, MONTH }

/** Ряд графика: подписи, значения периода и того же места в прошлом периоде (для фона). */
data class DynSeries(
    val labels: List<String>,
    val current: List<BigDecimal>,
    val previous: List<BigDecimal>?,
) {
    val total: BigDecimal get() = current.fold(BigDecimal.ZERO, BigDecimal::add)
    val previousTotal: BigDecimal? get() = previous?.fold(BigDecimal.ZERO, BigDecimal::add)
}

object Dynamics {
    private val TZ: TimeZone = TimeZone.getTimeZone("Europe/Moscow")
    private val MONTHS = listOf("янв", "фев", "мар", "апр", "май", "июн", "июл", "авг", "сен", "окт", "ноя", "дек")
    private val DAYS = listOf("вс", "пн", "вт", "ср", "чт", "пт", "сб")

    private fun cal(t: Long) = Calendar.getInstance(TZ).apply { timeInMillis = t }

    private fun startOf(t: Long, field: Int): Long = cal(t).apply {
        if (field == Calendar.YEAR) set(Calendar.MONTH, 0)
        if (field == Calendar.YEAR || field == Calendar.MONTH) set(Calendar.DAY_OF_MONTH, 1)
        if (field != Calendar.HOUR_OF_DAY) set(Calendar.HOUR_OF_DAY, 0)
        set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
    }.timeInMillis

    private fun add(t: Long, field: Int, n: Int) = cal(t).apply { add(field, n) }.timeInMillis

    /** Шаг и число столбцов периода. */
    private fun shape(p: DynPeriod, now: Long): Triple<Int, Int, Long> = when (p) {
        DynPeriod.TODAY -> Triple(Calendar.HOUR_OF_DAY, cal(now).get(Calendar.HOUR_OF_DAY) + 1, startOf(now, Calendar.DAY_OF_MONTH))
        DynPeriod.WEEK -> Triple(Calendar.DAY_OF_MONTH, 7, add(startOf(now, Calendar.DAY_OF_MONTH), Calendar.DAY_OF_MONTH, -6))
        DynPeriod.MONTH -> Triple(Calendar.DAY_OF_MONTH, 30, add(startOf(now, Calendar.DAY_OF_MONTH), Calendar.DAY_OF_MONTH, -29))
        DynPeriod.MONTHS -> Triple(Calendar.MONTH, 12, add(startOf(now, Calendar.MONTH), Calendar.MONTH, -11))
        DynPeriod.YEARS -> Triple(Calendar.YEAR, 5, add(startOf(now, Calendar.YEAR), Calendar.YEAR, -4))
    }

    /** Сдвиг к прошлому периоду: вчера, прошлые 7/30 дней, те же месяцы год назад; для лет — нет. */
    private fun previousShift(p: DynPeriod): Pair<Int, Int>? = when (p) {
        DynPeriod.TODAY -> Calendar.DAY_OF_MONTH to -1
        DynPeriod.WEEK -> Calendar.DAY_OF_MONTH to -7
        DynPeriod.MONTH -> Calendar.DAY_OF_MONTH to -30
        DynPeriod.MONTHS -> Calendar.YEAR to -1
        DynPeriod.YEARS -> null
    }

    /** Какая точность данных нужна периоду (для заранее посчитанных отгрузок). */
    fun grain(p: DynPeriod): Grain = when (p) {
        DynPeriod.TODAY -> Grain.HOUR
        DynPeriod.WEEK, DynPeriod.MONTH -> Grain.DAY
        DynPeriod.MONTHS, DynPeriod.YEARS -> Grain.MONTH
    }

    private fun label(p: DynPeriod, t: Long): String {
        val c = cal(t)
        return when (p) {
            DynPeriod.TODAY -> c.get(Calendar.HOUR_OF_DAY).toString()
            DynPeriod.WEEK -> DAYS[c.get(Calendar.DAY_OF_WEEK) - 1]
            DynPeriod.MONTH -> c.get(Calendar.DAY_OF_MONTH).toString()
            DynPeriod.MONTHS -> MONTHS[c.get(Calendar.MONTH)]
            DynPeriod.YEARS -> c.get(Calendar.YEAR).toString()
        }
    }

    /** Границы периода [from, to) — для анализов «за период» (топ клиентов, расходы по статьям). */
    fun range(p: DynPeriod, now: Long): Pair<Long, Long> {
        val (field, count, first) = shape(p, now)
        return first to add(first, field, count)
    }

    /** Суммы по столбцам периода и прошлого периода из точек (время, сумма). */
    fun series(items: List<Pair<Long, BigDecimal>>, p: DynPeriod, now: Long): DynSeries {
        val (field, count, first) = shape(p, now)
        val starts = (0..count).map { add(first, field, it) }
        fun sums(shift: Pair<Int, Int>?): List<BigDecimal> {
            val bounds = if (shift == null) starts else starts.map { add(it, shift.first, shift.second) }
            return (0 until count).map { i ->
                items.filter { it.first >= bounds[i] && it.first < bounds[i + 1] }.fold(BigDecimal.ZERO) { a, x -> a + x.second }
            }
        }
        val prev = previousShift(p)
        return DynSeries(starts.take(count).map { label(p, it) }, sums(null), prev?.let { sums(it) })
    }
}
