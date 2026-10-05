package com.knit.calculator.core

import java.math.BigDecimal

/** Товар МойСклад, как его отдаёт скрипт таблицы: цены по тиражам «от N штук». */
data class MsItem(
    val id: String,
    val name: String,
    val article: String = "",
    val group: String = "",
    val weightGrams: BigDecimal? = null,
    val buyPrice: BigDecimal? = null,
    val minPrice: BigDecimal? = null,
    val description: String = "",
    /** Порог тиража → цена за единицу. */
    val tiers: List<Pair<BigDecimal, BigDecimal>> = emptyList(),
    val stock: BigDecimal? = null,
    val type: String = "product",
    val chars: Map<String, String> = emptyMap(),
    val badges: List<String> = emptyList(),
)

/** Товары МойСклад в виде позиций ассортимента КП. */
object MoySklad {
    fun productId(msId: String): Long = CatalogParser.stableId("moysklad", msId)

    /**
     * Цена «от 1 штуки» — базовая, остальные тиражи — точной дробью к ней, без промежуточных округлений:
     * в КП получается ровно цена из МойСклад. Если цены за 1 шт нет, минимальная партия — первый тираж.
     */
    fun product(item: MsItem, clientChars: List<String> = emptyList()): Product? {
        val tiers = item.tiers.filter { it.second.signum() > 0 }.sortedBy { it.first }
        val (firstQty, base) = tiers.firstOrNull() ?: return null
        // В названии позиции — характеристики для клиента (состав, цвет, размер): они уйдут в КП, PDF и МойСклад.
        val shown = clientChars.mapNotNull { item.chars[it]?.takeIf { v -> v.isNotBlank() && v != "-" } }
            .filterNot { item.name.contains(it, ignoreCase = true) }
        return Product(
            id = productId(item.id),
            name = (listOf(item.name) + shown).joinToString(", "),
            unit = "шт",
            basePrice = base,
            minOrder = if (firstQty > BigDecimal.ONE) firstQty else BigDecimal.ZERO,
            tiers = tiers.drop(1).map { (qty, price) -> PriceTier(qty, Factor(price, base), "") },
            rounding = BigDecimal("0.01"),
            weightGrams = item.weightGrams?.takeIf { it.signum() > 0 },
            code = item.article,
            externalId = item.id,
            group = item.group,
            stock = item.stock,
            buyPrice = item.buyPrice?.takeIf { it.signum() > 0 },
            minPrice = item.minPrice?.takeIf { it.signum() > 0 },
            description = item.description,
            attributes = item.chars,
            badges = item.badges,
            externalType = item.type,
        )
    }

    /** Поиск по названию, артикулу и группе: все слова запроса, без учёта регистра. */
    fun search(products: List<Product>, query: String, group: String? = null, filters: Map<String, String> = emptyMap()): List<Product> {
        val words = query.lowercase().split(' ', ',').map { it.trim() }.filter { it.isNotEmpty() }
        return products.filter { p ->
            (group == null || p.group == group || p.group.startsWith("$group/")) &&
                filters.all { (k, v) -> p.attributes[k] == v } &&
                words.all { w ->
                    p.name.lowercase().contains(w) || p.code.lowercase().contains(w) || p.group.lowercase().contains(w) ||
                        p.attributes.values.any { it.lowercase().contains(w) }
                }
        }.sortedByDescending { it.badges.isNotEmpty() } // «Топ-продажа» и «Популярный» — первыми
    }

    /**
     * Значения характеристики-фильтра среди найденного: чипы показываем, когда выбирать есть из чего
     * (от 2 до [max] значений); уникальные (артикулы) ищутся строкой поиска.
     */
    fun filterValues(products: List<Product>, name: String, max: Int = 30): List<String> {
        val values = products.mapNotNull { it.attributes[name] }.distinct().sorted()
        return if (values.size in 2..max) values else emptyList()
    }

    /** Верхние группы товаров для фильтра. */
    fun topGroups(products: List<Product>): List<String> =
        products.map { it.group.substringBefore('/') }.filter { it.isNotBlank() }.distinct().sorted()
}
