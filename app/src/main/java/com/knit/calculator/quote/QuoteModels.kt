package com.knit.calculator.quote

import com.knit.calculator.core.CatalogParser
import com.knit.calculator.core.Coefficients
import com.knit.calculator.core.OptionGroup
import com.knit.calculator.core.PriceChoice
import com.knit.calculator.core.PriceTier
import com.knit.calculator.core.Product
import com.knit.calculator.core.SeedCatalog
import com.knit.calculator.core.YarnCalculator
import java.math.BigDecimal
import java.util.UUID
import kotlin.random.Random

fun newId(): Long = Random.nextLong(1, Long.MAX_VALUE)

/** Реквизиты и условия, которые печатаются в КП. */
data class CompanySettings(
    val brand: String = "Фабрика «KS»",
    val city: String = "г. Электросталь",
    val legalName: String = "ООО «СОЛВЕР»",
    val inn: String = "9705239429",
    val phone: String = "+7 985 000-79-92",
    val email: String = "Sale@fabrika-ks.ru",
    val website: String = "fabrika-ks.ru",
    val vatRate: String = "22",
    val vatIncluded: Boolean = true,
    val validityDays: String = "5",
    val leadTime: String = "5–15 рабочих дней",
    val freeDeliveryFrom: String = "50000",
    val terms: String = "Цены указаны в рублях.",
    val signature: String = "С уважением, команда Фабрики «KS»",
) {
    val freeDeliveryThreshold: BigDecimal?
        get() = YarnCalculator.parseDecimal(freeDeliveryFrom)?.takeIf { it.signum() > 0 }

    companion object {
        // Названия строк листа «Настройки» в Google Таблице.
        const val S_BRAND = "Название для КП"
        const val S_CITY = "Город"
        const val S_LEGAL = "Юр. лицо"
        const val S_INN = "ИНН"
        const val S_PHONE = "Телефон"
        const val S_EMAIL = "E-mail"
        const val S_SITE = "Сайт"
        const val S_VAT = "Ставка НДС, %"
        const val S_VAT_INCLUDED = "Цены с НДС"
        const val S_VALIDITY = "Срок действия КП, дней"
        const val S_LEAD_TIME = "Срок изготовления"
        const val S_FREE_DELIVERY = "Бесплатная доставка от, ₽"
        const val S_TERMS = "Прочие условия"
        const val S_SIGNATURE = "Подпись"

        /** Реквизиты из листа «Настройки»; отсутствующие строки берутся из [fallback]. */
        fun fromSheet(values: Map<String, String>, fallback: CompanySettings = CompanySettings()): CompanySettings {
            fun v(key: String, default: String) = values[key]?.trim() ?: default
            return CompanySettings(
                brand = v(S_BRAND, fallback.brand),
                city = v(S_CITY, fallback.city),
                legalName = v(S_LEGAL, fallback.legalName),
                inn = v(S_INN, fallback.inn),
                phone = v(S_PHONE, fallback.phone),
                email = v(S_EMAIL, fallback.email),
                website = v(S_SITE, fallback.website),
                vatRate = v(S_VAT, fallback.vatRate),
                vatIncluded = values[S_VAT_INCLUDED]?.trim()?.lowercase()
                    ?.let { it !in setOf("нет", "no", "false", "0", "ложь") } ?: fallback.vatIncluded,
                validityDays = v(S_VALIDITY, fallback.validityDays),
                leadTime = v(S_LEAD_TIME, fallback.leadTime),
                freeDeliveryFrom = v(S_FREE_DELIVERY, fallback.freeDeliveryFrom),
                terms = v(S_TERMS, fallback.terms),
                signature = v(S_SIGNATURE, fallback.signature),
            )
        }
    }
}

/** Строка КП в том виде, как её заполняет менеджер. */
data class DraftLine(
    val id: Long,
    val productId: Long,
    val selected: Map<Long, Long> = emptyMap(),
    val quantity: String = "",
)

/**
 * Черновик КП.
 * @property id уникальный идентификатор КП (одинаковый на всех телефонах после сохранения в таблицу).
 * @property saved КП уже сохранено в таблицу и получило номер [number].
 */
data class QuoteDraft(
    val id: String = UUID.randomUUID().toString(),
    val number: Int = 1,
    val saved: Boolean = false,
    val clientCompany: String = "",
    val clientContact: String = "",
    val clientEmail: String = "",
    val comment: String = "",
    val lines: List<DraftLine> = emptyList(),
)

// ---------- Редактируемая форма изделия (строки, как в полях ввода) ----------

data class EditableChoice(val id: Long = newId(), val name: String = "", val factor: String = "1", val priceAdd: String = "0")
data class EditableGroup(val id: Long = newId(), val name: String = "", val choices: List<EditableChoice> = listOf(EditableChoice()))
data class EditableTier(val id: Long = newId(), val fromQuantity: String = "", val factor: String = "")

data class EditableProduct(
    val id: Long = newId(),
    val code: String = "",
    val name: String = "",
    val unit: String = "шт",
    val basePrice: String = "",
    val minOrder: String = "",
    val setupFee: String = "0",
    val rounding: String = "1",
    val groups: List<EditableGroup> = emptyList(),
    val tiers: List<EditableTier> = emptyList(),
) {
    /** `null`, если обязательные поля не заполнены или числа/коэффициенты некорректны. */
    fun toProduct(): Product? {
        fun num(s: String, blankAsZero: Boolean = true): BigDecimal? =
            if (s.isBlank()) (if (blankAsZero) BigDecimal.ZERO else null) else YarnCalculator.parseDecimal(s)
        if (name.isBlank() || unit.isBlank()) return null
        val price = num(basePrice, blankAsZero = false) ?: return null
        val step = num(rounding) ?: return null
        if (price.signum() < 0 || step.signum() < 0) return null
        return Product(
            id = id,
            code = code.trim(),
            name = name.trim(),
            unit = unit.trim(),
            basePrice = price,
            minOrder = num(minOrder) ?: return null,
            setupFee = num(setupFee) ?: return null,
            rounding = step,
            options = groups.filter { it.name.isNotBlank() }.map { g ->
                OptionGroup(
                    g.id,
                    g.name.trim(),
                    g.choices.filter { it.name.isNotBlank() }.map { c ->
                        PriceChoice(c.id, c.name.trim(), num(c.priceAdd) ?: return null, Coefficients.parse(c.factor) ?: return null, c.factor.trim())
                    },
                )
            }.filter { it.choices.isNotEmpty() },
            tiers = tiers.filter { it.fromQuantity.isNotBlank() }.map { t ->
                val factors = Coefficients.parse(t.factor) ?: return null
                PriceTier(num(t.fromQuantity) ?: return null, Coefficients.product(factors), t.factor.trim())
            }.sortedBy { it.fromQuantity },
        )
    }

    companion object {
        fun from(p: Product) = EditableProduct(
            id = p.id,
            code = p.code,
            name = p.name,
            unit = p.unit,
            basePrice = p.basePrice.plain(),
            minOrder = p.minOrder.plain(),
            setupFee = p.setupFee.plain(),
            rounding = p.rounding.plain(),
            groups = p.options.map { g ->
                EditableGroup(g.id, g.name, g.choices.map { EditableChoice(it.id, it.name, it.factorText.ifBlank { "1" }, it.priceAdd.plain()) })
            },
            tiers = p.tiers.map { EditableTier(newId(), it.fromQuantity.plain(), it.factorText.ifBlank { it.factor.value.plain() }) },
        )

        private fun BigDecimal.plain(): String =
            stripTrailingZeros().let { if (it.signum() == 0) "0" else it.toPlainString() }.replace('.', ',')
    }
}

/** Стартовый ассортимент — те же строки, что и в Google Таблице (одинаковые идентификаторы). */
object DefaultCatalog {
    val products: List<Product> by lazy { CatalogParser.parse(SeedCatalog.sheets).products }
}
