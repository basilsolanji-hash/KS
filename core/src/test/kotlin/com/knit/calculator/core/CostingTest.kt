package com.knit.calculator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal
import java.math.RoundingMode

class CostingTest {
    private fun bd(s: String) = BigDecimal(s)
    private fun assertBd(expected: String, actual: BigDecimal, scale: Int = 2) =
        assertEquals(0, bd(expected).compareTo(actual.setScale(scale, RoundingMode.HALF_UP)))

    private val bazaKp = ProductCost(bd("100"), bd("8"), bd("16"), bd("1"), bd("3"), bd("8"), bd("2"), bd("3"))
    private val settings = CostSettings(bd("954000"), bd("13000"), bd("9"), bd("25"), VatSettings(bd("22"), true))

    private fun line(price: String, qty: String, weight: BigDecimal? = null, composition: List<YarnShare> = emptyList()) =
        QuoteCalculator.line(
            QuoteLineInput(
                Product(id = 1, name = "Изделие", unit = "шт", basePrice = bd(price), weightGrams = weight, composition = composition),
                emptyMap(),
                bd(qty),
            ),
        )

    /** Модель «База КП»: пряжа 100, вязание 8 мин × 16, операция 3, ВТО 8, упаковка 2, брак 3 %, постоянные 954 000 / 13 000. */
    @Test fun unitCostLikeBazaKp() {
        val e = CostCalculator.line(line("170", "600"), bazaKp, settings, emptyMap())!!
        assertBd("321.61", e.unitCost.total)
        assertBd("124.04", e.netUnitRevenue)
        assertBd("-197.57", e.unitProfit)
        assertBd("441", e.breakEvenPrice, 0)
        assertBd("551", e.targetPrice, 0)
        assertTrue(e.totalProfit.signum() < 0)
    }

    @Test fun profitableWhenPriceAboveBreakEven() {
        val e = CostCalculator.line(line("600", "100"), bazaKp, settings, emptyMap())!!
        assertTrue(e.unitProfit.signum() > 0)
        val q = CostCalculator.quote(listOf(e, null))
        assertEquals(0, q.totalProfit.compareTo(e.totalProfit))
    }

    @Test fun noCostDataGivesNull() = assertNull(CostCalculator.line(line("170", "1"), null, settings, emptyMap()))

    @Test fun yarnCostFromWeightAndPrices() {
        val comp = listOf(YarnShare("Хлопок", bd("95")), YarnShare("Спандекс", bd("5")))
        val prices = mapOf("хлопок" to bd("600"), "спандекс" to bd("1200"))
        // 200 г: 190 г × 600 ₽/кг = 114 + 10 г × 1200 ₽/кг = 12 → 126
        assertBd("126", CostCalculator.yarnCost(bazaKp, bd("200"), comp, prices))
        // Нет цены одной из нитей — берём «Пряжа, ₽/шт».
        assertBd("100", CostCalculator.yarnCost(bazaKp, bd("200"), comp, mapOf("хлопок" to bd("600"))))
    }

    @Test fun compositionParsing() {
        assertEquals(listOf(YarnShare("Хлопок", bd("95")), YarnShare("Спандекс", bd("5"))), Composition.parse("Хлопок 95; Спандекс 5"))
        assertEquals(listOf(YarnShare("Полиэстер", bd("100"))), Composition.parse("Полиэстер 100%"))
        assertEquals(emptyList<YarnShare>(), Composition.parse(""))
        assertNull(Composition.parse("Хлопок"))
        assertEquals("Хлопок 95 %; Спандекс 5 %", Composition.format(Composition.parse("Хлопок 95; Спандекс 5")!!))
        assertNull("сумма 120 % — ошибка", Composition.parse("Хлопок 60; Полиэстер 60"))
        assertNull("сумма 80 % — ошибка", Composition.parse("Хлопок 80"))
    }

    @Test fun orderYarnSumsAcrossLines() {
        val cotton = listOf(YarnShare("Хлопок", bd("95")), YarnShare("Спандекс", bd("5")))
        val lines = listOf(
            line("100", "1000", bd("50"), cotton), // 47,5 кг хлопка + 2,5 кг спандекса
            line("100", "500", bd("20"), listOf(YarnShare("хлопок", bd("100")))), // + 10 кг хлопка
            line("100", "10"), // без веса
        )
        val r = OrderYarnCalculator.calculate(lines, bd("10"), mapOf("хлопок" to bd("600")))
        assertEquals(2, r.needs.size)
        assertBd("57.5", r.needs[0].netKg, 3)
        assertBd("63.25", r.needs[0].totalKg, 3)
        assertBd("37950", r.needs[0].cost!!)
        assertNull(r.needs[1].cost)
        assertEquals(1, r.missingWeight.size)
    }

    @Test fun variantOverridesWeightAndComposition() {
        val size = OptionGroup(1, "Размер", listOf(PriceChoice(11, "S", weightGrams = bd("80")), PriceChoice(12, "L", weightGrams = bd("120"))))
        val kind = OptionGroup(2, "Тип", listOf(PriceChoice(21, "Хлопок", composition = listOf(YarnShare("Хлопок", bd("100"))))))
        val p = Product(id = 1, name = "A", unit = "шт", basePrice = bd("10"), options = listOf(size, kind), weightGrams = bd("100"))
        val l = QuoteCalculator.line(QuoteLineInput(p, mapOf(1L to 12L), bd("1")))
        assertBd("120", l.weightGrams!!)
        assertEquals("Хлопок", l.composition.single().yarn)
    }

    @Test fun managerDiscount() {
        val p = Product(id = 1, name = "A", unit = "шт", basePrice = bd("191"))
        val l = QuoteCalculator.line(QuoteLineInput(p, emptyMap(), bd("10"), discountPercent = bd("7")))
        assertBd("191", l.listUnitPrice)
        assertBd("178", l.unitPrice) // 191 × 0,93 = 177,63 → до рубля
        assertBd("1780", l.total)
    }

    @Test fun monthReport() {
        val quotes = listOf(
            QuoteSummary("2026-10", QuoteStatus.SENT, bd("1000"), bd("100"), "Анна", mapOf("Подвяз" to bd("1000"))),
            QuoteSummary("2026-10", QuoteStatus.PAID, bd("3000"), bd("600"), "Анна", mapOf("Подвяз" to bd("2000"), "Воротник" to bd("1000"))),
            QuoteSummary("2026-10", QuoteStatus.APPROVED, bd("500"), null, "Иван", mapOf("Воротник" to bd("500"))),
            QuoteSummary("2026-09", QuoteStatus.PAID, bd("9999"), bd("1"), "Иван", emptyMap()),
        )
        val r = ReportCalculator.month(quotes, "2026-10")
        assertEquals(3, r.count)
        assertBd("4500", r.sum)
        assertEquals(2, r.wonCount)
        assertBd("3500", r.wonSum)
        assertBd("600", r.profit)
        assertBd("66.7", r.conversionPercent, 1)
        assertEquals("Анна", r.byManager.first().name)
        assertEquals(listOf("Подвяз", "Воротник"), r.byProduct.map { it.name })
        assertBd("1500", r.byProduct[1].wonSum)
    }

    @Test fun statusFromText() {
        assertEquals(QuoteStatus.PAID, QuoteStatus.from("оплачено"))
        assertEquals(QuoteStatus.SENT, QuoteStatus.from(""))
    }

    @Test fun catalogParsesNewSheets() {
        val parsed = CatalogParser.parse(
            SeedCatalog.sheets.copy(
                yarns = listOf(listOf("h"), listOf("Хлопок", "650"), listOf("Полиэстер", "x")),
                clients = listOf(listOf("h"), listOf("ООО Ромашка", "Иван", "a@b.ru", "+7", "123")),
            ),
        )
        assertEquals(3, parsed.costs.size)
        assertBd("650", parsed.yarnPrices.getValue("хлопок"))
        assertEquals("ООО Ромашка", parsed.clients.single().company)
        assertTrue(parsed.warnings.any { it.startsWith("Пряжа, строка 3") })
    }
}
