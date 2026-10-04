package com.knit.calculator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal

class QuoteCalculatorTest {
    private fun bd(s: String) = BigDecimal(s)

    private fun assertBd(expected: String, actual: BigDecimal) =
        assertEquals("expected $expected but was $actual", 0, bd(expected).compareTo(actual))

    private val catalog = CatalogParser.parse(SeedCatalog.sheets)
    private val podv = catalog.products.first { it.code == "PODV" }

    private fun podvPrice(width: Int, type: String, qty: Int): BigDecimal {
        val size = podv.options.first { it.name == "Размер" }
        val kind = podv.options.first { it.name == "Тип" }
        val selected = mapOf(
            size.id to size.choices.first { it.name == "115×$width" }.id,
            kind.id to kind.choices.first { it.name == type }.id,
        )
        return QuoteCalculator.unitPrice(podv, selected, BigDecimal(qty)).first
    }

    /** Все 192 цены из «Прайс-листа подвязов KS» (лист «Прайс-лист»). */
    @Test fun matchesFactoryPriceListExactly() {
        val types = listOf("Полиэстер 1×1", "Хлопок 1×1", "Полиэстер 2×2", "Хлопок 2×2")
        val quantities = listOf(500, 100, 50, 20, 10, 1) // 500+, 100-499, 50-99, 20-49, 10-19, 1-9
        val table = mapOf(
            13 to listOf(listOf(158, 164, 171, 178), listOf(169, 176, 183, 190), listOf(174, 181, 188, 196), listOf(213, 222, 231, 240), listOf(259, 269, 280, 291), listOf(316, 329, 342, 356)),
            14 to listOf(listOf(170, 177, 184, 191), listOf(182, 189, 197, 205), listOf(187, 194, 202, 210), listOf(229, 238, 248, 258), listOf(279, 290, 302, 314), listOf(340, 354, 368, 383)),
            15 to listOf(listOf(182, 189, 197, 205), listOf(195, 203, 211, 219), listOf(200, 208, 216, 225), listOf(245, 255, 265, 276), listOf(299, 311, 323, 336), listOf(364, 379, 394, 410)),
            16 to listOf(listOf(194, 202, 210, 218), listOf(208, 216, 225, 234), listOf(214, 223, 232, 241), listOf(262, 272, 283, 294), listOf(319, 332, 345, 359), listOf(389, 405, 421, 438)),
            17 to listOf(listOf(206, 214, 223, 232), listOf(221, 230, 239, 249), listOf(227, 236, 245, 255), listOf(278, 289, 301, 313), listOf(339, 353, 367, 382), listOf(413, 430, 447, 465)),
            18 to listOf(listOf(219, 228, 237, 246), listOf(234, 243, 253, 263), listOf(240, 250, 260, 270), listOf(294, 306, 318, 331), listOf(359, 373, 388, 404), listOf(437, 454, 472, 491)),
            19 to listOf(listOf(231, 240, 250, 260), listOf(247, 257, 267, 278), listOf(254, 264, 275, 286), listOf(311, 323, 336, 349), listOf(379, 394, 410, 426), listOf(461, 479, 498, 518)),
            20 to listOf(listOf(243, 253, 263, 274), listOf(260, 270, 281, 292), listOf(267, 278, 289, 301), listOf(327, 340, 354, 368), listOf(399, 415, 432, 449), listOf(486, 505, 525, 546)),
        )
        var checked = 0
        table.forEach { (width, rows) ->
            rows.forEachIndexed { r, prices ->
                prices.forEachIndexed { t, expected ->
                    assertEquals("115×$width, ${types[t]}, от ${quantities[r]} шт", expected, podvPrice(width, types[t], quantities[r]).toInt())
                    // Граница диапазона: 499 шт — ещё тариф 100-499.
                    checked++
                }
            }
        }
        assertEquals(192, checked)
        assertEquals(182, podvPrice(14, "Полиэстер 1×1", 499).toInt())
        assertEquals(340, podvPrice(14, "Полиэстер 1×1", 9).toInt())
    }

    @Test fun lineTotalAndDescription() {
        val l = QuoteCalculator.line(QuoteLineInput(podv, emptyMap(), bd("600")))
        assertBd("158", l.unitPrice) // по умолчанию первые варианты: 115×13, полиэстер 1×1
        assertBd("94800", l.total)
        assertEquals("Подвяз 115×ширина (Размер: 115×13; Тип: Полиэстер 1×1)", l.description)
        assertBd("1", l.volumeFactor)
    }

    @Test fun rublesAddAndKopeckRounding() {
        val p = Product(
            id = 1, name = "Шнур", unit = "м", basePrice = bd("10.01"), rounding = bd("0.01"),
            options = listOf(OptionGroup(1, "Цвет", listOf(PriceChoice(2, "Pantone", bd("2.5"), listOf(Factor(bd("1.1"), BigDecimal.ONE)))))),
            tiers = listOf(PriceTier(bd("300"), Factor(bd("95"), bd("100")))),
        )
        // 10,01 × 0,95 = 9,5095 → 9,51; × 1,1 = 10,461 → 10,46; + 2,5 = 12,96
        val l = QuoteCalculator.line(QuoteLineInput(p, emptyMap(), bd("300")))
        assertBd("12.96", l.unitPrice)
        assertBd("3888", l.total)
        assertBd("0.95", l.volumeFactor)
    }

    @Test fun setupFeeAndMinimum() {
        val p = Product(id = 1, name = "A", unit = "шт", basePrice = bd("10"), minOrder = bd("50"), setupFee = bd("1500"))
        val l = QuoteCalculator.line(QuoteLineInput(p, emptyMap(), bd("20")))
        assertBd("1700", l.total)
        assertTrue(l.belowMinimum)
        assertFalse(QuoteCalculator.line(QuoteLineInput(p, emptyMap(), bd("50"))).belowMinimum)
    }

    @Test fun vatIncludedAndOnTop() {
        val p = Product(id = 1, name = "A", unit = "шт", basePrice = bd("1800"))
        val l = QuoteCalculator.line(QuoteLineInput(p, emptyMap(), bd("2")))
        val inc = QuoteCalculator.totals(listOf(l), VatSettings(bd("22"), included = true))
        assertBd("3600", inc.total)
        assertBd("649.18", inc.vat)
        assertBd("2950.82", inc.totalWithoutVat)
        val top = QuoteCalculator.totals(listOf(l), VatSettings(bd("22"), included = false))
        assertBd("792", top.vat)
        assertBd("4392", top.total)
    }

    @Test fun coefficientParsing() {
        assertEquals(listOf(Factor(bd("13"), bd("14"))), Coefficients.parse("13/14"))
        assertEquals(2, Coefficients.parse("1,04 * 1,04")!!.size)
        assertEquals(emptyList<Factor>(), Coefficients.parse(""))
        assertEquals(emptyList<Factor>(), Coefficients.parse("1"))
        assertNull(Coefficients.parse("abc"))
        assertNull(Coefficients.parse("1/0"))
        assertBd("1.1021", Coefficients.product(Coefficients.parse("1,07*1,03")!!).value)
    }

    @Test fun parserReportsBadRowsAndSkipsInactive() {
        val parsed = CatalogParser.parse(
            CatalogSheets(
                products = listOf(
                    listOf("h"),
                    listOf("A", "Изделие A", "м", "10"),
                    listOf("B", "Изделие B", "м", "abc"),
                    listOf("C", "Скрытое", "м", "5", "", "", "", "нет"),
                    listOf("", "", ""),
                ),
                parameters = listOf(listOf("h"), listOf("A", "Цвет", "Красный", "1,1", "2"), listOf("A", "Цвет", "", "1")),
                volume = listOf(listOf("h"), listOf("A", "100", "0,9"), listOf("A", "x", "1")),
                settings = listOf(listOf("Параметр", "Значение"), listOf("Ставка НДС, %", "22")),
            ),
        )
        assertEquals(listOf("A"), parsed.products.map { it.code })
        assertEquals(1, parsed.products[0].options.size)
        assertEquals(1, parsed.products[0].tiers.size)
        assertEquals("22", parsed.settings["Ставка НДС, %"])
        assertEquals(3, parsed.warnings.size)
        assertTrue(parsed.warnings[0].startsWith("Параметры, строка 3"))
    }

    @Test fun stableIdsAreDeterministic() {
        assertEquals(CatalogParser.stableId("PODV", "Тип"), CatalogParser.stableId(" podv ", "тип"))
        assertTrue(CatalogParser.stableId("a") != CatalogParser.stableId("b"))
        assertTrue(CatalogParser.stableId("x") >= 0)
    }

    @Test fun moneyFormat() {
        assertEquals("1 234 567,50", QuoteCalculator.formatMoney(bd("1234567.5")))
        assertEquals("999,00", QuoteCalculator.formatMoney(bd("999")))
        assertEquals("×1,07", QuoteCalculator.formatFactor(bd("1.07")))
    }
}
