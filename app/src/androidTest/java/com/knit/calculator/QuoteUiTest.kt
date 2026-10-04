package com.knit.calculator

import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextClearance
import androidx.compose.ui.test.performTextInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.QuoteLineInput
import com.knit.calculator.quote.CompanySettings
import com.knit.calculator.quote.DefaultCatalog
import com.knit.calculator.quote.QuoteDocument
import com.knit.calculator.quote.QuoteDraft
import com.knit.calculator.quote.QuotePdf
import com.knit.calculator.quote.vat
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.RuleChain
import org.junit.runner.RunWith
import java.math.BigDecimal

@RunWith(AndroidJUnit4::class)
class QuoteUiTest {
    private val compose = createAndroidComposeRule<MainActivity>()

    @get:Rule
    val rules: RuleChain = RuleChain.outerRule(ResetAppRule()).around(compose)

    private fun money(v: Int) = QuoteCalculator.formatMoney(BigDecimal(v)) + " ₽"

    private fun assertMoney(v: Int) =
        compose.onAllNodes(hasText(money(v))).onFirst().performScrollTo().assertExists()

    @Test fun podvyazPricesLikeFactoryPriceList() {
        Shots.take("00_home", compose)
        compose.onNodeWithText("Коммерческое предложение").performClick()
        compose.onNodeWithText("Компания клиента").performTextInput("ООО «Пример»")
        compose.onNodeWithText("Добавить позицию").performScrollTo().performClick()
        compose.onNodeWithText("Подвяз трикотажный").performClick()
        val qty = compose.onNode(hasSetTextAction() and hasText("Количество"))
        qty.performTextClearance()
        qty.performTextInput("600")
        assertMoney(94_800) // 115×13, полиэстер 1×1, 500+: 158 ₽

        compose.onNodeWithText("115×13", substring = true).performScrollTo().performClick()
        compose.onNodeWithText("115×14").performClick()
        assertMoney(102_000) // 170 ₽

        compose.onNodeWithText("Полиэстер 1×1", substring = true).performScrollTo().performClick()
        compose.onNodeWithText("Хлопок 2×2", substring = true).performClick()
        assertMoney(114_600) // 191 ₽
        Shots.take("30_quote_line", compose)

        qty.performTextClearance()
        qty.performTextInput("5")
        assertMoney(1_915) // 1-9 шт: 383 ₽
        compose.onNodeWithText("Итого к оплате").performScrollTo()
        Shots.take("31_quote_totals", compose)

        // Скидка менеджера: больше 7 % нельзя — применяется 7 %.
        qty.performTextClearance()
        qty.performTextInput("600")
        val discount = compose.onNode(hasSetTextAction() and hasText("Скидка, %"))
        discount.performScrollTo().performTextInput("10")
        compose.onNodeWithText("Скидка не больше 7 % — применено 7 %").assertExists()
        assertMoney(106_800) // 191 ₽ × 0,93 = 177,63 → 178 ₽ (округление до рубля) × 600

        // Себестоимость и прибыль — только для менеджера, по переключателю.
        compose.onNodeWithText("Себестоимость и прибыль (только для менеджера)").performScrollTo().performClick()
        compose.onAllNodes(hasText("Себестоимость заказа", substring = true)).onFirst().performScrollTo().assertExists()
        Shots.take("35_quote_economics", compose)

        // Сохранить КП (без таблицы — в архив телефона), затем история → статус → отчёт.
        compose.onNodeWithText("Сохранить КП").performScrollTo().performClick()
        Thread.sleep(3_000) // сохранение и PDF — в фоне
        compose.waitForIdle()
        compose.onNodeWithContentDescription("История КП").performClick()
        compose.waitUntil(10_000) { compose.onAllNodes(hasText("Статус: Отправлено", substring = true)).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText("Статус: Отправлено", substring = true).performClick()
        compose.onNodeWithText("Оплачено").performClick()
        compose.waitUntil(10_000) { compose.onAllNodes(hasText("Статус: Оплачено", substring = true)).fetchSemanticsNodes().isNotEmpty() }
        Shots.take("38_history_status", compose)
        compose.onNodeWithContentDescription("Назад").performClick()

        compose.onNodeWithContentDescription("Ассортимент и цены").performClick()
        Shots.take("32_catalog", compose)
        compose.onNodeWithContentDescription("Назад").performClick()
        compose.onNodeWithContentDescription("Реквизиты фабрики").performClick()
        Shots.take("33_company_settings", compose)
        compose.onNodeWithContentDescription("Назад").performClick()
        compose.onNodeWithContentDescription("Назад").performClick()

        // Главный экран → отчёт и каталог на сайте.
        compose.onNodeWithText("Отчёт").performScrollTo().performClick()
        compose.onNodeWithText("Отчёт за месяц").assertExists()
        compose.waitUntil(10_000) { compose.onAllNodes(hasText(money(106_800))).fetchSemanticsNodes().isNotEmpty() }
        Shots.take("36_report", compose)
        compose.onNodeWithContentDescription("Назад").performClick()
        compose.onNodeWithText("Каталог на сайте").performScrollTo().performClick()
        compose.mainClock.advanceTimeBy(5_000)
        Thread.sleep(4_000) // загрузка сайта (без проверки: сеть эмулятора может быть недоступна)
        Shots.take("37_shop", compose)
    }

    @Test fun quotePdfRenders() {
        val catalog = DefaultCatalog.products
        val podv = catalog.first { it.code == "PODV" }
        val polo = catalog.first { it.code == "POLO" }
        val lines = listOf(
            QuoteCalculator.line(QuoteLineInput(podv, emptyMap(), BigDecimal(600))),
            QuoteCalculator.line(QuoteLineInput(polo, mapOf(polo.options[2].id to polo.options[2].choices[2].id), BigDecimal(150))),
        )
        val settings = CompanySettings()
        val doc = QuoteDocument(
            settings = settings,
            draft = QuoteDraft(number = 12, clientCompany = "ООО «Пример»", clientContact = "Иван Петров", comment = "Цвета по образцу клиента."),
            totals = QuoteCalculator.totals(lines, settings.vat()),
        )
        val file = QuotePdf.create(targetContext, doc)
        assertTrue(file.length() > 1000)
        assertTrue(Shots.renderPdf(file, "34_quote_pdf") >= 1)
    }
}
