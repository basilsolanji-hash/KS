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
import com.knit.calculator.core.Invoice
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.quote.CompanySettings
import com.knit.calculator.quote.DealDoc
import com.knit.calculator.quote.DefaultCatalog
import com.knit.calculator.quote.DocPdf
import com.knit.calculator.quote.SnapshotLine
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.RuleChain
import org.junit.runner.RunWith
import java.math.BigDecimal

/** Версия 1.3: оплаты и долги, производство, склад пряжи, счёт, договор и прайс-лист. */
@RunWith(AndroidJUnit4::class)
class OpsUiTest {
    private val compose = createAndroidComposeRule<MainActivity>()

    @get:Rule
    val rules: RuleChain = RuleChain.outerRule(ResetAppRule()).around(compose)

    private fun money(v: Int) = QuoteCalculator.formatMoney(BigDecimal(v)) + " ₽"

    private fun waitText(text: String, substring: Boolean = false) =
        compose.waitUntil(10_000) { compose.onAllNodes(hasText(text, substring = substring)).fetchSemanticsNodes().isNotEmpty() }

    private val deal = DealDoc(
        quoteId = "q1", quoteNumber = 12, quoteDate = "05.10.2026", client = "ООО «Пример»", clientInn = "7701234567",
        clientEmail = "client@example.ru",
        lines = listOf(
            SnapshotLine("Подвяз трикотажный, 115×14, хлопок 2×2", BigDecimal(600), "шт", BigDecimal(178), BigDecimal(106_800)),
            SnapshotLine("Воротник поло", BigDecimal(150), "шт", BigDecimal(120), BigDecimal(18_000)),
        ),
        total = BigDecimal(124_800),
        vat = DocPdf.vatIn(BigDecimal(124_800), CompanySettings()),
    )

    @Test fun documentsRender() {
        val s = CompanySettings()
        val invoice = DocPdf.invoice(targetContext, s, Invoice(7, "q1", 12, deal.client, System.currentTimeMillis(), BigDecimal(62_400), "Предоплата 50 % по КП № 12"), deal)
        assertTrue(Shots.renderPdf(invoice, "40_invoice") >= 1)
        val full = DocPdf.invoice(targetContext, s, Invoice(8, "q1", 12, deal.client, System.currentTimeMillis(), deal.total, "Оплата по КП № 12"), deal)
        assertTrue(Shots.renderPdf(full, "41_invoice_full") >= 1)
        val contract = DocPdf.contract(targetContext, s, deal, com.knit.calculator.core.ContractTemplate.DEFAULT, "50")
        assertTrue(Shots.renderPdf(contract, "42_contract") >= 2)
        val price = DocPdf.priceList(targetContext, s, DefaultCatalog.products)
        assertTrue(Shots.renderPdf(price, "43_price_list") >= 1)
    }

    @Test fun paymentProductionAndStock() {
        // КП на 600 подвязов: 94 800 ₽.
        compose.onNodeWithContentDescription("Новый заказ — КП").performClick()
        compose.onNodeWithText("Добавить позицию").performScrollTo().performClick()
        // Пункт меню появляется во всплывающем окне — ждём его.
        compose.waitUntil(15_000) { compose.onAllNodes(hasText("Подвяз трикотажный")).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText("Подвяз трикотажный").performClick()
        // Клиента — после позиции: подсказка клиентов перекрывает кнопку «Добавить позицию».
        compose.onNodeWithText("Компания клиента").performScrollTo().performTextInput("ООО «Пример»")
        val qty = compose.onNode(hasSetTextAction() and hasText("Количество"))
        qty.performTextClearance()
        qty.performTextInput("600")
        compose.onNodeWithText("Сохранить КП").performScrollTo().performClick()
        Thread.sleep(3_000)
        compose.waitForIdle()

        // История → «⋮» → «Внести оплату» 50 000 ₽.
        compose.onNodeWithContentDescription("Заказы и КП").performClick()
        waitText("Статус: Отправлено", substring = true)
        compose.onNodeWithContentDescription("Документы и учёт").performClick()
        compose.onNodeWithText("Внести оплату").performClick()
        val amount = compose.onNode(hasSetTextAction() and hasText("Сумма оплаты"))
        amount.performTextClearance()
        amount.performTextInput("50000")
        compose.onNodeWithText("Сохранить").performClick()
        compose.waitForIdle()

        // «⋮» → «В производство» — сразу открывается экран производства.
        compose.onNodeWithContentDescription("Документы и учёт").performClick()
        compose.onNodeWithText("В производство").performClick()
        waitText("Заказ по КП № 1", substring = true)
        compose.onNodeWithText("Этап: Новый", substring = true).performClick()
        compose.onNodeWithText("В вязке").performClick()
        waitText("Этап: В вязке", substring = true)
        Shots.take("44_production", compose)

        // Назад до главного → «Оплаты и долги»: долг 44 800 ₽.
        repeat(3) { compose.onNodeWithContentDescription("Назад").performClick() }
        // Главный экран директора: приход денег (оплата 50 000 ₽); продажи — только из МойСклад (здесь его нет).
        compose.waitUntil(10_000) { compose.onAllNodes(hasText("Приход и расход денег")).fetchSemanticsNodes().isNotEmpty() }
        Shots.take("47_home_top", compose)
        compose.onNodeWithText("Приход и расход денег").performScrollTo()
        Shots.take("48_home_charts", compose)
        // Периоды динамики: «По месяцам» — тот же месяц год назад пунктиром.
        compose.onNodeWithText("По месяцам").performClick()
        compose.onNodeWithText("Приход и расход денег").performScrollTo()
        Shots.take("49_home_months", compose)
        compose.openMenu("Оплаты и долги")
        waitText(money(44_800))
        compose.onNodeWithText("Подробнее").performClick()
        compose.onAllNodes(hasText(money(50_000), substring = true)).onFirst().assertExists()
        Shots.take("45_payments", compose)

        // Склад: приход 25 кг полиэстера.
        compose.onNodeWithContentDescription("Назад").performClick()
        compose.openMenu("Склад пряжи")
        compose.onNodeWithText("Приход").performClick()
        compose.onNode(hasSetTextAction() and hasText("Пряжа")).performTextInput("Полиэстер")
        compose.onNode(hasSetTextAction() and hasText("Количество")).performTextInput("25")
        compose.onNodeWithText("Сохранить").performClick()
        waitText("25 кг")
        Shots.take("46_stock", compose)

        // Прайс-лист и QR-код в ассортименте.
        compose.onNodeWithContentDescription("Назад").performClick()
        compose.onNodeWithContentDescription("Новый заказ — КП").performClick()
        compose.onNodeWithContentDescription("Ассортимент и цены").performClick()
        compose.onNodeWithText("QR-код").performClick()
        compose.onNodeWithText("https://fabrika-ks.ru/shop").assertExists()
        Shots.take("47_qr", compose)
    }
}
