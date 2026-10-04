package com.knit.calculator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal

class YarnCalculatorTest {

    private fun bd(s: String) = BigDecimal(s)

    private fun input(
        weight: String = "300",
        amount: String = "1000",
        unit: OrderUnit = OrderUnit.PIECES,
        waste: String = "0",
        extras: List<Pair<String, String>> = emptyList(),
    ) = YarnInput(
        itemWeightGrams = bd(weight),
        orderAmount = bd(amount),
        orderUnit = unit,
        wastePercent = bd(waste),
        mainName = "Основная",
        extras = extras.map { YarnComponent(it.first, bd(it.second)) },
    )

    private fun success(i: YarnInput) = (YarnCalculator.calculate(i) as YarnOutcome.Success).result

    private fun assertBd(expected: String, actual: BigDecimal) =
        assertEquals("expected $expected but was $actual", 0, bd(expected).compareTo(actual))

    @Test fun singleColorIsHundredPercentMain() {
        val r = success(input())
        assertEquals(1, r.lines.size)
        assertBd("100", r.lines[0].percent)
        assertBd("300", r.lines[0].gramsPerItem)
        assertBd("300", r.totalKg) // 300 г × 1000 шт = 300 кг
    }

    @Test fun wasteIsAddedOnTop() {
        val r = success(input(waste = "5"))
        assertBd("315", r.gramsPerItem)
        assertBd("315", r.totalKg)
        assertBd("300", r.totalKgNet)
        assertBd("15", r.wasteKg)
    }

    @Test fun multiColorSplitsByPercent() {
        val r = success(input(waste = "10", extras = listOf("Доп 1" to "30", "Спандекс" to "5")))
        assertEquals(listOf("Основная", "Доп 1", "Спандекс"), r.lines.map { it.name })
        assertBd("65", r.lines[0].percent)
        assertBd("195", r.lines[0].gramsPerItemNet)
        assertBd("214.5", r.lines[0].gramsPerItem)
        assertBd("214.5", r.lines[0].orderKg)
        assertBd("99", r.lines[1].gramsPerItem)
        assertBd("16.5", r.lines[2].gramsPerItem)
        assertBd("16.5", r.lines[2].orderKg)
        assertBd("330", r.totalKg)
        // Сумма по нитям совпадает с итогом.
        assertBd("330", r.lines.fold(BigDecimal.ZERO) { a, l -> a + l.orderKg })
    }

    @Test fun orderInKilograms() {
        val r = success(input(weight = "250", amount = "100", unit = OrderUnit.KILOGRAMS, waste = "4"))
        assertBd("400", r.pieces) // 100 кг / 0,25 кг
        assertBd("100", r.productKg)
        assertBd("104", r.totalKg)
    }

    @Test fun validation() {
        val bad = YarnCalculator.calculate(input(weight = "0", amount = "0", waste = "150", extras = listOf("A" to "60", "B" to "50")))
        val issues = (bad as YarnOutcome.Invalid).issues
        assertEquals(
            setOf(YarnIssue.WEIGHT_REQUIRED, YarnIssue.ORDER_REQUIRED, YarnIssue.WASTE_OUT_OF_RANGE, YarnIssue.PERCENT_SUM_EXCEEDED),
            issues,
        )
        val zeroExtra = YarnCalculator.calculate(input(extras = listOf("A" to "0")))
        assertTrue(YarnIssue.EXTRA_PERCENT_INVALID in (zeroExtra as YarnOutcome.Invalid).issues)
    }

    @Test fun parseDecimal() {
        assertBd("12.5", YarnCalculator.parseDecimal(" 12,5 ")!!)
        assertBd("1500", YarnCalculator.parseDecimal("1 500")!!)
        assertNull(YarnCalculator.parseDecimal(""))
        assertNull(YarnCalculator.parseDecimal("abc"))
        assertNull(YarnCalculator.parseDecimal("1,2,3"))
    }

    @Test fun formatting() {
        assertEquals("214,500", YarnCalculator.format(bd("214.5"), 3))
        assertEquals("12\u202F345,7", YarnCalculator.format(bd("12345.67"), 1))
        assertEquals("33,33", YarnCalculator.formatCompact(bd("33.3333"), 2))
        assertEquals("400", YarnCalculator.formatCompact(bd("400.000"), 2))
    }
}
