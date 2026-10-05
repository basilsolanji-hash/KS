package com.knit.calculator

import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasTestTag
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
            fun product(
                id: String, name: String, article: String, group: String, stock: Int, prices: List<Pair<Int, String>>,
                chars: Map<String, String> = emptyMap(), badges: List<String> = emptyList(), barcode: String = "",
            ) = JSONObject().put("barcode", barcode).put("id", id).put("name", name).put("article", article).put("group", group).put("type", "variant")
                .put("weight", 84.9).put("buyPrice", "143.95").put("minPrice", "143.95").put("stock", stock)
                .put("tiers", JSONArray().apply { prices.forEach { (q, p) -> put(JSONObject().put("from", q).put("price", p)) } })
                .put("chars", JSONObject(chars)).put("badges", JSONArray(badges))
            val tiers = listOf(1 to "201.53", 10 to "194.33", 20 to "187.13", 50 to "172.74", 100 to "165.54", 500 to "158.34")
            store.saveMsCatalog(
                JSONObject().put("enabled", true).put("store", "Электросталь").put("loadedAt", System.currentTimeMillis())
                    .put(
                        "products",
                        JSONArray()
                            .put(
                                product(
                                    "p1", "Подвяз трикотажный 1×1 (ПЭ) Белый, 14х100 см", "11-001 0067", "Подвязы/Полиэстер", 120, tiers,
                                    mapOf(
                                        "Цвет" to "Белый", "Тип резинки" to "1х1", "Артикул производитель" to "AP-77",
                                        "Размер" to "14х100 см", "Артикул" to "11-001 0067",
                                    ),
                                    listOf("Топ-продажа"), barcode = "4601234567893",
                                ),
                            )
                            .put(product("p2", "Подвяз трикотажный 2×2 Чёрный, 16х100 см", "22-013", "Подвязы/Хлопок", 0, tiers, mapOf("Цвет" to "Чёрный", "Тип резинки" to "2х2")))
                            .put(product("p3", "Поло-воротник белый", "PV-1", "Поло-воротники", 5, listOf(1 to "120"))),
                    )
                    .put("clients", JSONArray().put(JSONObject().put("name", "ООО \"БРЕНД СИТИ\"").put("inn", "7726358110")))
                    .put("filters", JSONArray(listOf("Артикул", "Цвет", "Тип резинки", "Тип", "Артикул производитель")))
                    .put("clientChars", JSONArray(listOf("Состав / материала", "Цвет", "Размер"))),
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
        compose.onNode(hasSetTextAction() and hasText("Поиск: название или артикул")).performTextInput("подвяз")
        compose.onNodeWithText("Поло-воротник белый").assertDoesNotExist()
        // Фильтр по характеристике «Цвет» и значок «Топ-продажа».
        compose.onNodeWithText("Белый").performClick()
        compose.onNodeWithText("Подвяз трикотажный 2×2 Чёрный, 16х100 см").assertDoesNotExist()
        compose.onNodeWithText("★ Топ-продажа").assertExists()
        compose.onAllNodes(hasText("Артикул производитель: AP-77", substring = true)).onFirst().assertExists()
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

    @Test fun labelsWithEan13() {
        compose.onNodeWithText("Этикетки").performScrollTo().performClick()
        compose.onNodeWithText("Добавить товар").performClick()
        compose.onNodeWithText("Подвяз трикотажный 1×1 (ПЭ) Белый, 14х100 см").performScrollTo().performClick()
        compose.onNodeWithText("EAN-13: 4 601234 567893").assertExists()
        compose.onNode(hasTestTag("labelPreview")).assertExists()
        Shots.take("52_labels", compose)
        // Товар без штрихкода: предложение создать его в МойСклад.
        compose.onNodeWithText("Добавить товар").performScrollTo().performClick()
        // Сверху — «Недавние» (первый товар); второй находим поиском.
        compose.onNodeWithText("Недавние").assertExists()
        compose.onNode(hasSetTextAction() and hasText("Поиск: название или артикул")).performTextInput("чёрный")
        compose.onNodeWithText("Подвяз трикотажный 2×2 Чёрный, 16х100 см").performScrollTo().performClick()
        compose.onNodeWithText("Нет штрихкода в МойСклад").assertExists()
        compose.onNodeWithText("Создать в МойСклад").assertExists()
        compose.onNodeWithText("Принтер не выбран").performScrollTo().assertExists()
        Shots.take("53_labels_list", compose)

        // Этикетка в точках принтера (203 dpi): макет 959×592, команды TSPL для рулона 75 мм.
        val product = com.knit.calculator.core.Product(
            1, "Подвяз трикотажный 1×1 (ПЭ) Белый, 14х100 см", "шт", BigDecimal.ONE, code = "11-001 0067", barcode = "4601234567893",
            attributes = mapOf("Цвет" to "Белый", "Размер" to "14х100 см", "Состав / материала" to "полиэстер 100%"),
        )
        val spec = com.knit.calculator.core.LabelSpec()
        val content = com.knit.calculator.label.labelContent(
            com.knit.calculator.label.LabelJob(product, 2, "50"), com.knit.calculator.quote.CompanySettings(), "10.2026",
        )
        val bitmap = com.knit.calculator.label.LabelRenderer.render(content, spec)
        org.junit.Assert.assertEquals(959, bitmap.width)
        org.junit.Assert.assertEquals(592, bitmap.height)
        Shots.save(bitmap, "54_label_203dpi")
        val mono = com.knit.calculator.label.LabelRenderer.toMono(bitmap)
        val cmd = String(com.knit.calculator.core.LabelPrinter.commands(spec, mono, 2), Charsets.ISO_8859_1)
        org.junit.Assert.assertTrue(cmd.startsWith("SIZE 75 mm,120 mm"))
        org.junit.Assert.assertTrue(cmd.contains("BITMAP 0,0,74,959,0,"))
        org.junit.Assert.assertTrue(cmd.endsWith("PRINT 2,1\r\n"))
    }

    @Test fun productsCatalogCard() {
        compose.onNodeWithText("Товары").performScrollTo().performClick()
        compose.onNodeWithText("Товары МойСклад (3)").assertExists()
        compose.onNodeWithText("Подвяз трикотажный 1×1 (ПЭ) Белый, 14х100 см").performClick()
        // Карточка: цены по тиражам, остаток, штрихкод.
        compose.onNodeWithText("от 500 шт").assertExists()
        compose.onAllNodes(hasText(money("158.34"))).onFirst().assertExists()
        compose.onNodeWithText("EAN-13: 4 601234 567893").assertExists()
        Shots.take("55_product_card", compose)
        compose.onNodeWithText("В КП").performClick()
        compose.onAllNodes(hasText("Подвяз трикотажный 1×1 (ПЭ) Белый, 14х100 см", substring = true)).onFirst().assertExists()
        // Позиция в КП ждёт количество.
        compose.onNode(hasSetTextAction() and hasText("Количество")).assertExists()
    }
}
