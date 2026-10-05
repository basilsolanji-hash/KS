package com.knit.calculator.core

import java.math.BigDecimal

/** Панель дня на главном экране: долги, просроченные заказы, КП без ответа. */
data class DaySummary(
    val debt: BigDecimal,
    val debtors: Int,
    val overdueOrders: Int,
    val waitingQuotes: Int,
) {
    val isEmpty: Boolean get() = debt.signum() == 0 && overdueOrders == 0 && waitingQuotes == 0
}

object Dashboard {
    private const val DAY = 86_400_000L

    /**
     * [sentAt] — когда создано КП (ID → время); «без ответа» — в статусе «Отправлено» дольше [waitDays] дней.
     */
    fun summary(
        deals: List<Deal>,
        sentAt: Map<String, Long>,
        payments: List<Payment>,
        orders: List<ProductionOrder>,
        now: Long,
        waitDays: Int = 3,
    ): DaySummary {
        val debts = Debts.report(deals, payments).rows.filter { it.remaining.signum() > 0 }
        return DaySummary(
            debt = debts.fold(BigDecimal.ZERO) { a, r -> a + r.remaining },
            debtors = debts.size,
            overdueOrders = orders.count { it.isOverdue(now) },
            waitingQuotes = deals.count { d -> d.status == QuoteStatus.SENT && (sentAt[d.quoteId] ?: now) < now - waitDays * DAY },
        )
    }
}
