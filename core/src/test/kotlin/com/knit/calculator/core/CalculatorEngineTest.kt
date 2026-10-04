package com.knit.calculator.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class CalculatorEngineTest {

    /** Нажимает клавиши по очереди. `=` вычисляет, `C` очищает, `<` стирает, `±` меняет знак, `()` — умная скобка. */
    private fun press(keys: String, start: CalculatorState = CalculatorState()): CalculatorState {
        var s = start
        keys.forEach { k ->
            s = when (k) {
                in '0'..'9' -> CalculatorEngine.digit(s, k)
                '.' -> CalculatorEngine.dot(s)
                '+', '−', '×', '÷' -> CalculatorEngine.operator(s, k)
                '%' -> CalculatorEngine.percent(s)
                '(' -> CalculatorEngine.openParen(s)
                ')' -> CalculatorEngine.closeParen(s)
                'p' -> CalculatorEngine.parentheses(s)
                '±' -> CalculatorEngine.toggleSign(s)
                '<' -> CalculatorEngine.backspace(s)
                'C' -> CalculatorEngine.clear()
                '=' -> CalculatorEngine.evaluate(s, now = 1L).state
                else -> error("unknown key $k")
            }
        }
        return s
    }

    @Test fun twoPlusThree() {
        val s = press("2+3=")
        assertEquals("5", s.expression)
        assertTrue(s.evaluated)
        assertEquals("2+3", s.previousExpression)
    }

    @Test fun tenDividedByTwo() = assertEquals("5", press("10÷2=").expression)

    @Test fun previewWhileTyping() {
        val s = press("2+3×4")
        assertEquals("14", s.preview)
        assertFalse(s.evaluated)
    }

    @Test fun previewIgnoresTrailingOperator() = assertEquals("5", press("2+3×").preview)

    @Test fun noPreviewForPlainNumber() = assertNull(press("123").preview)

    @Test fun decimals() = assertEquals("4", press("1.5+2.5=").expression)

    @Test fun dotStartsWithZero() = assertEquals("0.", press(".").expression)

    @Test fun secondDotIgnored() = assertEquals("1.25", press("1.2.5").expression)

    @Test fun leadingZerosCollapsed() = assertEquals("7", press("0007").expression)

    @Test fun percentOfSum() = assertEquals("220", press("200+10%=").expression)

    @Test fun percentAlone() = assertEquals("0.5", press("50%=").expression)

    @Test fun percentRequiresNumber() = assertEquals("", press("%").expression)

    @Test fun toggleSignOfNumber() {
        assertEquals("(−5", press("5±").expression)
        assertEquals("−5", press("5±=").expression)
        assertEquals("5", press("5±±").expression)
    }

    @Test fun toggleSignOfSecondOperand() {
        val s = press("5−3±")
        assertEquals("5−(−3", s.expression)
        assertEquals("8", press("=", s).expression)
    }

    @Test fun toggleSignOfNegativeResult() = assertEquals("5", press("2−7=±").expression)

    @Test fun toggleSignOnEmptyAndBack() {
        assertEquals("(−", press("±").expression)
        assertEquals("(", press("±±").expression)
    }

    @Test fun backspaceRemovesLastSymbol() = assertEquals("12+", press("12+3<").expression)

    @Test fun backspaceUpdatesPreview() = assertEquals("15", press("12+34<").preview)

    @Test fun backspaceOnEmptyIsSafe() = assertEquals("", press("<<<").expression)

    @Test fun clearResetsEverything() = assertEquals(CalculatorState(), press("12+3=C"))

    @Test fun divisionByZeroShowsError() {
        val s = press("5÷0=")
        assertEquals(CalcError.DIVISION_BY_ZERO, s.error)
        assertEquals("5÷0", s.expression)
        assertFalse(s.evaluated)
        // После ошибки можно продолжить ввод.
        val fixed = press("<2=", s)
        assertEquals("2.5", fixed.expression)
        assertNull(fixed.error)
    }

    @Test fun repeatedOperatorReplaces() = assertEquals("5×", press("5+++×").expression)

    @Test fun operatorReplacement() {
        assertEquals("5−", press("5+−").expression)
        assertEquals("5×−", press("5×−").expression)
        assertEquals("5+", press("5×−+").expression)
        assertEquals("−15", press("5×−3=").expression)
    }

    @Test fun operatorOnEmptyIgnoredExceptMinus() {
        assertEquals("", press("×÷+").expression)
        assertEquals("−", press("−").expression)
        assertEquals("−", press("−+×").expression)
        assertEquals("−2", press("−2=").expression)
    }

    @Test fun equalsOnEmptyOrIncompleteIsSafe() {
        assertEquals(CalculatorState(), press("==="))
        assertEquals("2", press("2+=").expression)
    }

    @Test fun repeatedEqualsKeepsResult() = assertEquals("5", press("2+3===").expression)

    @Test fun continueFromResultWithOperator() = assertEquals("10", press("2+3=×2=").expression)

    @Test fun digitAfterResultStartsNew() {
        val s = press("2+3=7")
        assertEquals("7", s.expression)
        assertNull(s.previousExpression)
    }

    @Test fun smartParentheses() {
        assertEquals("(2+3)×4", press("p2+3p×4").expression)
        assertEquals("20", press("p2+3p×4=").expression)
    }

    @Test fun implicitMultiplicationWithParentheses() {
        assertEquals("2×(3", press("2(3").expression)
        assertEquals("(2)×3", press("(2)3").expression)
        assertEquals("6", press("2(3=").expression)
    }

    @Test fun closeParenWithoutOpenIgnored() = assertEquals("2", press("2)").expression)

    @Test fun unclosedParenthesesClosedInHistory() {
        val outcome = CalculatorEngine.evaluate(press("(2+3"), now = 42L)
        assertEquals(HistoryEntry("(2+3)", "5", 42L), outcome.historyEntry)
    }

    @Test fun historyEntryCreatedOnlyForRealCalculation() {
        assertEquals(HistoryEntry("2+3", "5", 7L), CalculatorEngine.evaluate(press("2+3"), now = 7L).historyEntry)
        assertNull(CalculatorEngine.evaluate(press("23"), now = 7L).historyEntry)
        assertNull(CalculatorEngine.evaluate(press("5÷0"), now = 7L).historyEntry)
    }

    @Test fun restoreFromHistory() {
        val s = CalculatorEngine.restore(HistoryEntry("2+3", "5", 1L))
        assertEquals("5", s.expression)
        assertTrue(s.evaluated)
        assertEquals("15", press("×3=", s).expression)
    }

    @Test fun digitLimitPerNumber() =
        assertEquals(CalculatorEngine.MAX_DIGITS_PER_NUMBER, press("12345678901234567890").expression.length)

    @Test fun maxLengthRespected() {
        val s = press("1+".repeat(100))
        assertTrue(s.expression.length <= CalculatorEngine.MAX_LENGTH)
    }

    @Test fun scientificResultBackspaceClears() {
        val s = press("9999999999×9999999999=")
        assertTrue('E' in s.expression)
        assertEquals("", press("<", s).expression)
    }

    @Test fun scientificResultCanContinue() {
        val s = press("10000000000×10000000000=÷2=")
        assertEquals("5E19", s.expression)
    }

    @Test fun trailingNumberDetection() {
        assertEquals("1.5E-20", CalculatorEngine.trailingNumber("3+1.5E-20"))
        assertNull(CalculatorEngine.trailingNumber("3+"))
    }
}
