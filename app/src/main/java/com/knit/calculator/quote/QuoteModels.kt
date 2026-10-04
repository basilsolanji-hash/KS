package com.knit.calculator.quote

import com.knit.calculator.core.DiscountTier
import com.knit.calculator.core.OptionGroup
import com.knit.calculator.core.PriceChoice
import com.knit.calculator.core.Product
import com.knit.calculator.core.YarnCalculator
import java.math.BigDecimal
import kotlin.random.Random

fun newId(): Long = Random.nextLong(1, Long.MAX_VALUE)

/** Реквизиты и условия, которые печатаются в КП. Всё редактируется в настройках. */
data class CompanySettings(
    val brand: String = "Трикотажная фабрика «KS»",
    val city: String = "г. Электросталь",
    val legalName: String = "ООО «СОЛВЕР»",
    val inn: String = "9705239429",
    val phone: String = "+7 985 000-79-92",
    val email: String = "Sale@fabrika-ks.ru",
    val website: String = "fabrika-ks.ru",
    val vatRate: String = "22",
    val vatIncluded: Boolean = true,
    val validityDays: String = "5",
    val leadTime: String = "от 2 рабочих дней",
    val terms: String = "Цены указаны в рублях. Доставка рассчитывается отдельно.",
    val signature: String = "С уважением, команда Фабрики «KS»",
)

/** Строка КП в том виде, как её заполняет менеджер. */
data class DraftLine(
    val id: Long,
    val productId: Long,
    val selected: Map<Long, Long> = emptyMap(),
    val quantity: String = "",
)

data class QuoteDraft(
    val number: Int = 1,
    val clientCompany: String = "",
    val clientContact: String = "",
    val clientEmail: String = "",
    val comment: String = "",
    val lines: List<DraftLine> = emptyList(),
)

// ---------- Редактируемая форма изделия (строки, как в полях ввода) ----------

data class EditableChoice(val id: Long = newId(), val name: String = "", val priceAdd: String = "0")
data class EditableGroup(val id: Long = newId(), val name: String = "", val choices: List<EditableChoice> = listOf(EditableChoice()))
data class EditableTier(val id: Long = newId(), val fromQuantity: String = "", val percent: String = "")

data class EditableProduct(
    val id: Long = newId(),
    val name: String = "",
    val unit: String = "шт",
    val basePrice: String = "",
    val minOrder: String = "",
    val setupFee: String = "0",
    val groups: List<EditableGroup> = emptyList(),
    val tiers: List<EditableTier> = emptyList(),
) {
    /** `null`, если обязательные поля не заполнены или числа некорректны. */
    fun toProduct(): Product? {
        fun num(s: String, blankAsZero: Boolean = true): BigDecimal? =
            if (s.isBlank()) (if (blankAsZero) BigDecimal.ZERO else null) else YarnCalculator.parseDecimal(s)
        if (name.isBlank() || unit.isBlank()) return null
        val price = num(basePrice, blankAsZero = false) ?: return null
        if (price.signum() < 0) return null
        return Product(
            id = id,
            name = name.trim(),
            unit = unit.trim(),
            basePrice = price,
            minOrder = num(minOrder) ?: return null,
            setupFee = num(setupFee) ?: return null,
            options = groups.filter { it.name.isNotBlank() }.map { g ->
                OptionGroup(
                    g.id,
                    g.name.trim(),
                    g.choices.filter { it.name.isNotBlank() }.map { c -> PriceChoice(c.id, c.name.trim(), num(c.priceAdd) ?: return null) },
                )
            }.filter { it.choices.isNotEmpty() },
            tiers = tiers.filter { it.fromQuantity.isNotBlank() && it.percent.isNotBlank() }.map { t ->
                val pct = num(t.percent) ?: return null
                if (pct.signum() < 0 || pct > BigDecimal(100)) return null
                DiscountTier(num(t.fromQuantity) ?: return null, pct)
            }.sortedBy { it.fromQuantity },
        )
    }

    companion object {
        fun from(p: Product) = EditableProduct(
            id = p.id,
            name = p.name,
            unit = p.unit,
            basePrice = p.basePrice.plain(),
            minOrder = p.minOrder.plain(),
            setupFee = p.setupFee.plain(),
            groups = p.options.map { g -> EditableGroup(g.id, g.name, g.choices.map { EditableChoice(it.id, it.name, it.priceAdd.plain()) }) },
            tiers = p.tiers.map { EditableTier(newId(), it.fromQuantity.plain(), it.percent.plain()) },
        )

        private fun BigDecimal.plain(): String =
            stripTrailingZeros().let { if (it.signum() == 0) "0" else it.toPlainString() }.replace('.', ',')
    }
}

/**
 * Стартовый ассортимент. Цены — ориентировочные примеры: замените их на актуальные
 * в разделе «Ассортимент и цены».
 */
object DefaultCatalog {
    private fun bd(s: String) = BigDecimal(s)
    private fun choice(id: Long, name: String, add: String) = PriceChoice(id, name, bd(add))

    private fun pattern(base: Long) = OptionGroup(
        base, "Рисунок",
        listOf(
            choice(base + 1, "Гладкий", "0"),
            choice(base + 2, "1 полоса", "3"),
            choice(base + 3, "2 полосы", "5"),
            choice(base + 4, "3 полосы", "7"),
            choice(base + 5, "С люрексом", "6"),
        ),
    )

    private fun color(base: Long) = OptionGroup(
        base, "Цвет",
        listOf(choice(base + 1, "Из палитры фабрики", "0"), choice(base + 2, "Окраска по Pantone", "5")),
    )

    val products: List<Product> = listOf(
        Product(
            id = 1001, name = "Подвязы трикотажные", unit = "м",
            basePrice = bd("18"), minOrder = bd("50"), setupFee = bd("0"),
            options = listOf(
                OptionGroup(
                    1100, "Ширина",
                    listOf(
                        choice(1101, "2 см", "0"), choice(1102, "2,5 см", "2"), choice(1103, "3 см", "4"),
                        choice(1104, "4 см", "7"), choice(1105, "5 см", "10"),
                    ),
                ),
                pattern(1200),
                color(1300),
            ),
            tiers = listOf(DiscountTier(bd("300"), bd("5")), DiscountTier(bd("1000"), bd("10")), DiscountTier(bd("3000"), bd("15"))),
        ),
        Product(
            id = 2001, name = "Воротник поло", unit = "шт",
            basePrice = bd("35"), minOrder = bd("100"), setupFee = bd("0"),
            options = listOf(
                OptionGroup(
                    2100, "Размер",
                    listOf(choice(2101, "37 × 8 см", "0"), choice(2102, "40 × 9 см", "3"), choice(2103, "42 × 10 см", "5")),
                ),
                pattern(2200),
                color(2300),
            ),
            tiers = listOf(DiscountTier(bd("500"), bd("5")), DiscountTier(bd("1000"), bd("10")), DiscountTier(bd("5000"), bd("15"))),
        ),
        Product(
            id = 3001, name = "Манжеты поло", unit = "пара",
            basePrice = bd("22"), minOrder = bd("100"), setupFee = bd("0"),
            options = listOf(
                OptionGroup(3100, "Ширина", listOf(choice(3101, "3 см", "0"), choice(3102, "3,5 см", "2"), choice(3103, "4 см", "4"))),
                pattern(3200),
                color(3300),
            ),
            tiers = listOf(DiscountTier(bd("500"), bd("5")), DiscountTier(bd("1000"), bd("10"))),
        ),
    )
}
