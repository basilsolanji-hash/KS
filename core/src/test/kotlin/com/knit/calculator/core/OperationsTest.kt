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

class MoySkladTest {
    private fun bd(s: String) = BigDecimal(s)

    @Test fun tierPricesExactlyAsInMoySklad() {
        val item = MsItem(
            id = "p1", name = "Подвяз 1×1 ПЭ белый 14×100", article = "11-001", group = "Подвязы/Вязка 1х1",
            buyPrice = bd("143.95"), minPrice = bd("143.95"), stock = bd("120"),
            tiers = listOf(bd("1") to bd("201.53"), bd("10") to bd("194.33"), bd("20") to bd("187.13"), bd("50") to bd("172.74"), bd("100") to bd("165.54"), bd("500") to bd("158.34")),
        )
        val p = MoySklad.product(item)!!
        fun price(q: Int) = QuoteCalculator.unitPrice(p, emptyMap(), BigDecimal(q)).first
        assertEquals(0, bd("201.53").compareTo(price(1)))
        assertEquals(0, bd("201.53").compareTo(price(9)))
        assertEquals(0, bd("194.33").compareTo(price(10)))
        assertEquals(0, bd("172.74").compareTo(price(99)))
        assertEquals(0, bd("165.54").compareTo(price(100)))
        assertEquals(0, bd("158.34").compareTo(price(600)))
        // Позиция 600 шт: 95 004,00 ₽.
        assertEquals(0, bd("95004.00").compareTo(QuoteCalculator.line(QuoteLineInput(p, emptyMap(), BigDecimal(600))).total))
        assertEquals("p1", p.externalId)
        assertEquals(MoySklad.productId("p1"), p.id)
    }

    @Test fun noPriceForOnePiece() {
        val p = MoySklad.product(MsItem("p2", "A", tiers = listOf(bd("100") to bd("50"), bd("500") to bd("45"))))!!
        assertEquals(0, bd("100").compareTo(p.minOrder))
        assertEquals(null, MoySklad.product(MsItem("p3", "B")))
    }

    @Test fun searchByWordsAndGroup() {
        val list = listOf(
            Product(1, "Подвяз 1×1 белый 14×100", "шт", bd("1"), code = "11-001", group = "Подвязы/Вязка 1х1"),
            Product(2, "Подвяз 2×2 чёрный 16×100", "шт", bd("1"), code = "22-013", group = "Подвязы/Вязка 2х2"),
            Product(3, "Поло-воротник белый", "шт", bd("1"), group = "Поло-воротники"),
        )
        assertEquals(listOf(1L), MoySklad.search(list, "белый подвяз").map { it.id })
        assertEquals(listOf(2L), MoySklad.search(list, "22-013").map { it.id })
        assertEquals(listOf(1L, 2L), MoySklad.search(list, "", "Подвязы").map { it.id })
        assertEquals(listOf("Подвязы", "Поло-воротники"), MoySklad.topGroups(list))
    }
}

class MoySkladVariantsTest {
    private fun bd(s: String) = BigDecimal(s)
    private val client = listOf("Состав / материала", "Цвет", "Размер")

    private fun v(id: String, color: String, size: String, badges: List<String> = emptyList()) = MoySklad.product(
        MsItem(
            id, "Подвяз двуслойный 1х1", group = "Подвязы", type = "variant", badges = badges,
            tiers = listOf(bd("1") to bd("150")),
            chars = mapOf("Цвет" to color, "Размер" to size, "Тип резинки" to "1х1", "Состав / материала" to "хлопок 95%", "Артикул" to "A-$id"),
        ),
        client,
    )!!

    @Test fun clientCharacteristicsInName() {
        val p = v("v1", "бордовый / белый", "115х14 см")
        assertEquals("Подвяз двуслойный 1х1, хлопок 95%, бордовый / белый, 115х14 см", p.name)
        assertEquals("variant", p.externalType)
        assertEquals("1х1", p.attributes["Тип резинки"])
    }

    @Test fun filtersAndBadgesFirst() {
        val list = listOf(v("v1", "белый", "115х14 см"), v("v2", "чёрный", "115х14 см", listOf("Топ-продажа")), v("v3", "белый", "115х16 см"))
        assertEquals(listOf("v2", "v1", "v3").map { MoySklad.productId(it) }, MoySklad.search(list, "").map { it.id })
        assertEquals(listOf(MoySklad.productId("v3")), MoySklad.search(list, "", filters = mapOf("Цвет" to "белый", "Размер" to "115х16 см")).map { it.id })
        assertEquals(listOf("белый", "чёрный"), MoySklad.filterValues(list, "Цвет"))
        // Уникальные значения (артикулы) — не чипы, а поиск.
        assertEquals(emptyList<String>(), MoySklad.filterValues(list, "Тип резинки"))
        assertEquals(listOf(MoySklad.productId("v3")), MoySklad.search(list, "A-v3").map { it.id })
    }
}
