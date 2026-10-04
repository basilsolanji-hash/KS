package com.knit.calculator.core

import java.math.BigDecimal
import java.math.RoundingMode

// ---------------------------------------------------------------- Сумма прописью (для счёта и договора)

object MoneyWords {
    private val unitsMale = listOf("", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять")
    private val unitsFemale = listOf("", "одна", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять")
    private val teens = listOf(
        "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать",
        "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать",
    )
    private val tens = listOf("", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто")
    private val hundreds = listOf("", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот")

    // Разряды: (формы для 1 / 2–4 / 5+, женский род).
    private val scales = listOf(
        Triple(listOf("", "", ""), false, 1L),
        Triple(listOf("тысяча", "тысячи", "тысяч"), true, 1_000L),
        Triple(listOf("миллион", "миллиона", "миллионов"), false, 1_000_000L),
        Triple(listOf("миллиард", "миллиарда", "миллиардов"), false, 1_000_000_000L),
    )

    /** 1 → 0, 2–4 → 1, остальное → 2 (рубль / рубля / рублей). */
    fun form(n: Long): Int {
        val n100 = n % 100
        val n10 = n % 10
        return when {
            n100 in 11..14 -> 2
            n10 == 1L -> 0
            n10 in 2..4 -> 1
            else -> 2
        }
    }

    private fun triad(n: Int, female: Boolean): List<String> = buildList {
        add(hundreds[n / 100])
        val rest = n % 100
        if (rest in 10..19) {
            add(teens[rest - 10])
        } else {
            add(tens[rest / 10])
            add((if (female) unitsFemale else unitsMale)[rest % 10])
        }
    }.filter { it.isNotEmpty() }

    /** Целое число прописью, мужской род: 106800 → «сто шесть тысяч восемьсот». */
    fun number(value: Long): String {
        require(value >= 0) { "Отрицательные суммы не поддерживаются" }
        if (value == 0L) return "ноль"
        val words = mutableListOf<String>()
        for (i in scales.indices.reversed()) {
            val (forms, female, size) = scales[i]
            val part = ((value / size) % 1000).toInt()
            if (part == 0) continue
            words += triad(part, female)
            if (i > 0) words += forms[form(part.toLong())]
        }
        return words.joinToString(" ")
    }

    /** «Сто шесть тысяч восемьсот рублей 00 копеек». */
    fun rubles(amount: BigDecimal): String {
        val money = amount.setScale(2, RoundingMode.HALF_UP)
        val rubles = money.toBigInteger().toLong()
        val kopecks = money.subtract(BigDecimal(rubles)).movePointRight(2).toInt()
        val text = number(rubles) + " " + listOf("рубль", "рубля", "рублей")[form(rubles)] +
            " %02d ".format(kopecks) + listOf("копейка", "копейки", "копеек")[form(kopecks.toLong())]
        return text.replaceFirstChar { it.uppercaseChar() }
    }
}

// ---------------------------------------------------------------- Рабочие дни (срок отгрузки)

object WorkingDays {
    private const val DAY_MS = 86_400_000L

    /** День недели по номеру дня от 01.01.1970: 0 — понедельник … 6 — воскресенье. */
    fun weekday(epochDay: Long): Int = Math.floorMod(epochDay + 3, 7L).toInt()

    /** Через [days] рабочих дней (без суббот и воскресений; праздники не учитываются). */
    fun addEpochDay(startEpochDay: Long, days: Int): Long {
        var day = startEpochDay
        var left = days
        while (left > 0) {
            day++
            if (weekday(day) < 5) left--
        }
        return day
    }

    /** То же для времени в миллисекундах в часовом поясе со смещением [offsetMs]. */
    fun add(startMillis: Long, days: Int, offsetMs: Long): Long {
        val startDay = Math.floorDiv(startMillis + offsetMs, DAY_MS)
        return addEpochDay(startDay, days) * DAY_MS - offsetMs
    }

    /** Наибольшее число в сроке: «5–15 рабочих дней» → 15. */
    fun maxDays(leadTime: String): Int? = Regex("\\d+").findAll(leadTime).map { it.value.toInt() }.maxOrNull()
}

// ---------------------------------------------------------------- Оплаты и долги

data class Payment(
    val id: String,
    val quoteId: String,
    val quoteNumber: Int,
    val client: String,
    val date: Long,
    val amount: BigDecimal,
    val note: String = "",
)

data class Invoice(
    val number: Int,
    val quoteId: String,
    val quoteNumber: Int,
    val client: String,
    val date: Long,
    val amount: BigDecimal,
    val purpose: String,
)

/** Сделка для расчёта долга: КП с суммой и статусом. */
data class Deal(val quoteId: String, val quoteNumber: Int, val client: String, val total: BigDecimal, val status: QuoteStatus)

data class DebtRow(val deal: Deal, val paid: BigDecimal) {
    val remaining: BigDecimal get() = (deal.total - paid).max(BigDecimal.ZERO)
    val overpaid: BigDecimal get() = (paid - deal.total).max(BigDecimal.ZERO)
    val isClosed: Boolean get() = remaining.signum() == 0
}

data class DebtReport(val rows: List<DebtRow>) {
    val totalDebt: BigDecimal get() = rows.fold(BigDecimal.ZERO) { a, r -> a + r.remaining }
    val totalPaid: BigDecimal get() = rows.fold(BigDecimal.ZERO) { a, r -> a + r.paid }
}

object Debts {
    /** Согласованные КП и КП с оплатами; сначала самые большие долги, закрытые — в конце. */
    fun report(deals: List<Deal>, payments: List<Payment>): DebtReport {
        val paid = payments.groupBy { it.quoteId }.mapValues { (_, ps) -> ps.fold(BigDecimal.ZERO) { a, p -> a + p.amount } }
        val rows = deals
            .filter { it.status != QuoteStatus.REJECTED && (it.status.isWon || (paid[it.quoteId]?.signum() ?: 0) > 0) }
            .map { DebtRow(it, paid[it.quoteId] ?: BigDecimal.ZERO) }
            .sortedWith(compareBy<DebtRow> { it.isClosed }.thenByDescending { it.remaining }.thenByDescending { it.deal.quoteNumber })
        return DebtReport(rows)
    }

    /** Сумма счёта на предоплату: [percent] % от суммы КП, до копеек. */
    fun prepayment(total: BigDecimal, percent: BigDecimal): BigDecimal =
        (total * percent).divide(BigDecimal(100), 2, RoundingMode.HALF_UP)
}

// ---------------------------------------------------------------- Заказы на производство

enum class OrderStage(val title: String) {
    NEW("Новый"),
    KNITTING("В вязке"),
    FINISHING("ВТО"),
    PACKING("Упаковка"),
    SHIPPED("Отгружено");

    companion object {
        fun from(text: String): OrderStage = entries.firstOrNull { it.title.equals(text.trim(), ignoreCase = true) } ?: NEW
    }
}

/** Пряжа заказа, кг (с учётом брака) — списывается со склада, когда заказ уходит в вязку. */
data class YarnAmount(val yarn: String, val kg: BigDecimal)

data class ProductionOrder(
    val quoteId: String,
    val quoteNumber: Int,
    val client: String,
    val created: Long,
    val due: Long,
    val stage: OrderStage = OrderStage.NEW,
    val stageDate: Long = created,
    val items: String = "",
    val comment: String = "",
    val yarn: List<YarnAmount> = emptyList(),
    val yarnWrittenOff: Boolean = false,
) {
    fun isOverdue(now: Long): Boolean = stage != OrderStage.SHIPPED && now > due
}

// ---------------------------------------------------------------- Склад пряжи

data class YarnMove(val id: String, val date: Long, val yarn: String, val kg: BigDecimal, val reason: String = "")

data class StockRow(val yarn: String, val kg: BigDecimal)

data class Shortage(val yarn: String, val need: BigDecimal, val have: BigDecimal) {
    val missing: BigDecimal get() = (need - have).max(BigDecimal.ZERO)
}

object YarnStock {
    private fun key(name: String) = name.trim().lowercase()

    /** Остатки по движениям (приход «+», расход «−»); названия без учёта регистра. */
    fun balances(moves: List<YarnMove>): List<StockRow> {
        val sums = linkedMapOf<String, BigDecimal>()
        val names = linkedMapOf<String, String>()
        moves.filter { it.yarn.isNotBlank() }.forEach { m ->
            val k = key(m.yarn)
            names.putIfAbsent(k, m.yarn.trim())
            sums[k] = (sums[k] ?: BigDecimal.ZERO) + m.kg
        }
        return sums.map { (k, kg) -> StockRow(names.getValue(k), kg.setScale(3, RoundingMode.HALF_UP)) }.sortedBy { it.yarn.lowercase() }
    }

    /** Чего не хватает на заказ: потребность против остатка. */
    fun shortages(need: List<YarnAmount>, stock: List<StockRow>): List<Shortage> {
        val have = stock.associate { key(it.yarn) to it.kg }
        return need.map { Shortage(it.yarn, it.kg, have[key(it.yarn)] ?: BigDecimal.ZERO) }
    }
}
