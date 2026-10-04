package com.knit.calculator.core

import org.junit.Assert.assertEquals
import org.junit.Test
import java.math.BigDecimal

class NumberFormatterTest {
    private val nb = NumberFormatter.GROUP_SEPARATOR

    @Test fun stripsTrailingZeros() = assertEquals("2.5", NumberFormatter.toRaw(BigDecimal("2.5000")))
    @Test fun integerWithoutExponent() = assertEquals("100", NumberFormatter.toRaw(BigDecimal("1E2")))
    @Test fun zeroHasNoSign() = assertEquals("0", NumberFormatter.toRaw(BigDecimal("-0.000")))
    @Test fun negativeUsesMinusSign() = assertEquals("−3.5", NumberFormatter.toRaw(BigDecimal("-3.5")))
    @Test fun roundsTo15Digits() = assertEquals("1.23456789012346", NumberFormatter.toRaw(BigDecimal("1.234567890123456789")))

    @Test fun displayGroupsThousands() = assertEquals("1${nb}234${nb}567,89", NumberFormatter.toDisplay("1234567.89"))
    @Test fun displayKeepsShortNumbers() = assertEquals("1234", NumberFormatter.toDisplay("1234"))
    @Test fun displayExpression() = assertEquals("12${nb}345+(−6,5)", NumberFormatter.toDisplay("12345+(−6.5)"))
    @Test fun displayScientific() = assertEquals("1,5E-20", NumberFormatter.toDisplay("1.5E-20"))
    @Test fun clipboardFormat() = assertEquals("-1234,5", NumberFormatter.toClipboard("−1234.5"))
}
