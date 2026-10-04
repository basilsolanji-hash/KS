package com.knit.calculator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal

class QuoteCalculatorTest {
    private fun bd(s: String) = BigDecimal(s)

    private val width = OptionGroup(
        1, "Ширина",
        listOf(PriceChoice(11, "2 см", bd("0")), PriceChoice(12, "3 см", bd("4"))),
    )
    private val pattern = OptionGroup(
        2, "Рисунок",
        listOf(PriceChoice(21, "Гладкий", bd("0")), PriceChoice(22, "2 полосы", bd("5.50"))),
    )
    private val trim = Product(
        id = 1, name = "Подвязы", unit = "м", basePrice = bd("18"), minOrder = bd("50"), setupFee = bd("0"),
        options = listOf(width, pattern),
        tiers = listOf(DiscountTier(bd("300"), bd("5")), DiscountTier(bd("1000"), bd("10"))),
    )

    private fun assertBd(expected: String, actual: BigDecimal) =
        assertEquals("expected $expected but was $actual", 0, bd(expected).compareTo(actual))

    @Test fun defaultChoicesAreFirst() {
        val l = QuoteCalculator.line(QuoteLineInput(trim, emptyMap(), bd("100")))
        assertBd("18", l.unitPrice)
        assertBd("1800", l.total)
        assertEquals("Подвязы (Ширина: 2 см; Рисунок: Гладкий)", l.description)
    }

    @Test fun optionSurchargesAdd() {
        val l = QuoteCalculator.line(QuoteLineInput(trim, mapOf(1L to 12L, 2L to 22L), bd("100")))
        assertBd("27.5", l.listPrice)
        assertBd("2750", l.total)
    }

    @Test fun discountTiersPickHighestReached() {
        assertBd("0", QuoteCalculator.discountFor(trim, bd("299")))
        assertBd("5", QuoteCalculator.discountFor(trim, bd("300")))
        assertBd("10", QuoteCalculator.discountFor(trim, bd("5000")))
        val l = QuoteCalculator.line(QuoteLineInput(trim, mapOf(1L to 12L), bd("1000")))
        assertBd("19.80", l.unitPrice) // 22 × 0,9
        assertBd("19800", l.total)
    }

    @Test fun unitPriceRoundedToKopecks() {
        val l = QuoteCalculator.line(QuoteLineInput(trim.copy(basePrice = bd("10.01")), emptyMap(), bd("300")))
        assertBd("9.51", l.unitPrice) // 9,5095 → 9,51
        assertBd("2853", l.total)
    }

    @Test fun setupFeeAndMinimum() {
        val l = QuoteCalculator.line(QuoteLineInput(trim.copy(setupFee = bd("1500")), emptyMap(), bd("20")))
        assertBd("1860", l.total)
        assertTrue(l.belowMinimum)
        assertFalse(QuoteCalculator.line(QuoteLineInput(trim, emptyMap(), bd("50"))).belowMinimum)
    }

    @Test fun vatIncluded() {
        val l = QuoteCalculator.line(QuoteLineInput(trim, emptyMap(), bd("100"))) // 1800
        val t = QuoteCalculator.totals(listOf(l, l), VatSettings(bd("22"), included = true))
        assertBd("3600", t.total)
        assertBd("649.18", t.vat) // 3600 × 22 / 122
        assertBd("2950.82", t.totalWithoutVat)
    }

    @Test fun vatOnTop() {
        val l = QuoteCalculator.line(QuoteLineInput(trim, emptyMap(), bd("100")))
        val t = QuoteCalculator.totals(listOf(l), VatSettings(bd("22"), included = false))
        assertBd("396", t.vat)
        assertBd("2196", t.total)
        assertBd("1800", t.totalWithoutVat)
    }

    @Test fun moneyFormat() {
        assertEquals("1 234 567,50", QuoteCalculator.formatMoney(bd("1234567.5")))
        assertEquals("1 800,00", QuoteCalculator.formatMoney(bd("1800")))
        assertEquals("0,05", QuoteCalculator.formatMoney(bd("0.049")))
        assertEquals("999,00", QuoteCalculator.formatMoney(bd("999")))
    }
}
