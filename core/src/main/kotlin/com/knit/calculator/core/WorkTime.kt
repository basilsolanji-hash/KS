package com.knit.calculator.core

import java.util.Calendar
import java.util.TimeZone

/** Рабочая смена сотрудника: начало при входе в приложение, конец — «Закрыть смену». */
data class WorkShift(
    val id: String,
    val person: String,
    val start: Long,
    val end: Long? = null,
    /** Часы телефона расходятся с сервером больше чем на 15 минут. */
    val suspicious: Boolean = false,
) {
    val open: Boolean get() = end == null
}

/** Итог по сотруднику за период: дней, минут, незакрытых смен. */
data class WorkTotal(val person: String, val days: Int, val minutes: Long, val unclosed: Int)

object WorkTime {
    private val TZ: TimeZone = TimeZone.getTimeZone("Europe/Moscow")
    private const val MAX_OPEN_MS = 16 * 3_600_000L

    fun dayStart(t: Long): Long = Calendar.getInstance(TZ).apply {
        timeInMillis = t
        set(Calendar.HOUR_OF_DAY, 0); set(Calendar.MINUTE, 0); set(Calendar.SECOND, 0); set(Calendar.MILLISECOND, 0)
    }.timeInMillis

    fun monthKey(t: Long): String = Calendar.getInstance(TZ).apply { timeInMillis = t }
        .let { "%04d-%02d".format(it.get(Calendar.YEAR), it.get(Calendar.MONTH) + 1) }

    /**
     * Начинать ли смену автоматически при входе: нет открытой смены и сегодня смен ещё не было
     * (вечером открыли приложение посмотреть — новая смена сама не начинается).
     */
    fun shouldAutoStart(shifts: List<WorkShift>, now: Long): Boolean {
        val today = dayStart(now)
        return shifts.none { it.open && it.start >= today } && shifts.none { it.start >= today }
    }

    /** Открытая смена сегодня (вчерашняя незакрытая к сегодняшней не относится). */
    fun current(shifts: List<WorkShift>, now: Long): WorkShift? {
        val today = dayStart(now)
        return shifts.filter { it.open && it.start >= today }.maxByOrNull { it.start }
    }

    /**
     * Отработано минут: закрытые смены — до конца; открытая сегодняшняя — до [now];
     * незакрытая прошлая смена не считается (её исправляет директор).
     */
    fun minutes(s: WorkShift, now: Long): Long {
        val end = s.end ?: if (s.start >= dayStart(now) && now - s.start <= MAX_OPEN_MS) now else return 0
        return ((end - s.start).coerceAtLeast(0)) / 60_000
    }

    fun workedToday(shifts: List<WorkShift>, now: Long): Long {
        val today = dayStart(now)
        return shifts.filter { it.start >= today }.sumOf { minutes(it, now) }
    }

    /** Итоги по сотрудникам за месяц «2026-10». */
    fun totals(shifts: List<WorkShift>, month: String, now: Long): List<WorkTotal> =
        shifts.filter { monthKey(it.start) == month }.groupBy { it.person }.map { (person, xs) ->
            WorkTotal(
                person,
                xs.map { dayStart(it.start) }.distinct().size,
                xs.sumOf { minutes(it, now) },
                xs.count { it.open && it.start < dayStart(now) },
            )
        }.sortedBy { it.person }

    /** «7 ч 05 мин». */
    fun format(minutes: Long): String = "%d ч %02d мин".format(minutes / 60, minutes % 60)
}
