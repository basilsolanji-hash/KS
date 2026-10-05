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
)

/** Товары МойСклад в виде позиций ассортимента КП. */
object MoySklad {
    fun productId(msId: String): Long = CatalogParser.stableId("moysklad", msId)

    /**
     * Цена «от 1 штуки» — базовая, остальные тиражи — точной дробью к ней, без промежуточных округлений:
     * в КП получается ровно цена из МойСклад. Если цены за 1 шт нет, минимальная партия — первый тираж.
     */
    fun product(item: MsItem): Product? {
        val tiers = item.tiers.filter { it.second.signum() > 0 }.sortedBy { it.first }
        val (firstQty, base) = tiers.firstOrNull() ?: return null
        return Product(
            id = productId(item.id),
            name = item.name,
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
        )
    }

    /** Поиск по названию, артикулу и группе: все слова запроса, без учёта регистра. */
    fun search(products: List<Product>, query: String, group: String? = null): List<Product> {
        val words = query.lowercase().split(' ', ',').map { it.trim() }.filter { it.isNotEmpty() }
        return products.filter { p ->
            (group == null || p.group == group || p.group.startsWith("$group/")) &&
                words.all { w -> p.name.lowercase().contains(w) || p.code.lowercase().contains(w) || p.group.lowercase().contains(w) }
        }
    }

    /** Верхние группы товаров для фильтра. */
    fun topGroups(products: List<Product>): List<String> =
        products.map { it.group.substringBefore('/') }.filter { it.isNotBlank() }.distinct().sorted()
}
