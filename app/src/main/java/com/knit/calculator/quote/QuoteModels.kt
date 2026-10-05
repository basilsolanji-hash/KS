package com.knit.calculator.quote

import com.knit.calculator.core.CatalogParser
import com.knit.calculator.core.CostSettings
import com.knit.calculator.core.Coefficients
import com.knit.calculator.core.OptionGroup
import com.knit.calculator.core.PriceChoice
import com.knit.calculator.core.PriceTier
import com.knit.calculator.core.Product
import com.knit.calculator.core.QuoteStatus
import com.knit.calculator.core.SeedCatalog
import com.knit.calculator.core.YarnCalculator
import java.math.BigDecimal
import java.util.UUID
import kotlin.random.Random

fun newId(): Long = Random.nextLong(1, Long.MAX_VALUE)

/** Реквизиты и условия, которые печатаются в КП. */
data class CompanySettings(
    val brand: String = "Фабрика \"KS\"",
    val city: String = "г. Электросталь",
    val legalName: String = "ООО «Солвер»",
    val legalFullName: String = "Общество с ограниченной ответственностью «Солвер»",
    val inn: String = "9705239429",
    val kpp: String = "772301001",
    val ogrn: String = "1257700099832",
    val legalAddress: String = "109380, г. Москва, м.о. Люблино, ул. Чагинская, д. 4, стр. 13, пом. 9/3",
    val factAddress: String = "144010, Московская обл., г. Электросталь, ул. Ялагина, 3",
    val director: String = "Соланджи Басил",
    val bank: String = "ПАО Сбербанк",
    val account: String = "40702810538710009446",
    val bik: String = "044525225",
    val corrAccount: String = "30101810400000000225",
    val phone: String = "+7 985 000-79-92",
    val email: String = "Sale@fabrika-ks.ru",
    val website: String = "fabrika-ks.ru",
    val vatRate: String = "22",
    val vatIncluded: Boolean = true,
    val validityDays: String = "5",
    val leadTime: String = "5–15 рабочих дней",
    val freeDeliveryFrom: String = "50000",
    val terms: String = "Цены указаны в рублях.",
    val deliveryArea: String = "по Москве и Московской области",
    // Юридические условия КП (печатаются мелким шрифтом в конце).
    val termOffer: String = "Предложение носит информационный характер и не является публичной офертой (ст. 437 ГК РФ). " +
        "Договор заключается путём подписания договора поставки или оплаты счёта. " +
        "После окончания срока действия цены могут быть изменены в связи с изменением стоимости сырья.",
    val termPayment: String = "Оплата: предоплата 50 %, остаток — перед отгрузкой.",
    val termQuality: String = "Цвет изделий может незначительно отличаться от изображения на экране. " +
        "Допустимое отклонение количества — ±5 %, размеров — ±1 см. " +
        "После письменного утверждения образца (макета) претензии по дизайну не принимаются.",
    val termRights: String = "Заказчик гарантирует наличие прав на логотипы и изображения, переданные для изготовления, " +
        "и самостоятельно несёт ответственность за их использование.",
    val termConfidential: String = "Условия и цены настоящего предложения конфиденциальны и предназначены только для адресата. " +
        "Передача третьим лицам без согласия ООО «Солвер» не допускается.",
    val termPersonal: String = "Контактные данные получателя используются только для подготовки и исполнения заказа " +
        "(Федеральный закон № 152-ФЗ «О персональных данных»).",
    val emailDisclaimer: String = "Письмо и вложения предназначены только для адресата. " +
        "Если вы получили его по ошибке, сообщите отправителю и удалите письмо.",
    val signature: String = "С уважением, команда Фабрики \"KS\"",
    // Экономика (модель «База КП»).
    val fixedMonthly: String = "954000",
    val planQuantity: String = "13000",
    val commissionPercent: String = "9",
    val targetMarginPercent: String = "25",
    val maxDiscountPercent: String = "7",
    val reminderDays: String = "1",
    val yarnWastePercent: String = "3",
    val shopUrl: String = "https://fabrika-ks.ru/shop",
    /** Предоплата для счёта и договора, %. */
    val prepayPercent: String = "50",
    /** PIN режима директора; пусто — все видят себестоимость и прибыль. */
    val directorPin: String = "",
) {
    val prepay: BigDecimal
        get() = YarnCalculator.parseDecimal(prepayPercent)?.coerceIn(BigDecimal.ZERO, BigDecimal(100)) ?: BigDecimal(50)

    /** Юридические условия по порядку; пустые не печатаются. */
    val legalTerms: List<String>
        get() = listOf(termOffer, termPayment, termQuality, termRights, termConfidential, termPersonal).map { it.trim() }.filter { it.isNotEmpty() }

    val freeDeliveryThreshold: BigDecimal?
        get() = YarnCalculator.parseDecimal(freeDeliveryFrom)?.takeIf { it.signum() > 0 }

    val maxDiscount: BigDecimal
        get() = YarnCalculator.parseDecimal(maxDiscountPercent)?.coerceIn(BigDecimal.ZERO, BigDecimal(100)) ?: BigDecimal.ZERO

    val reminderDaysValue: Int
        get() = YarnCalculator.parseDecimal(reminderDays)?.toInt()?.coerceIn(0, 30) ?: 1

    val yarnWaste: BigDecimal
        get() = YarnCalculator.parseDecimal(yarnWastePercent) ?: BigDecimal.ZERO

    fun costSettings(): CostSettings = CostSettings(
        fixedMonthly = YarnCalculator.parseDecimal(fixedMonthly) ?: BigDecimal.ZERO,
        planQuantity = YarnCalculator.parseDecimal(planQuantity) ?: BigDecimal.ZERO,
        commissionPercent = YarnCalculator.parseDecimal(commissionPercent) ?: BigDecimal.ZERO,
        targetMarginPercent = YarnCalculator.parseDecimal(targetMarginPercent) ?: BigDecimal.ZERO,
        vat = vat(),
    )

    companion object {
        // Названия строк листа «Настройки» в Google Таблице.
        const val S_BRAND = "Название для КП"
        const val S_CITY = "Город"
        const val S_LEGAL = "Юр. лицо"
        const val S_INN = "ИНН"
        const val S_LEGAL_FULL = "Полное наименование"
        const val S_KPP = "КПП"
        const val S_OGRN = "ОГРН"
        const val S_LEGAL_ADDRESS = "Юридический адрес"
        const val S_FACT_ADDRESS = "Адрес производства"
        const val S_DIRECTOR = "Генеральный директор"
        const val S_BANK = "Банк"
        const val S_ACCOUNT = "Расчётный счёт"
        const val S_BIK = "БИК"
        const val S_CORR = "Корр. счёт"
        const val S_DELIVERY_AREA = "Зона бесплатной доставки"
        const val S_TERM_OFFER = "Условие: не оферта"
        const val S_TERM_PAYMENT = "Условие: оплата"
        const val S_TERM_QUALITY = "Условие: образец и допуски"
        const val S_TERM_RIGHTS = "Условие: логотипы клиента"
        const val S_TERM_CONFIDENTIAL = "Условие: конфиденциальность"
        const val S_TERM_PERSONAL = "Условие: персональные данные"
        const val S_EMAIL_DISCLAIMER = "Подпись письма: оговорка"
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
        const val S_FIXED = "Постоянные расходы в месяц, ₽"
        const val S_PLAN = "План выпуска, шт/мес"
        const val S_COMMISSION = "Комиссия, %"
        const val S_TARGET = "Целевая рентабельность, %"
        const val S_MAX_DISCOUNT = "Макс. скидка менеджера, %"
        const val S_REMINDER = "Напоминание за, дней"
        const val S_YARN_WASTE = "Брак пряжи, %"
        const val S_SHOP = "Сайт каталога"
        const val S_PREPAY = "Предоплата, %"
        const val S_PIN = "PIN директора"

        /** Реквизиты из листа «Настройки»; отсутствующие строки берутся из [fallback]. */
        fun fromSheet(values: Map<String, String>, fallback: CompanySettings = CompanySettings()): CompanySettings {
            fun v(key: String, default: String) = values[key]?.trim() ?: default
            return CompanySettings(
                brand = v(S_BRAND, fallback.brand),
                city = v(S_CITY, fallback.city),
                legalName = v(S_LEGAL, fallback.legalName),
                inn = v(S_INN, fallback.inn),
                legalFullName = v(S_LEGAL_FULL, fallback.legalFullName),
                kpp = v(S_KPP, fallback.kpp),
                ogrn = v(S_OGRN, fallback.ogrn),
                legalAddress = v(S_LEGAL_ADDRESS, fallback.legalAddress),
                factAddress = v(S_FACT_ADDRESS, fallback.factAddress),
                director = v(S_DIRECTOR, fallback.director),
                bank = v(S_BANK, fallback.bank),
                account = v(S_ACCOUNT, fallback.account),
                bik = v(S_BIK, fallback.bik),
                corrAccount = v(S_CORR, fallback.corrAccount),
                deliveryArea = v(S_DELIVERY_AREA, fallback.deliveryArea),
                termOffer = v(S_TERM_OFFER, fallback.termOffer),
                termPayment = v(S_TERM_PAYMENT, fallback.termPayment),
                termQuality = v(S_TERM_QUALITY, fallback.termQuality),
                termRights = v(S_TERM_RIGHTS, fallback.termRights),
                termConfidential = v(S_TERM_CONFIDENTIAL, fallback.termConfidential),
                termPersonal = v(S_TERM_PERSONAL, fallback.termPersonal),
                emailDisclaimer = v(S_EMAIL_DISCLAIMER, fallback.emailDisclaimer),
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
                fixedMonthly = v(S_FIXED, fallback.fixedMonthly),
                planQuantity = v(S_PLAN, fallback.planQuantity),
                commissionPercent = v(S_COMMISSION, fallback.commissionPercent),
                targetMarginPercent = v(S_TARGET, fallback.targetMarginPercent),
                maxDiscountPercent = v(S_MAX_DISCOUNT, fallback.maxDiscountPercent),
                reminderDays = v(S_REMINDER, fallback.reminderDays),
                yarnWastePercent = v(S_YARN_WASTE, fallback.yarnWastePercent),
                shopUrl = v(S_SHOP, fallback.shopUrl),
                prepayPercent = v(S_PREPAY, fallback.prepayPercent),
                directorPin = v(S_PIN, fallback.directorPin),
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
    /** Скидка менеджера, % (строка поля ввода). */
    val discount: String = "",
    /** Фото образца на этом телефоне (files/photos/…). */
    val photoPath: String? = null,
    /** То же фото в папке «Фото образцов» Google Диска (для других телефонов). */
    val photoFileId: String? = null,
)

/**
 * Черновик КП.
 * @property id уникальный идентификатор КП (одинаковый на всех телефонах после сохранения в таблицу).
 * @property saved КП уже сохранено в таблицу и получило номер [number].
 */
/** Шаблон КП — частый набор позиций одной кнопкой. */
data class QuoteTemplate(val name: String, val lines: List<DraftLine>)

data class QuoteDraft(
    val id: String = UUID.randomUUID().toString(),
    val number: Int = 1,
    val saved: Boolean = false,
    val clientCompany: String = "",
    val clientContact: String = "",
    val clientEmail: String = "",
    val clientPhone: String = "",
    val clientInn: String = "",
    /** КПП и юридический адрес клиента (из DaData или вручную) — для счёта и договора. */
    val clientKpp: String = "",
    val clientAddress: String = "",
    val comment: String = "",
    val lines: List<DraftLine> = emptyList(),
    /** Позиции с ценами на момент сохранения — для счёта и договора (прайс может измениться). */
    val snapshot: List<SnapshotLine> = emptyList(),
)

/** Позиция сохранённого КП: как в PDF клиенту. */
data class SnapshotLine(
    val name: String,
    val quantity: BigDecimal,
    val unit: String,
    val price: BigDecimal,
    val total: BigDecimal,
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
    val parsed by lazy { CatalogParser.parse(SeedCatalog.sheets) }
    val products: List<Product> get() = parsed.products
}

/**
 * КП, отправленное с этого телефона (архив для истории без таблицы, отчёта и напоминаний).
 * С таблицей основной источник — лист «КП», архив дублирует статус для напоминаний.
 */
data class ArchivedQuote(
    val id: String,
    val number: Int,
    val createdAt: Long,
    val client: String,
    val total: BigDecimal,
    val profit: BigDecimal?,
    val status: QuoteStatus,
    val validUntil: Long?,
    val manager: String,
    val data: String,
    /** Изделие → сумма. */
    val products: Map<String, BigDecimal>,
)
