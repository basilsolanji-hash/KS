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
import org.junit.Rule
import org.junit.Test
import org.junit.rules.RuleChain
import org.junit.runner.RunWith

/** Версия 1.6.0: проверка ИНН, «Отменить» удаление позиции, «О версии». */
@RunWith(AndroidJUnit4::class)
class V16UiTest {
    private val compose = createAndroidComposeRule<MainActivity>()

    @get:Rule
    val rules: RuleChain = RuleChain.outerRule(ResetAppRule()).around(compose)

    @Test fun innCheckAndUndoRemove() {
        compose.onNodeWithText("Коммерческое предложение").performClick()
        val inn = compose.onNode(hasSetTextAction() and hasText("ИНН клиента"))
        inn.performTextInput("7701234567")
        compose.onNodeWithText("Неверный ИНН").assertExists()
        inn.performTextClearance()
        inn.performTextInput("9705239429")
        compose.onNodeWithText("Неверный ИНН").assertDoesNotExist()
        // ИНН организации — появляются КПП и юридический адрес для счёта и договора.
        compose.onAllNodes(hasText("КПП")).onFirst().performScrollTo().assertExists()
        Shots.take("60_inn_check", compose)

        compose.onNodeWithText("Добавить позицию").performScrollTo().performClick()
        compose.waitUntil(5_000) { compose.onAllNodes(hasText("Подвяз трикотажный")).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText("Подвяз трикотажный").performClick()
        compose.onNodeWithContentDescription("Удалить позицию").performClick()
        compose.onNodeWithText("Позиция удалена").assertExists()
        Shots.take("61_undo", compose)
        compose.onNodeWithText("Отменить").performClick()
        compose.onAllNodes(hasText("Подвяз трикотажный", substring = true)).onFirst().assertExists()
        compose.onNodeWithText("Позиция удалена").assertDoesNotExist()
    }

    @Test fun aboutScreen() {
        compose.onNodeWithText("О версии", substring = true).performScrollTo().performClick()
        compose.onNodeWithText("Что нового").assertExists()
        compose.onNodeWithText("Google Таблица").assertExists()
        Shots.take("62_about", compose)
    }

    @Test fun settingsTabsAndQuoteSteps() {
        compose.onNodeWithText("Коммерческое предложение").performClick()
        compose.onNodeWithText("3 · Итог").assertExists()
        compose.onNodeWithText("Добавить позицию").performScrollTo().performClick()
        compose.waitUntil(5_000) { compose.onAllNodes(hasText("Подвяз трикотажный")).fetchSemanticsNodes().isNotEmpty() }
        compose.onNodeWithText("Подвяз трикотажный").performClick()
        // Итог и «Сохранить» — внизу экрана, без прокрутки.
        compose.onNodeWithText("Итого").assertExists()
        compose.onNodeWithText("Сохранить").assertExists()
        compose.onNodeWithText("1 · Клиент").performClick()
        Shots.take("63_quote_steps", compose)

        compose.onNodeWithContentDescription("Реквизиты фабрики").performClick()
        compose.onNodeWithText("Подключить по QR-коду").assertExists()
        compose.onNodeWithText("Ежедневная сводка в 9:00").assertExists()
        compose.onNodeWithText("Реквизиты").performClick()
        compose.onNodeWithText("Подключить по QR-коду").assertDoesNotExist()
        Shots.take("64_settings_tabs", compose)
    }
}
