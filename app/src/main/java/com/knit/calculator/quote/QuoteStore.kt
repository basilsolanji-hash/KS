package com.knit.calculator.quote

import android.content.Context
import com.knit.calculator.core.CatalogSheets
import com.knit.calculator.core.Coefficients
import com.knit.calculator.core.OptionGroup
import com.knit.calculator.core.PriceChoice
import com.knit.calculator.core.PriceTier
import com.knit.calculator.core.Product
import com.knit.calculator.core.QuoteStatus
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal

/** Настройки подключения к Google Таблице (хранятся только на этом телефоне). */
data class SyncConfig(val url: String = "", val key: String = "", val manager: String = "") {
    val enabled: Boolean get() = url.isNotBlank()
}

/** Последняя загруженная из таблицы копия справочников — для работы без интернета. */
data class SheetCache(val sheets: CatalogSheets, val sheetUrl: String, val loadedAt: Long, val contract: List<List<String>> = emptyList())

/**
 * Локальное хранилище: свой ассортимент (без таблицы), реквизиты, черновик КП,
 * подключение к таблице и кэш справочников из неё. JSON в SharedPreferences.
 */
class QuoteStore(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("quote", Context.MODE_PRIVATE)

    // ---------------- Ассортимент (локальный режим) ----------------

    fun loadCatalog(): List<Product> {
        val raw = prefs.getString(KEY_CATALOG_V2, null) ?: return DefaultCatalog.products
        return try {
            JSONArray(raw).objects().map(::productFrom)
        } catch (e: Exception) {
            DefaultCatalog.products
        }
    }

    fun saveCatalog(products: List<Product>) {
        val array = JSONArray()
        products.forEach { array.put(productTo(it)) }
        prefs.edit().putString(KEY_CATALOG_V2, array.toString()).apply()
    }

    // ---------------- Реквизиты (локальный режим) ----------------

    fun loadSettings(): CompanySettings {
        val raw = prefs.getString(KEY_SETTINGS, null) ?: return CompanySettings()
        return try {
            val o = JSONObject(raw)
            val d = CompanySettings()
            CompanySettings(
                brand = o.optString("brand", d.brand),
                city = o.optString("city", d.city),
                legalName = o.optString("legalName", d.legalName),
                inn = o.optString("inn", d.inn),
                legalFullName = o.optString("legalFullName", d.legalFullName),
                kpp = o.optString("kpp", d.kpp),
                ogrn = o.optString("ogrn", d.ogrn),
                legalAddress = o.optString("legalAddress", d.legalAddress),
                factAddress = o.optString("factAddress", d.factAddress),
                director = o.optString("director", d.director),
                bank = o.optString("bank", d.bank),
                account = o.optString("account", d.account),
                bik = o.optString("bik", d.bik),
                corrAccount = o.optString("corrAccount", d.corrAccount),
                deliveryArea = o.optString("deliveryArea", d.deliveryArea),
                termOffer = o.optString("termOffer", d.termOffer),
                termPayment = o.optString("termPayment", d.termPayment),
                termQuality = o.optString("termQuality", d.termQuality),
                termRights = o.optString("termRights", d.termRights),
                termConfidential = o.optString("termConfidential", d.termConfidential),
                termPersonal = o.optString("termPersonal", d.termPersonal),
                emailDisclaimer = o.optString("emailDisclaimer", d.emailDisclaimer),
                phone = o.optString("phone", d.phone),
                email = o.optString("email", d.email),
                website = o.optString("website", d.website),
                vatRate = o.optString("vatRate", d.vatRate),
                vatIncluded = o.optBoolean("vatIncluded", d.vatIncluded),
                validityDays = o.optString("validityDays", d.validityDays),
                leadTime = o.optString("leadTime", d.leadTime),
                freeDeliveryFrom = o.optString("freeDeliveryFrom", d.freeDeliveryFrom),
                terms = o.optString("terms", d.terms),
                signature = o.optString("signature", d.signature),
                fixedMonthly = o.optString("fixedMonthly", d.fixedMonthly),
                planQuantity = o.optString("planQuantity", d.planQuantity),
                commissionPercent = o.optString("commissionPercent", d.commissionPercent),
                targetMarginPercent = o.optString("targetMarginPercent", d.targetMarginPercent),
                maxDiscountPercent = o.optString("maxDiscountPercent", d.maxDiscountPercent),
                reminderDays = o.optString("reminderDays", d.reminderDays),
                yarnWastePercent = o.optString("yarnWastePercent", d.yarnWastePercent),
                shopUrl = o.optString("shopUrl", d.shopUrl),
                prepayPercent = o.optString("prepayPercent", d.prepayPercent),
                directorPin = o.optString("directorPin", d.directorPin),
            )
        } catch (e: Exception) {
            CompanySettings()
        }
    }

    fun saveSettings(s: CompanySettings) {
        val o = JSONObject()
            .put("brand", s.brand).put("city", s.city).put("legalName", s.legalName).put("inn", s.inn)
            .put("legalFullName", s.legalFullName).put("kpp", s.kpp).put("ogrn", s.ogrn).put("legalAddress", s.legalAddress).put("factAddress", s.factAddress).put("director", s.director).put("bank", s.bank).put("account", s.account).put("bik", s.bik).put("corrAccount", s.corrAccount).put("deliveryArea", s.deliveryArea)
            .put("termOffer", s.termOffer).put("termPayment", s.termPayment).put("termQuality", s.termQuality).put("termRights", s.termRights).put("termConfidential", s.termConfidential).put("termPersonal", s.termPersonal).put("emailDisclaimer", s.emailDisclaimer)
            .put("phone", s.phone).put("email", s.email).put("website", s.website)
            .put("vatRate", s.vatRate).put("vatIncluded", s.vatIncluded)
            .put("validityDays", s.validityDays).put("leadTime", s.leadTime)
            .put("freeDeliveryFrom", s.freeDeliveryFrom)
            .put("terms", s.terms).put("signature", s.signature)
            .put("fixedMonthly", s.fixedMonthly).put("planQuantity", s.planQuantity)
            .put("commissionPercent", s.commissionPercent).put("targetMarginPercent", s.targetMarginPercent)
            .put("maxDiscountPercent", s.maxDiscountPercent).put("reminderDays", s.reminderDays)
            .put("yarnWastePercent", s.yarnWastePercent).put("shopUrl", s.shopUrl)
            .put("prepayPercent", s.prepayPercent).put("directorPin", s.directorPin)
        prefs.edit().putString(KEY_SETTINGS, o.toString()).apply()
    }

    // ---------------- Черновик КП ----------------

    fun loadDraft(): QuoteDraft = prefs.getString(KEY_DRAFT, null)?.let(::draftFromJson) ?: QuoteDraft()

    fun saveDraft(d: QuoteDraft) {
        prefs.edit().putString(KEY_DRAFT, draftToJson(d)).apply()
    }

    // ---------------- Google Таблица ----------------

    fun loadSyncConfig(): SyncConfig = SyncConfig(
        url = prefs.getString(KEY_SYNC_URL, "").orEmpty(),
        key = prefs.getString(KEY_SYNC_KEY, "").orEmpty(),
        manager = prefs.getString(KEY_SYNC_MANAGER, "").orEmpty(),
    )

    fun saveSyncConfig(c: SyncConfig) {
        prefs.edit()
            .putString(KEY_SYNC_URL, c.url)
            .putString(KEY_SYNC_KEY, c.key)
            .putString(KEY_SYNC_MANAGER, c.manager)
            .apply()
    }

    // ---------------- МойСклад (копия товаров для работы без связи) ----------------

    private val msFile = java.io.File(context.applicationContext.filesDir, "moysklad.json")

    fun loadMsCatalog(): JSONObject? = runCatching { if (msFile.exists()) JSONObject(msFile.readText()) else null }.getOrNull()

    fun saveMsCatalog(o: JSONObject?) {
        if (o == null) msFile.delete() else msFile.writeText(o.toString())
    }

    /** Роль по ключу из таблицы («director» / «manager») и имя менеджера из листа «Менеджеры». */
    var serverRole: String
        get() = prefs.getString("server_role", "director").orEmpty()
        set(value) = prefs.edit().putString("server_role", value).apply()

    var serverManager: String
        get() = prefs.getString("server_manager", "").orEmpty()
        set(value) = prefs.edit().putString("server_manager", value).apply()

    /** PIN, которым на этом телефоне открыт режим директора. */
    /** Шаблоны КП: название → позиции (товар, варианты, количество, скидка). */
    fun loadTemplates(): List<QuoteTemplate> = runCatching {
        val a = org.json.JSONArray(prefs.getString("templates", "[]"))
        (0 until a.length()).mapNotNull { a.optJSONObject(it) }.map { t ->
            val lines = t.optJSONArray("lines") ?: org.json.JSONArray()
            QuoteTemplate(
                t.optString("name"),
                (0 until lines.length()).mapNotNull { lines.optJSONObject(it) }.map { l ->
                    val sel = l.optJSONObject("selected") ?: org.json.JSONObject()
                    DraftLine(
                        id = 0, productId = l.optLong("productId"),
                        selected = sel.keys().asSequence().associate { it.toLong() to sel.getLong(it) },
                        quantity = l.optString("quantity"), discount = l.optString("discount"),
                    )
                },
            )
        }
    }.getOrDefault(emptyList())

    fun saveTemplates(list: List<QuoteTemplate>) {
        val a = org.json.JSONArray()
        list.forEach { t ->
            val lines = org.json.JSONArray()
            t.lines.forEach { l ->
                val sel = org.json.JSONObject()
                l.selected.forEach { (k, v) -> sel.put(k.toString(), v) }
                lines.put(org.json.JSONObject().put("productId", l.productId).put("selected", sel).put("quantity", l.quantity).put("discount", l.discount))
            }
            a.put(org.json.JSONObject().put("name", t.name).put("lines", lines))
        }
        prefs.edit().putString("templates", a.toString()).apply()
    }

    /** Вход по отпечатку / PIN телефона при запуске и после 5 минут в фоне. */
    var appLock: Boolean
        get() = prefs.getBoolean("app_lock", false)
        set(v) { prefs.edit().putBoolean("app_lock", v).apply() }

    /** Свой PIN приложения (только хэш с солью); пусто — PIN не задан. */
    val pinSet: Boolean get() = !prefs.getString("pin_hash", null).isNullOrEmpty()

    fun setPin(pin: String?) {
        if (pin == null) {
            prefs.edit().remove("pin_hash").remove("pin_salt").remove("pin_fails").remove("pin_blocked").apply()
            return
        }
        val salt = java.util.UUID.randomUUID().toString()
        prefs.edit().putString("pin_salt", salt).putString("pin_hash", com.knit.calculator.core.PinLock.hash(pin, salt))
            .remove("pin_fails").remove("pin_blocked").apply()
    }

    fun checkPin(pin: String): Boolean =
        com.knit.calculator.core.PinLock.hash(pin, prefs.getString("pin_salt", "").orEmpty()) == prefs.getString("pin_hash", null)

    var pinAttempts: com.knit.calculator.core.PinLock.Attempts
        get() = com.knit.calculator.core.PinLock.Attempts(prefs.getInt("pin_fails", 0), prefs.getLong("pin_blocked", 0L))
        set(v) { prefs.edit().putInt("pin_fails", v.fails).putLong("pin_blocked", v.blockedUntil).apply() }

    /** Кнопки наверху главного экрана (названия разделов); `null` — как по умолчанию. */
    var homeShortcuts: List<String>?
        get() = prefs.getString("home_shortcuts", null)?.split(',')?.filter { it.isNotBlank() }
        set(v) { prefs.edit().putString("home_shortcuts", v?.joinToString(",")).apply() }

    /** Ежедневная сводка в 9:00 (долги, отгрузки, КП без ответа). */
    var digestEnabled: Boolean
        get() = prefs.getBoolean("digest", true)
        set(v) { prefs.edit().putBoolean("digest", v).apply() }

    /** Избранные товары МойСклад (ID МойСклад). */
    var favoriteMs: Set<String>
        get() = prefs.getStringSet("fav_ms", emptySet()).orEmpty().toSet()
        set(v) { prefs.edit().putStringSet("fav_ms", v).apply() }

    /** Последние выбранные товары МойСклад, новые — первыми. */
    var recentMs: List<String>
        get() = prefs.getString("recent_ms", "").orEmpty().split('\n').filter { it.isNotBlank() }
        set(v) { prefs.edit().putString("recent_ms", v.take(12).joinToString("\n")).apply() }

    var directorPin: String
        get() = prefs.getString("director_pin", "").orEmpty()
        set(value) = prefs.edit().putString("director_pin", value).apply()

    var logoVersion: String
        get() = prefs.getString(KEY_LOGO_VERSION, "").orEmpty()
        set(value) = prefs.edit().putString(KEY_LOGO_VERSION, value).apply()

    fun loadSheetCache(): SheetCache? {
        val raw = prefs.getString(KEY_SHEET_CACHE, null) ?: return null
        return try {
            val o = JSONObject(raw)
            SheetCache(sheetsFromJson(o.getJSONObject("sheets")), o.optString("url"), o.optLong("loadedAt"), rowsFromJson(o.optJSONArray("contract")))
        } catch (e: Exception) {
            null
        }
    }

    fun saveSheetCache(cache: SheetCache?) {
        if (cache == null) {
            prefs.edit().remove(KEY_SHEET_CACHE).apply()
            return
        }
        val o = JSONObject().put("sheets", sheetsToJson(cache.sheets)).put("url", cache.sheetUrl).put("loadedAt", cache.loadedAt)
            .put("contract", rowsToJson(cache.contract))
        prefs.edit().putString(KEY_SHEET_CACHE, o.toString()).apply()
    }

    // ---------------- Архив КП и клиенты (этот телефон) ----------------

    fun loadArchive(): List<ArchivedQuote> {
        val raw = prefs.getString(KEY_ARCHIVE, null) ?: return emptyList()
        return try {
            JSONArray(raw).objects().map { o ->
                val products = o.optJSONObject("products") ?: JSONObject()
                ArchivedQuote(
                    id = o.getString("id"),
                    number = o.optInt("number"),
                    createdAt = o.optLong("createdAt"),
                    client = o.optString("client"),
                    total = BigDecimal(o.optString("total", "0")),
                    profit = o.optString("profit").takeIf { it.isNotBlank() }?.let(::BigDecimal),
                    status = QuoteStatus.from(o.optString("status")),
                    validUntil = o.optLong("validUntil").takeIf { it > 0 },
                    manager = o.optString("manager"),
                    data = o.optString("data"),
                    products = products.keys().asSequence().associateWith { BigDecimal(products.getString(it)) },
                )
            }
        } catch (e: Exception) {
            emptyList()
        }
    }

    fun saveArchive(list: List<ArchivedQuote>) {
        val array = JSONArray()
        list.take(MAX_ARCHIVE).forEach { q ->
            val products = JSONObject()
            q.products.forEach { (k, v) -> products.put(k, v.toPlainString()) }
            array.put(
                JSONObject().put("id", q.id).put("number", q.number).put("createdAt", q.createdAt)
                    .put("client", q.client).put("total", q.total.toPlainString())
                    .put("profit", q.profit?.toPlainString() ?: "").put("status", q.status.title)
                    .put("validUntil", q.validUntil ?: 0L).put("manager", q.manager)
                    .put("data", q.data).put("products", products),
            )
        }
        prefs.edit().putString(KEY_ARCHIVE, array.toString()).apply()
    }

    fun loadLocalClients(): List<com.knit.calculator.core.Client> {
        val raw = prefs.getString(KEY_CLIENTS, null) ?: return emptyList()
        return try {
            JSONArray(raw).objects().map {
                com.knit.calculator.core.Client(
                    it.optString("company"), it.optString("contact"), it.optString("email"), it.optString("phone"), it.optString("inn"),
                    kpp = it.optString("kpp"), address = it.optString("address"),
                )
            }
        } catch (e: Exception) {
            emptyList()
        }
    }

    fun saveLocalClients(list: List<com.knit.calculator.core.Client>) {
        val array = JSONArray()
        list.forEach { c ->
            array.put(JSONObject().put("company", c.company).put("contact", c.contact).put("email", c.email).put("phone", c.phone).put("inn", c.inn)
                .put("kpp", c.kpp).put("address", c.address))
        }
        prefs.edit().putString(KEY_CLIENTS, array.toString()).apply()
    }

    // ---------------- JSON ----------------

    private fun JSONArray.objects(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }

    private fun productTo(p: Product): JSONObject {
        val options = JSONArray()
        p.options.forEach { g ->
            val choices = JSONArray()
            g.choices.forEach { c ->
                choices.put(JSONObject().put("id", c.id).put("name", c.name).put("add", c.priceAdd.toPlainString()).put("factor", c.factorText))
            }
            options.put(JSONObject().put("id", g.id).put("name", g.name).put("choices", choices))
        }
        val tiers = JSONArray()
        p.tiers.forEach { t -> tiers.put(JSONObject().put("from", t.fromQuantity.toPlainString()).put("factor", t.factorText.ifBlank { t.factor.value.toPlainString() })) }
        return JSONObject()
            .put("id", p.id).put("code", p.code).put("name", p.name).put("unit", p.unit)
            .put("basePrice", p.basePrice.toPlainString()).put("minOrder", p.minOrder.toPlainString())
            .put("setupFee", p.setupFee.toPlainString()).put("rounding", p.rounding.toPlainString())
            .put("options", options).put("tiers", tiers)
    }

    private fun productFrom(o: JSONObject): Product = Product(
        id = o.getLong("id"),
        code = o.optString("code"),
        name = o.getString("name"),
        unit = o.getString("unit"),
        basePrice = BigDecimal(o.getString("basePrice")),
        minOrder = BigDecimal(o.optString("minOrder", "0")),
        setupFee = BigDecimal(o.optString("setupFee", "0")),
        rounding = BigDecimal(o.optString("rounding", "1")),
        options = (o.optJSONArray("options") ?: JSONArray()).objects().map { g ->
            OptionGroup(
                g.getLong("id"),
                g.getString("name"),
                (g.optJSONArray("choices") ?: JSONArray()).objects().map { c ->
                    val factor = c.optString("factor")
                    PriceChoice(c.getLong("id"), c.getString("name"), BigDecimal(c.optString("add", "0")), Coefficients.parse(factor).orEmpty(), factor)
                },
            )
        },
        tiers = (o.optJSONArray("tiers") ?: JSONArray()).objects().mapNotNull { t ->
            val factor = t.optString("factor", "1")
            val parsed = Coefficients.parse(factor) ?: return@mapNotNull null
            PriceTier(BigDecimal(t.getString("from")), Coefficients.product(parsed), factor)
        },
    )

    private fun rowsToJson(rows: List<List<String>>): JSONArray =
        JSONArray().also { array -> rows.forEach { row -> array.put(JSONArray(row)) } }

    private fun rowsFromJson(array: JSONArray?): List<List<String>> =
        if (array == null) emptyList()
        else (0 until array.length()).map { i ->
            val row = array.optJSONArray(i) ?: JSONArray()
            (0 until row.length()).map { j -> row.optString(j) }
        }

    fun sheetsToJson(s: CatalogSheets): JSONObject = JSONObject()
        .put("products", rowsToJson(s.products))
        .put("parameters", rowsToJson(s.parameters))
        .put("volume", rowsToJson(s.volume))
        .put("settings", rowsToJson(s.settings))
        .put("costs", rowsToJson(s.costs))
        .put("yarns", rowsToJson(s.yarns))
        .put("clients", rowsToJson(s.clients))

    fun sheetsFromJson(o: JSONObject): CatalogSheets = CatalogSheets(
        products = rowsFromJson(o.optJSONArray("products")),
        parameters = rowsFromJson(o.optJSONArray("parameters")),
        volume = rowsFromJson(o.optJSONArray("volume")),
        settings = rowsFromJson(o.optJSONArray("settings")),
        costs = rowsFromJson(o.optJSONArray("costs")),
        yarns = rowsFromJson(o.optJSONArray("yarns")),
        clients = rowsFromJson(o.optJSONArray("clients")),
    )

    companion object {
        private const val KEY_CATALOG_V2 = "catalog_v2"
        private const val KEY_SETTINGS = "settings"
        private const val KEY_DRAFT = "draft"
        private const val KEY_SYNC_URL = "sync_url"
        private const val KEY_SYNC_KEY = "sync_key"
        private const val KEY_SYNC_MANAGER = "sync_manager"
        private const val KEY_SHEET_CACHE = "sheet_cache"
        private const val KEY_ARCHIVE = "archive"
        private const val KEY_LOGO_VERSION = "logo_version"
        private const val KEY_CLIENTS = "clients"
        private const val MAX_ARCHIVE = 500

        fun draftToJson(d: QuoteDraft): String {
            val lines = JSONArray()
            d.lines.forEach { l ->
                val sel = JSONObject()
                l.selected.forEach { (g, c) -> sel.put(g.toString(), c) }
                lines.put(
                    JSONObject().put("id", l.id).put("productId", l.productId).put("selected", sel)
                        .put("quantity", l.quantity).put("discount", l.discount).put("photo", l.photoPath ?: "")
                        .put("photoFileId", l.photoFileId ?: ""),
                )
            }
            return JSONObject()
                .put("id", d.id).put("number", d.number).put("saved", d.saved)
                .put("clientCompany", d.clientCompany).put("clientContact", d.clientContact)
                .put("clientEmail", d.clientEmail).put("clientPhone", d.clientPhone).put("clientInn", d.clientInn)
                .put("clientKpp", d.clientKpp).put("clientAddress", d.clientAddress)
                .put("comment", d.comment).put("lines", lines)
                .put(
                    "snapshot",
                    JSONArray().apply {
                        d.snapshot.forEach { s ->
                            put(
                                JSONObject().put("name", s.name).put("qty", s.quantity.toPlainString()).put("unit", s.unit)
                                    .put("price", s.price.toPlainString()).put("total", s.total.toPlainString()),
                            )
                        }
                    },
                )
                .toString()
        }

        fun draftFromJson(raw: String): QuoteDraft? = try {
            val o = JSONObject(raw)
            val lines = o.optJSONArray("lines") ?: JSONArray()
            QuoteDraft(
                id = o.optString("id").ifBlank { java.util.UUID.randomUUID().toString() },
                number = o.optInt("number", 1),
                saved = o.optBoolean("saved", false),
                clientCompany = o.optString("clientCompany"),
                clientContact = o.optString("clientContact"),
                clientEmail = o.optString("clientEmail"),
                clientPhone = o.optString("clientPhone"),
                clientInn = o.optString("clientInn"),
                clientKpp = o.optString("clientKpp"),
                clientAddress = o.optString("clientAddress"),
                comment = o.optString("comment"),
                lines = (0 until lines.length()).mapNotNull { lines.optJSONObject(it) }.map { l ->
                    val sel = l.optJSONObject("selected") ?: JSONObject()
                    DraftLine(
                        id = l.getLong("id"),
                        productId = l.getLong("productId"),
                        selected = sel.keys().asSequence().associate { it.toLong() to sel.getLong(it) },
                        quantity = l.optString("quantity"),
                        discount = l.optString("discount"),
                        photoPath = l.optString("photo").ifBlank { null },
                        photoFileId = l.optString("photoFileId").ifBlank { null },
                    )
                },
                snapshot = (o.optJSONArray("snapshot") ?: JSONArray()).let { a ->
                    (0 until a.length()).mapNotNull { a.optJSONObject(it) }.map { s ->
                        SnapshotLine(
                            name = s.optString("name"),
                            quantity = java.math.BigDecimal(s.optString("qty", "0")),
                            unit = s.optString("unit"),
                            price = java.math.BigDecimal(s.optString("price", "0")),
                            total = java.math.BigDecimal(s.optString("total", "0")),
                        )
                    }
                },
            )
        } catch (e: Exception) {
            null
        }
    }
}
