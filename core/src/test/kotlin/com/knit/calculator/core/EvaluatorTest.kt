package com.knit.calculator.core

import org.junit.Assert.assertEquals
import org.junit.Test

class EvaluatorTest {

    private fun eval(expr: String): String = when (val r = Evaluator.evaluate(expr)) {
        is EvalResult.Success -> NumberFormatter.toRaw(r.value)
        is EvalResult.Failure -> r.error.name
    }

    @Test fun addition() = assertEquals("5", eval("2+3"))
    @Test fun division() = assertEquals("5", eval("10÷2"))
    @Test fun asciiOperators() = assertEquals("7", eval("10-6/2"))
    @Test fun precedence() = assertEquals("14", eval("2+3×4"))
    @Test fun parentheses() = assertEquals("20", eval("(2+3)×4"))
    @Test fun nestedParentheses() = assertEquals("−9", eval("−(1+(2×4))"))
    @Test fun autoCloseParentheses() = assertEquals("20", eval("(2+3)×(1+3"))
    @Test fun decimalsAreExact() = assertEquals("0.3", eval("0.1+0.2"))
    @Test fun decimalMultiplication() = assertEquals("3.75", eval("1.5×2.5"))
    @Test fun repeatingFraction() = assertEquals("0.333333333333333", eval("1÷3"))
    @Test fun roundingHalfUp() = assertEquals("0.666666666666667", eval("2÷3"))
    @Test fun unaryMinus() = assertEquals("−2", eval("−5+3"))
    @Test fun unaryMinusAfterTimes() = assertEquals("−15", eval("5×−3"))
    @Test fun negatedParenthesisedNumber() = assertEquals("8", eval("5−(−3)"))

    @Test fun standalonePercent() = assertEquals("0.5", eval("50%"))
    @Test fun percentOfAddition() = assertEquals("220", eval("200+10%"))
    @Test fun percentOfSubtraction() = assertEquals("180", eval("200−10%"))
    @Test fun percentOfMultiplication() = assertEquals("20", eval("200×10%"))
    @Test fun percentOfDivision() = assertEquals("2000", eval("200÷10%"))

    @Test fun divisionByZero() = assertEquals("DIVISION_BY_ZERO", eval("5÷0"))
    @Test fun divisionByZeroExpression() = assertEquals("DIVISION_BY_ZERO", eval("5÷(2−2)"))
    @Test fun zeroDividedIsZero() = assertEquals("0", eval("0÷5"))
    @Test fun emptyIsInvalid() = assertEquals("INVALID_EXPRESSION", eval(""))
    @Test fun trailingOperatorInvalid() = assertEquals("INVALID_EXPRESSION", eval("2+"))
    @Test fun doubleDotInvalid() = assertEquals("INVALID_EXPRESSION", eval("1.2.3"))
    @Test fun emptyParensInvalid() = assertEquals("INVALID_EXPRESSION", eval("()"))
    @Test fun extraCloseParenInvalid() = assertEquals("INVALID_EXPRESSION", eval("2)"))
    @Test fun lettersInvalid() = assertEquals("INVALID_EXPRESSION", eval("2+abc"))

    @Test fun largeNumbersUseScientific() = assertEquals("1E20", eval("10000000000×10000000000"))
    @Test fun smallNumbersUseScientific() = assertEquals("1E-12", eval("1÷1000000000000"))
    @Test fun scientificInputParses() = assertEquals("3E20", eval("1.5E20×2"))
    @Test fun negativeExponentInputParses() = assertEquals("2.5E-10", eval("5E-10÷2"))
    @Test fun overflow() = assertEquals("OVERFLOW", eval("1E999×1E999"))
    @Test fun longExpression() = assertEquals("55", eval((1..10).joinToString("+")))
}
