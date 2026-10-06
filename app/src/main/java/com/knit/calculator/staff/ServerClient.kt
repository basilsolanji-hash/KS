package com.knit.calculator.staff

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

class ServerException(message: String) : Exception(message)

/** Подключение к серверу фабрики на этом телефоне: адрес и токен входа (ключ сотрудника не хранится). */
class ServerStore(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences("staff", Context.MODE_PRIVATE)

    var url: String
        get() = prefs.getString("url", null) ?: DEFAULT_URL
        set(v) { prefs.edit().putString("url", v).apply() }

    /** Токен телефона — зашифрован ключом Android Keystore. */
    var token: String?
        get() = com.knit.calculator.quote.Secrets.decrypt(prefs.getString("token", null)).ifEmpty { null }
        set(v) { prefs.edit().putString("token", v?.let { com.knit.calculator.quote.Secrets.encrypt(it) }).apply() }

    /** Последний ответ «me» — чтобы меню по роли было видно сразу, без сети. */
    var me: String?
        get() = prefs.getString("me", null)
        set(v) { prefs.edit().putString("me", v).apply() }

    val connected: Boolean get() = !token.isNullOrBlank()

    fun clear() = prefs.edit().remove("token").remove("me").apply()

    companion object {
        const val DEFAULT_URL = "https://staff.fabrika-ks.ru/"
    }
}

/** API сервера staff.fabrika-ks.ru: POST JSON {action, token, …} → {ok, …}. Только HTTPS. */
class ServerClient(private val url: String, private val token: String?) {
    suspend fun call(action: String, body: JSONObject = JSONObject()): JSONObject = withContext(Dispatchers.IO) {
        if (!url.startsWith("https://")) throw ServerException("Адрес сервера должен начинаться с https://")
        body.put("action", action)
        token?.let { body.put("token", it) }
        val conn = URL(url).openConnection() as HttpURLConnection
        try {
            conn.requestMethod = "POST"
            conn.connectTimeout = 15_000
            conn.readTimeout = 25_000
            conn.doOutput = true
            conn.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            conn.outputStream.use { it.write(body.toString().toByteArray()) }
            val code = conn.responseCode
            val text = (if (code >= 400) conn.errorStream else conn.inputStream)?.bufferedReader()?.use { it.readText() }.orEmpty()
            val json = runCatching { JSONObject(text) }.getOrNull() ?: throw ServerException("Сервер недоступен ($code)")
            if (!json.optBoolean("ok")) throw ServerException(json.optString("error").ifBlank { "Ошибка сервера" })
            json
        } catch (e: ServerException) {
            throw e
        } catch (e: java.io.IOException) {
            throw ServerException("Нет связи с сервером фабрики")
        } finally {
            conn.disconnect()
        }
    }
}

/** Роль и этапы текущего сотрудника (ответ «me»). */
data class StaffMe(
    val id: Int,
    val name: String,
    val role: String,
    val roles: Map<String, String>,
    /** Этапы: ключ, название, можно ли вести этот этап. */
    val stages: List<Triple<String, String, Boolean>>,
    /** Учитывается активность в приложении (офис, рабочее время). */
    val tracked: Boolean = false,
    /** Соглашения приняты (иначе — экран соглашений перед работой). */
    val legalOk: Boolean = true,
) {
    val director: Boolean get() = role == "director"
    val full: Boolean get() = role == "director" || role == "assistant"
    /** Сотрудник производства: в меню только производство, время, рейтинг. */
    val production: Boolean get() = role in setOf("designer", "operator", "handwork")
    /** Товаровед: товары МойСклад, склад, этикетки — без финансов и КП. */
    val merch: Boolean get() = role == "merch"
    val roleTitle: String get() = roles[role] ?: role

    companion object {
        fun parse(o: JSONObject): StaffMe {
            val me = o.getJSONObject("me")
            val roles = o.optJSONObject("roles")?.let { r -> r.keys().asSequence().associateWith { r.optString(it) } }.orEmpty()
            val stages = o.optJSONArray("stages") ?: JSONArray()
            return StaffMe(
                me.optInt("id"), me.optString("name"), me.optString("role"), roles,
                (0 until stages.length()).map { stages.getJSONObject(it) }.map { Triple(it.optString("key"), it.optString("name"), it.optBoolean("mine")) },
                o.optBoolean("tracked"),
                o.optBoolean("legalOk", true),
            )
        }
    }
}
