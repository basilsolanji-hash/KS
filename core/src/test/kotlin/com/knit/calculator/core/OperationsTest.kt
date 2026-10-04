package com.knit.calculator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal

class OperationsTest {

    @Test fun moneyInWords() {
        assertEquals("Сто шесть тысяч восемьсот рублей 00 копеек", MoneyWords.rubles(BigDecimal("106800")))
        assertEquals("Один рубль 01 копейка", MoneyWords.rubles(BigDecimal("1.01")))
        assertEquals("Две тысячи двадцать два рубля 45 копеек", MoneyWords.rubles(BigDecimal("2022.45")))
        assertEquals("Ноль рублей 50 копеек", MoneyWords.rubles(BigDecimal("0.5")))
        assertEquals("Одиннадцать тысяч одиннадцать рублей 12 копеек", MoneyWords.rubles(BigDecimal("11011.12")))
        assertEquals(
            "Один миллион двести тридцать четыре тысячи пятьсот шестьдесят семь рублей 89 копеек",
            MoneyWords.rubles(BigDecimal("1234567.89")),
        )
        assertEquals("двадцать одна тысяча", MoneyWords.number(21_000))
    }

    @Test fun workingDaysSkipWeekends() {
        // 01.01.1970 — четверг.
        assertEquals(3, WorkingDays.weekday(0))
        // Пятница + 1 рабочий день = понедельник.
        val friday = 1L
        assertEquals(4, WorkingDays.weekday(friday))
        assertEquals(friday + 3, WorkingDays.addEpochDay(friday, 1))
        // 15 рабочих дней = 3 недели.
        assertEquals(friday + 21, WorkingDays.addEpochDay(friday, 15))
        assertEquals(15, WorkingDays.maxDays("5–15 рабочих дней"))
        assertEquals(null, WorkingDays.maxDays("по договорённости"))
    }

    @Test fun debtsByDeal() {
        val deals = listOf(
            Deal("a", 1, "ООО А", BigDecimal(100_000), QuoteStatus.APPROVED),
            Deal("b", 2, "ООО Б", BigDecimal(50_000), QuoteStatus.PAID),
            Deal("c", 3, "ООО В", BigDecimal(30_000), QuoteStatus.SENT),
            Deal("d", 4, "ООО Г", BigDecimal(20_000), QuoteStatus.SENT),
            Deal("e", 5, "ООО Д", BigDecimal(10_000), QuoteStatus.REJECTED),
        )
        val payments = listOf(
            Payment("p1", "a", 1, "ООО А", 0, BigDecimal(50_000)),
            Payment("p2", "b", 2, "ООО Б", 0, BigDecimal(50_000)),
            Payment("p3", "c", 3, "ООО В", 0, BigDecimal(10_000)),
        )
        val report = Debts.report(deals, payments)
        // «Отправлено» без оплат и «Отказ» не считаются долгом; закрытые — в конце.
        assertEquals(listOf("a", "c", "b"), report.rows.map { it.deal.quoteId })
        assertEquals(0, BigDecimal(70_000).compareTo(report.totalDebt))
        assertTrue(report.rows.last().isClosed)
        assertEquals(0, BigDecimal("53400.00").compareTo(Debts.prepayment(BigDecimal(106_800), BigDecimal(50))))
    }

    @Test fun yarnStockAndShortages() {
        val moves = listOf(
            YarnMove("1", 0, "Полиэстер", BigDecimal("25")),
            YarnMove("2", 0, "полиэстер ", BigDecimal("-5.5")),
            YarnMove("3", 0, "Хлопок", BigDecimal("10")),
        )
        val stock = YarnStock.balances(moves)
        assertEquals(listOf("Полиэстер", "Хлопок"), stock.map { it.yarn })
        assertEquals(0, BigDecimal("19.5").compareTo(stock[0].kg))
        val short = YarnStock.shortages(listOf(YarnAmount("ПОЛИЭСТЕР", BigDecimal(30)), YarnAmount("Спандекс", BigDecimal(1))), stock)
        assertEquals(0, BigDecimal("10.5").compareTo(short[0].missing))
        assertEquals(0, BigDecimal.ONE.compareTo(short[1].missing))
    }

    @Test fun orderOverdueAndStages() {
        val order = ProductionOrder("q", 1, "ООО", created = 0, due = 1_000)
        assertTrue(order.isOverdue(2_000))
        assertFalse(order.copy(stage = OrderStage.SHIPPED).isOverdue(2_000))
        assertEquals(OrderStage.FINISHING, OrderStage.from("вто"))
    }
}

class QrCodeTest {
    @Test fun shopLinkQr() {
        val m = QrCode.matrix("https://fabrika-ks.ru/shop")
        // Версия 2 (25×25) или больше; квадрат; угловые метки на месте.
        assertTrue(m.size >= 25)
        assertTrue(m.all { it.size == m.size })
        assertTrue(m[0][0] && m[0][6] && m[6][0] && !m[1][1])
    }
}

class ContractTemplateTest {
    @Test fun fillsPlaceholders() {
        val text = ContractTemplate.fill(listOf("Договор № {номер} на {сумма}, {неизвестно}"), mapOf("номер" to "12", "сумма" to "100 ₽"))
        assertEquals("Договор № 12 на 100 ₽, {неизвестно}", text[0])
        assertEquals(ContractTemplate.DEFAULT, ContractTemplate.fromSheet(listOf(listOf("Текст договора"))))
        assertEquals(listOf("# Заголовок", "Абзац"), ContractTemplate.fromSheet(listOf(listOf("Текст"), listOf("# Заголовок"), listOf(""), listOf("Абзац"))))
        // Все ключи шаблона по умолчанию известны приложению.
        val keys = ContractTemplate.DEFAULT.flatMap { Regex("\\{([^{}]+)\\}").findAll(it).map { m -> m.groupValues[1] } }.toSet()
        assertEquals(setOf("номер", "город", "дата", "поставщик", "директор", "покупатель", "сумма", "сумма_прописью", "ставка_ндс", "ндс", "предоплата", "срок", "доставка"), keys)
    }
}
