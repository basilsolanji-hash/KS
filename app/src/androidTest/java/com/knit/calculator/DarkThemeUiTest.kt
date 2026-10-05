package com.knit.calculator

import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.rules.RuleChain
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class DarkThemeUiTest {
    private val compose = createAndroidComposeRule<MainActivity>()

    @get:Rule
    val rules: RuleChain = RuleChain.outerRule(ResetAppRule(theme = "DARK")).around(compose)

    @Test fun darkScreens() {
        Shots.take("09_home_dark", compose)
        compose.onNodeWithContentDescription("Калькулятор").performClick()
        listOf("1", "2", "×", "(", "3", "+", "4").forEach { compose.onNodeWithTag("key_${if (it == "(") "( )" else it}").performClick() }
        Shots.take("10_calculator_dark_preview", compose)
        compose.onNodeWithTag("key_=").performClick()
        compose.onNodeWithContentDescription("Результат: 84").assertExists()
        Shots.take("11_calculator_dark_result", compose)
        compose.onNodeWithContentDescription("Назад").performClick()
        compose.onNodeWithText("Расход пряжи").performScrollTo().performClick()
        Shots.take("12_yarn_dark", compose)
    }
}
