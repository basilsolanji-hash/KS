package com.knit.calculator

import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextClearance
import androidx.compose.ui.test.performTextInput
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.quote.QuoteStore
import com.knit.calculator.quote.SyncConfig
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Rule
import org.junit.Test
import org.junit.rules.RuleChain
import org.junit.rules.TestWatcher
import org.junit.runner.Description
import org.junit.runner.RunWith
import java.math.BigDecimal

/** Товары МойСклад в КП: поиск, цена по тиражу, остаток склада (копия каталога — без сети). */
@RunWith(AndroidJUnit4::class)
class MoySkladUiTest {
    private val compose = createAndroidComposeRule<MainActivity>()

    /** Как будто таблица подключена и товары МойСклад уже загружены. */
    private val seed = object : TestWatcher() {
        override fun starting(description: Description) {
            val store = QuoteStore(targetContext)
            store.saveSyncConfig(SyncConfig("https://script.google.com/macros/s/test/exec", "k", "Тест"))
            fun product(id: String, name: String, article: String, group: String, stock: Int, prices: List<Pair<Int, String>>) =
                JSONObject().put("id", id).put("name", name).put("article", article).put("group", group)
                    .put("weight", 84.9).put("buyPrice", "143.95").put("minPrice", "143.95").put("stock", stock)
                    .put("tiers", JSONArray().apply { prices.forEach { (q, p) -> put(JSONObject().put("from", q).put("price", p)) } })
            val tiers = listOf(1 to "201.53", 10 to "194.33", 20 to "187.13", 50 to "172.74", 100 to "165.54", 500 to "158.34")
            store.saveMsCatalog(
                JSONObject().put("enabled", true).put("store", "Электросталь").put("loadedAt", System.currentTimeMillis())
                    .put(
                        "products",
                        JSONArray()
                            .put(product("p1", "Подвяз трикотажный 1×1 (ПЭ) Белый, 14х100 см", "11-001 0067", "Подвязы/Полиэстер", 120, tiers))
                            .put(product("p2", "Подвяз трикотажный 2×2 Чёрный, 16х100 см", "22-013", "Подвязы/Хлопок", 0, tiers))
                            .put(product("p3", "Поло-воротник белый", "PV-1", "Поло-воротники", 5, listOf(1 to "120"))),
                    )
                    .put("clients", JSONArray().put(JSONObject().put("name", "ООО \"БРЕНД СИТИ\"").put("inn", "7726358110"))),
            )
        }
    }

    @get:Rule
    val rules: RuleChain = RuleChain.outerRule(ResetAppRule()).around(seed).around(compose)

    private fun money(v: String) = QuoteCalculator.formatMoney(BigDecimal(v)) + " ₽"

    @Test fun pickMoySkladProductWithTierPrice() {
        compose.onNodeWithText("Коммерческое предложение").performClick()
        compose.onNodeWithText("Добавить позицию").performScrollTo().performClick()
        compose.onNodeWithText("Товары МойСклад (3)").assertExists()
        compose.onNode(hasSetTextAction() and hasText("Поиск: название или артикул")).performTextInput("белый подвяз")
        compose.onNodeWithText("Поло-воротник белый").assertDoesNotExist()
        compose.onNodeWithText("На складе Электросталь: 120 шт").assertExists()
        Shots.take("50_ms_picker", compose)
        compose.onNodeWithText("Подвяз трикотажный 1×1 (ПЭ) Белый, 14х100 см").performClick()

        val qty = compose.onNode(hasSetTextAction() and hasText("Количество"))
        qty.performTextClearance()
        qty.performTextInput("600")
        // 600 × 158,34 ₽ («от 500 штук») = 95 004,00 ₽.
        compose.onAllNodes(hasText(money("95004"))).onFirst().performScrollTo().assertExists()
        qty.performTextClearance()
        qty.performTextInput("10")
        compose.onAllNodes(hasText(money("1943.30"))).onFirst().performScrollTo().assertExists()
        compose.onNodeWithText("На складе Электросталь: 120 шт").assertExists()
        Shots.take("51_ms_line", compose)

        // Клиент из МойСклад — в подсказках.
        compose.onNodeWithText("Компания клиента").performScrollTo().performTextInput("БРЕНД")
        compose.onAllNodes(hasText("БРЕНД СИТИ", substring = true)).onFirst().assertExists()
    }
}
