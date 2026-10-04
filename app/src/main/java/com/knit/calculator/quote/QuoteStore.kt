package com.knit.calculator.quote

import android.content.Context
import com.knit.calculator.core.CatalogSheets
import com.knit.calculator.core.Coefficients
import com.knit.calculator.core.OptionGroup
import com.knit.calculator.core.PriceChoice
import com.knit.calculator.core.PriceTier
import com.knit.calculator.core.Product
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal

/** Настройки подключения к Google Таблице (хранятся только на этом телефоне). */
data class SyncConfig(val url: String = "", val key: String = "", val manager: String = "") {
    val enabled: Boolean get() = url.isNotBlank()
}

/** Последняя загруженная из таблицы копия справочников — для работы без интернета. */
data class SheetCache(val sheets: CatalogSheets, val sheetUrl: String, val loadedAt: Long)

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
            )
        } catch (e: Exception) {
            CompanySettings()
        }
    }

    fun saveSettings(s: CompanySettings) {
        val o = JSONObject()
            .put("brand", s.brand).put("city", s.city).put("legalName", s.legalName).put("inn", s.inn)
            .put("phone", s.phone).put("email", s.email).put("website", s.website)
            .put("vatRate", s.vatRate).put("vatIncluded", s.vatIncluded)
            .put("validityDays", s.validityDays).put("leadTime", s.leadTime)
            .put("freeDeliveryFrom", s.freeDeliveryFrom)
            .put("terms", s.terms).put("signature", s.signature)
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

    fun loadSheetCache(): SheetCache? {
        val raw = prefs.getString(KEY_SHEET_CACHE, null) ?: return null
        return try {
            val o = JSONObject(raw)
            SheetCache(sheetsFromJson(o.getJSONObject("sheets")), o.optString("url"), o.optLong("loadedAt"))
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
        prefs.edit().putString(KEY_SHEET_CACHE, o.toString()).apply()
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

    fun sheetsFromJson(o: JSONObject): CatalogSheets = CatalogSheets(
        products = rowsFromJson(o.optJSONArray("products")),
        parameters = rowsFromJson(o.optJSONArray("parameters")),
        volume = rowsFromJson(o.optJSONArray("volume")),
        settings = rowsFromJson(o.optJSONArray("settings")),
    )

    companion object {
        private const val KEY_CATALOG_V2 = "catalog_v2"
        private const val KEY_SETTINGS = "settings"
        private const val KEY_DRAFT = "draft"
        private const val KEY_SYNC_URL = "sync_url"
        private const val KEY_SYNC_KEY = "sync_key"
        private const val KEY_SYNC_MANAGER = "sync_manager"
        private const val KEY_SHEET_CACHE = "sheet_cache"

        fun draftToJson(d: QuoteDraft): String {
            val lines = JSONArray()
            d.lines.forEach { l ->
                val sel = JSONObject()
                l.selected.forEach { (g, c) -> sel.put(g.toString(), c) }
                lines.put(JSONObject().put("id", l.id).put("productId", l.productId).put("selected", sel).put("quantity", l.quantity))
            }
            return JSONObject()
                .put("id", d.id).put("number", d.number).put("saved", d.saved)
                .put("clientCompany", d.clientCompany).put("clientContact", d.clientContact)
                .put("clientEmail", d.clientEmail).put("comment", d.comment).put("lines", lines)
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
                comment = o.optString("comment"),
                lines = (0 until lines.length()).mapNotNull { lines.optJSONObject(it) }.map { l ->
                    val sel = l.optJSONObject("selected") ?: JSONObject()
                    DraftLine(
                        id = l.getLong("id"),
                        productId = l.getLong("productId"),
                        selected = sel.keys().asSequence().associate { it.toLong() to sel.getLong(it) },
                        quantity = l.optString("quantity"),
                    )
                },
            )
        } catch (e: Exception) {
            null
        }
    }
}
