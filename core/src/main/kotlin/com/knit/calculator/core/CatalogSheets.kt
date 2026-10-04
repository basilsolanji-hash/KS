package com.knit.calculator.core

import java.math.BigDecimal

/**
 * Справочники в виде строк таблицы (как в Google Таблице). Первая строка каждого листа — заголовок.
 *
 * Лист «Изделия»:   Код | Название | Ед. | Базовая цена, ₽ | Мин. заказ | Подготовка, ₽ | Округление, ₽ | Активно
 * Лист «Параметры»: Код изделия | Параметр | Вариант | Коэффициент | Надбавка, ₽
 * Лист «Объём»:     Код изделия | От количества | Коэффициент | Примечание
 * Лист «Настройки»: Параметр | Значение | Описание
 */
data class CatalogSheets(
    val products: List<List<String>>,
    val parameters: List<List<String>>,
    val volume: List<List<String>>,
    val settings: List<List<String>> = emptyList(),
)

data class ParsedCatalog(
    val products: List<Product>,
    /** Значения листа «Настройки»: название параметра → значение. */
    val settings: Map<String, String>,
    /** Понятные описания пропущенных строк: «Изделия, строка 4: цена не число». */
    val warnings: List<String>,
)

object CatalogParser {
    const val SHEET_PRODUCTS = "Изделия"
    const val SHEET_PARAMETERS = "Параметры"
    const val SHEET_VOLUME = "Объём"
    const val SHEET_SETTINGS = "Настройки"

    /** Стабильный идентификатор из строк (FNV-1a, 64 бит): одинаков на всех телефонах. */
    fun stableId(vararg parts: String): Long {
        var hash = -0x340d631b7bdddcdbL // 14695981039346656037
        parts.joinToString("\u0001") { it.trim().lowercase() }.forEach { ch ->
            hash = hash xor ch.code.toLong()
            hash *= 0x100000001b3L
        }
        return hash and Long.MAX_VALUE
    }

    fun parse(sheets: CatalogSheets): ParsedCatalog {
        val warnings = mutableListOf<String>()
        fun cell(row: List<String>, i: Int) = row.getOrNull(i)?.trim().orEmpty()
        fun number(row: List<String>, i: Int, default: BigDecimal? = null): BigDecimal? {
            val text = cell(row, i).replace("₽", "").replace("%", "")
            return if (text.isEmpty()) default else YarnCalculator.parseDecimal(text)
        }

        // Параметры: код → группы в порядке первого появления.
        val groups = linkedMapOf<String, LinkedHashMap<String, MutableList<PriceChoice>>>()
        sheets.parameters.drop(1).forEachIndexed { index, row ->
            val line = index + 2
            val code = cell(row, 0)
            val group = cell(row, 1)
            val name = cell(row, 2)
            if (code.isEmpty() && group.isEmpty() && name.isEmpty()) return@forEachIndexed
            if (code.isEmpty() || group.isEmpty() || name.isEmpty()) {
                warnings += "$SHEET_PARAMETERS, строка $line: нужны код изделия, параметр и вариант"
                return@forEachIndexed
            }
            val factorText = cell(row, 3)
            val factors = Coefficients.parse(factorText)
            val add = number(row, 4, BigDecimal.ZERO)
            if (factors == null || add == null) {
                warnings += "$SHEET_PARAMETERS, строка $line: некорректный коэффициент или надбавка"
                return@forEachIndexed
            }
            groups.getOrPut(code.lowercase()) { linkedMapOf() }
                .getOrPut(group) { mutableListOf() }
                .add(PriceChoice(stableId(code, group, name), name, add, factors, factorText))
        }

        val tiers = linkedMapOf<String, MutableList<PriceTier>>()
        sheets.volume.drop(1).forEachIndexed { index, row ->
            val line = index + 2
            val code = cell(row, 0)
            if (code.isEmpty() && cell(row, 1).isEmpty()) return@forEachIndexed
            val from = number(row, 1)
            val factorText = cell(row, 2)
            val factors = Coefficients.parse(factorText)
            if (code.isEmpty() || from == null || factors == null) {
                warnings += "$SHEET_VOLUME, строка $line: нужны код изделия, число «от количества» и коэффициент"
                return@forEachIndexed
            }
            tiers.getOrPut(code.lowercase()) { mutableListOf() }.add(PriceTier(from, Coefficients.product(factors), factorText))
        }

        val products = sheets.products.drop(1).mapIndexedNotNull { index, row ->
            val line = index + 2
            val code = cell(row, 0)
            val name = cell(row, 1)
            if (code.isEmpty() && name.isEmpty()) return@mapIndexedNotNull null
            val active = cell(row, 7).lowercase()
            if (active in setOf("нет", "no", "false", "0", "ложь")) return@mapIndexedNotNull null
            val price = number(row, 3)
            val minOrder = number(row, 4, BigDecimal.ZERO)
            val setup = number(row, 5, BigDecimal.ZERO)
            val rounding = number(row, 6, BigDecimal.ONE)
            if (code.isEmpty() || name.isEmpty() || price == null || minOrder == null || setup == null || rounding == null) {
                warnings += "$SHEET_PRODUCTS, строка $line: нужны код, название и числовая цена"
                return@mapIndexedNotNull null
            }
            val key = code.lowercase()
            Product(
                id = stableId(code),
                code = code,
                name = name,
                unit = cell(row, 2).ifEmpty { "шт" },
                basePrice = price,
                minOrder = minOrder,
                setupFee = setup,
                rounding = rounding,
                options = groups[key].orEmpty().map { (group, choices) -> OptionGroup(stableId(code, group), group, choices) },
                tiers = tiers[key].orEmpty().sortedBy { it.fromQuantity },
            )
        }

        val settings = sheets.settings.drop(1)
            .filter { cell(it, 0).isNotEmpty() }
            .associate { cell(it, 0) to cell(it, 1) }
        return ParsedCatalog(products, settings, warnings)
    }
}

/**
 * Стартовые справочники: такие же строки создаются в Google Таблице.
 * Подвязы — по «Прайс-листу подвязов KS» (база 170 ₽ за 115×14, ротация цен по объёму, +4 % по типу нити).
 * Воротник и манжеты поло — ориентировочные цены, замените их в таблице.
 */
object SeedCatalog {
    val products: List<List<String>> = listOf(
        listOf("Код", "Название", "Ед.", "Базовая цена, ₽", "Мин. заказ", "Подготовка, ₽", "Округление, ₽", "Активно"),
        listOf("PODV", "Подвяз трикотажный", "шт", "170", "1", "0", "1", "да"),
        listOf("POLO", "Воротник поло", "шт", "120", "50", "0", "1", "да"),
        listOf("MANZH", "Манжеты поло", "пара", "60", "50", "0", "1", "да"),
    )

    private val podvSizes = (13..20).map { w -> listOf("PODV", "Размер", "115×$w", "$w/14", "0") }
    private val types = listOf(
        "Полиэстер 1×1" to "1",
        "Хлопок 1×1" to "1,04",
        "Полиэстер 2×2" to "1,04*1,04",
        "Хлопок 2×2" to "1,04*1,04*1,04",
    )

    val parameters: List<List<String>> = listOf(listOf("Код изделия", "Параметр", "Вариант", "Коэффициент", "Надбавка, ₽")) +
        podvSizes +
        types.map { (n, k) -> listOf("PODV", "Тип", n, k, "0") } +
        listOf(
            listOf("POLO", "Размер", "37×8 см", "1", "0"),
            listOf("POLO", "Размер", "40×9 см", "1,05", "0"),
            listOf("POLO", "Размер", "42×10 см", "1,1", "0"),
        ) +
        types.map { (n, k) -> listOf("POLO", "Тип", n, k, "0") } +
        listOf(
            listOf("POLO", "Рисунок", "Гладкий", "1", "0"),
            listOf("POLO", "Рисунок", "1 полоса", "1", "10"),
            listOf("POLO", "Рисунок", "2 полосы", "1", "15"),
            listOf("POLO", "Рисунок", "3 полосы", "1", "20"),
            listOf("MANZH", "Ширина", "3 см", "1", "0"),
            listOf("MANZH", "Ширина", "3,5 см", "1,05", "0"),
            listOf("MANZH", "Ширина", "4 см", "1,1", "0"),
        ) +
        types.map { (n, k) -> listOf("MANZH", "Тип", n, k, "0") }

    private val rotation = listOf(
        "500" to ("1" to "500 шт и больше — базовая цена"),
        "100" to ("1,07" to "100–499 шт — наценка 7 %"),
        "50" to ("1,07*1,03" to "50–99 шт — ещё 3 % к предыдущему"),
        "20" to ("1,07*1,03*1,22" to "20–49 шт — ещё 22 % к предыдущему"),
        "10" to ("1,07*1,03*1,22*1,22" to "10–19 шт — ещё 22 % к предыдущему"),
        "1" to ("1,07*1,03*1,22*1,22*1,22" to "1–9 шт — ещё 22 % к предыдущему"),
    )

    val volume: List<List<String>> = listOf(listOf("Код изделия", "От количества", "Коэффициент", "Примечание")) +
        listOf("PODV", "POLO", "MANZH").flatMap { code ->
            rotation.map { (from, kv) -> listOf(code, from, kv.first, kv.second) }
        }

    val sheets = CatalogSheets(products, parameters, volume)
}
