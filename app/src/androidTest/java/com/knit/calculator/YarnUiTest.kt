package com.knit.calculator

import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextClearance
import androidx.compose.ui.test.performTextInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.knit.calculator.core.OrderUnit
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.core.YarnOutcome
import com.knit.calculator.yarn.ExtraYarnForm
import com.knit.calculator.yarn.YarnForm
import com.knit.calculator.yarn.YarnReport
import com.knit.calculator.yarn.YarnReportData
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.RuleChain
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class YarnUiTest {
    private val compose = createAndroidComposeRule<MainActivity>()

    @get:Rule
    val rules: RuleChain = RuleChain.outerRule(ResetAppRule()).around(compose)

    private fun field(label: String) = compose.onNode(hasSetTextAction() and hasText(label))

    @Test fun multiColorWithSpandexAndWaste() {
        compose.onNodeWithText("Расход пряжи").performScrollTo().performClick()
        field("Вес одного изделия, г").performTextInput("300")
        field("Количество, шт").performTextInput("1000")
        field("Брак и отходы, %").performTextClearance()
        field("Брак и отходы, %").performTextInput("10")
        compose.onNodeWithText("Доп. нить").performScrollTo().performClick()
        field("Доля, %").performTextInput("30")
        compose.onNodeWithText("Спандекс").performScrollTo().performClick()
        compose.onNodeWithText("330,000 кг").performScrollTo().assertExists()
        Shots.take("20_yarn_result", compose)
    }

    @Test fun yarnPdfRenders() {
        val form = YarnForm(
            productName = "Свитер", orderNumber = "17", itemWeight = "300", orderAmount = "1000",
            orderUnit = OrderUnit.PIECES, waste = "10",
            extras = listOf(ExtraYarnForm(1, "Доп 1", "30"), ExtraYarnForm(2, "Спандекс (резинка)", "5")),
        )
        val result = (YarnCalculator.calculate(form.toInput("Основная")!!) as YarnOutcome.Success).result
        val file = YarnReport.createPdf(targetContext, YarnReportData(form, result))
        assertTrue(file.length() > 1000)
        assertTrue(Shots.renderPdf(file, "21_yarn_pdf") >= 1)
    }
}
