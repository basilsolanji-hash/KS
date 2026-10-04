package com.knit.calculator

import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.rules.RuleChain
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class CalculatorUiTest {
    private val compose = createAndroidComposeRule<MainActivity>()

    @get:Rule
    val rules: RuleChain = RuleChain.outerRule(ResetAppRule()).around(compose)

    private fun press(keys: String) = keys.forEach { k ->
        val label = when (k) {
            '/' -> "÷"
            '*' -> "×"
            '-' -> "−"
            '.' -> ","
            'C' -> "AC"
            's' -> "±"
            'p' -> "( )"
            else -> k.toString()
        }
        compose.onNodeWithTag("key_$label").performClick()
    }

    private fun assertResult(text: String) = compose.onNodeWithContentDescription("Результат: $text").assertExists()
    private fun assertExpression(text: String) = compose.onNodeWithContentDescription("Выражение: $text").assertExists()

    @Test fun twoPlusThree() {
        press("2+3=")
        assertResult("5")
    }

    @Test fun tenDividedByTwo() {
        press("10/2=")
        assertResult("5")
    }

    @Test fun decimalsPercentSign() {
        press("1.5+2.5=")
        assertResult("4")
        press("C200+10%=")
        assertResult("220")
        press("C5s=")
        assertResult("−5")
    }

    @Test fun backspaceAndClear() {
        press("123")
        compose.onNodeWithContentDescription("Удалить последний символ").performClick()
        assertExpression("12")
        press("+3C")
        assertExpression("0")
    }

    @Test fun divisionByZero() {
        press("5/0=")
        compose.onNodeWithText("Деление на ноль невозможно").assertExists()
        Shots.take("02_division_by_zero")
    }

    @Test fun historyRestoreAndCopy() {
        press("1234.5*2=")
        assertResult("2469")
        compose.onNodeWithText("Скопировать результат").performClick()
        Shots.take("01_calculator_light")
        press("C2+3=")
        compose.onNodeWithContentDescription("Открыть историю вычислений").performClick()
        compose.waitUntil(5_000) { compose.onAllNodes(hasText("= 5")).fetchSemanticsNodes().isNotEmpty() }
        Shots.take("03_history")
        compose.onNodeWithText("= 2469").performClick()
        assertResult("2469")
    }

    @Test fun stateSurvivesRecreation() {
        press("7*6=")
        compose.activityRule.scenario.recreate()
        assertResult("42")
    }
}
