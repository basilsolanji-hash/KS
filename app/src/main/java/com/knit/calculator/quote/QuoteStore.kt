package com.knit.calculator.quote

import android.content.Context
import com.knit.calculator.core.DiscountTier
import com.knit.calculator.core.OptionGroup
import com.knit.calculator.core.PriceChoice
import com.knit.calculator.core.Product
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.math.BigDecimal

/** Ассортимент, реквизиты и черновик КП хранятся локально в JSON (SharedPreferences). */
class QuoteStore(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("quote", Context.MODE_PRIVATE)

    fun loadCatalog(): List<Product> {
        val raw = prefs.getString(KEY_CATALOG, null) ?: return DefaultCatalog.products
        return try {
            JSONArray(raw).objects().map(::productFrom)
        } catch (e: Exception) {
            DefaultCatalog.products
        }
    }

    fun saveCatalog(products: List<Product>) {
        val array = JSONArray()
        products.forEach { array.put(productTo(it)) }
        prefs.edit().putString(KEY_CATALOG, array.toString()).apply()
    }

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
                terms = o.optString("terms", d.terms),
                signature = o.optString("signature", d.signature),
            )
        } catch (e: JSONException) {
            CompanySettings()
        }
    }

    fun saveSettings(s: CompanySettings) {
        val o = JSONObject()
            .put("brand", s.brand).put("city", s.city).put("legalName", s.legalName).put("inn", s.inn)
            .put("phone", s.phone).put("email", s.email).put("website", s.website)
            .put("vatRate", s.vatRate).put("vatIncluded", s.vatIncluded)
            .put("validityDays", s.validityDays).put("leadTime", s.leadTime)
            .put("terms", s.terms).put("signature", s.signature)
        prefs.edit().putString(KEY_SETTINGS, o.toString()).apply()
    }

    fun loadDraft(): QuoteDraft {
        val raw = prefs.getString(KEY_DRAFT, null) ?: return QuoteDraft()
        return try {
            val o = JSONObject(raw)
            QuoteDraft(
                number = o.optInt("number", 1),
                clientCompany = o.optString("clientCompany"),
                clientContact = o.optString("clientContact"),
                clientEmail = o.optString("clientEmail"),
                comment = o.optString("comment"),
                lines = (o.optJSONArray("lines") ?: JSONArray()).objects().map { l ->
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
            QuoteDraft()
        }
    }

    fun saveDraft(d: QuoteDraft) {
        val lines = JSONArray()
        d.lines.forEach { l ->
            val sel = JSONObject()
            l.selected.forEach { (g, c) -> sel.put(g.toString(), c) }
            lines.put(JSONObject().put("id", l.id).put("productId", l.productId).put("selected", sel).put("quantity", l.quantity))
        }
        val o = JSONObject()
            .put("number", d.number).put("clientCompany", d.clientCompany).put("clientContact", d.clientContact)
            .put("clientEmail", d.clientEmail).put("comment", d.comment).put("lines", lines)
        prefs.edit().putString(KEY_DRAFT, o.toString()).apply()
    }

    private fun JSONArray.objects(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }

    private fun productTo(p: Product): JSONObject {
        val options = JSONArray()
        p.options.forEach { g ->
            val choices = JSONArray()
            g.choices.forEach { c -> choices.put(JSONObject().put("id", c.id).put("name", c.name).put("add", c.priceAdd.toPlainString())) }
            options.put(JSONObject().put("id", g.id).put("name", g.name).put("choices", choices))
        }
        val tiers = JSONArray()
        p.tiers.forEach { t -> tiers.put(JSONObject().put("from", t.fromQuantity.toPlainString()).put("percent", t.percent.toPlainString())) }
        return JSONObject()
            .put("id", p.id).put("name", p.name).put("unit", p.unit)
            .put("basePrice", p.basePrice.toPlainString()).put("minOrder", p.minOrder.toPlainString())
            .put("setupFee", p.setupFee.toPlainString())
            .put("options", options).put("tiers", tiers)
    }

    private fun productFrom(o: JSONObject): Product = Product(
        id = o.getLong("id"),
        name = o.getString("name"),
        unit = o.getString("unit"),
        basePrice = BigDecimal(o.getString("basePrice")),
        minOrder = BigDecimal(o.optString("minOrder", "0")),
        setupFee = BigDecimal(o.optString("setupFee", "0")),
        options = (o.optJSONArray("options") ?: JSONArray()).objects().map { g ->
            OptionGroup(
                g.getLong("id"),
                g.getString("name"),
                (g.optJSONArray("choices") ?: JSONArray()).objects().map { c ->
                    PriceChoice(c.getLong("id"), c.getString("name"), BigDecimal(c.optString("add", "0")))
                },
            )
        },
        tiers = (o.optJSONArray("tiers") ?: JSONArray()).objects().map { t ->
            DiscountTier(BigDecimal(t.getString("from")), BigDecimal(t.getString("percent")))
        },
    )

    private companion object {
        const val KEY_CATALOG = "catalog"
        const val KEY_SETTINGS = "settings"
        const val KEY_DRAFT = "draft"
    }
}
