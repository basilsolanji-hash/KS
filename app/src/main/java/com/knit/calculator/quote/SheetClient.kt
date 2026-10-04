package com.knit.calculator.quote

import com.knit.calculator.core.CatalogSheets
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder

/** Ошибка связи с таблицей; [message] понятно пользователю. */
class SheetException(message: String) : IOException(message)

data class RemoteCatalog(val name: String, val url: String, val sheets: CatalogSheets)

data class RemoteQuote(
    val number: Int,
    val date: String,
    val client: String,
    val total: Double,
    val author: String,
    val id: String,
    val data: String,
)

/**
 * Клиент веб-приложения Google Apps Script, привязанного к таблице (см. google-sheets/Code.gs).
 * Только стандартный HttpURLConnection — без сторонних библиотек.
 */
class SheetClient(private val config: SyncConfig) {

    suspend fun catalog(): RemoteCatalog {
        val o = get("catalog")
        val sheets = o.optJSONObject("sheets") ?: throw SheetException("Таблица не вернула справочники")
        return RemoteCatalog(
            name = o.optString("name"),
            url = o.optString("url"),
            sheets = CatalogSheets(
                products = rows(sheets.optJSONArray("products")),
                parameters = rows(sheets.optJSONArray("parameters")),
                volume = rows(sheets.optJSONArray("volume")),
                settings = rows(sheets.optJSONArray("settings")),
            ),
        )
    }

    suspend fun quotes(limit: Int = 50): List<RemoteQuote> {
        val array = get("quotes", "limit" to limit.toString()).optJSONArray("quotes") ?: JSONArray()
        return (0 until array.length()).mapNotNull { array.optJSONObject(it) }.map {
            RemoteQuote(
                number = it.optInt("number"),
                date = it.optString("date"),
                client = it.optString("client"),
                total = it.optDouble("total", 0.0),
                author = it.optString("author"),
                id = it.optString("id"),
                data = it.optString("data"),
            )
        }
    }

    /** Сохраняет КП и возвращает его номер (новый или прежний для того же ID). */
    suspend fun saveQuote(quote: JSONObject): Int {
        val body = JSONObject().put("action", "saveQuote").put("key", config.key).put("quote", quote)
        val number = request(URL(config.url.trim()), body.toString()).optInt("number", 0)
        if (number <= 0) throw SheetException("Таблица не выдала номер КП")
        return number
    }

    private suspend fun get(action: String, vararg params: Pair<String, String>): JSONObject {
        val query = (listOf("action" to action, "key" to config.key) + params)
            .joinToString("&") { (k, v) -> "$k=" + URLEncoder.encode(v, "UTF-8") }
        val base = config.url.trim()
        return request(URL(base + (if ('?' in base) "&" else "?") + query), null)
    }

    private suspend fun request(start: URL, postBody: String?): JSONObject = withContext(Dispatchers.IO) {
        if (start.protocol != "https") throw SheetException("Адрес должен начинаться с https://")
        var url = start
        var body = postBody
        repeat(MAX_REDIRECTS) {
            val conn = (url.openConnection() as HttpURLConnection).apply {
                instanceFollowRedirects = false
                connectTimeout = TIMEOUT_MS
                readTimeout = TIMEOUT_MS
                requestMethod = if (body != null) "POST" else "GET"
                setRequestProperty("Accept", "application/json")
            }
            try {
                if (body != null) {
                    conn.doOutput = true
                    conn.setRequestProperty("Content-Type", "text/plain; charset=utf-8")
                    conn.outputStream.use { it.write(body!!.toByteArray(Charsets.UTF_8)) }
                }
                val code = conn.responseCode
                if (code in 300..399) {
                    // Apps Script отвечает перенаправлением на googleusercontent.com — результат забираем GET-запросом.
                    val location = conn.getHeaderField("Location") ?: throw SheetException("Пустое перенаправление")
                    url = URL(url, location)
                    body = null
                    return@repeat
                }
                val text = (if (code < 400) conn.inputStream else conn.errorStream)?.bufferedReader()?.use { it.readText() }.orEmpty()
                if (code == 401 || code == 403) throw SheetException("Нет доступа к скрипту: в развертывании укажите доступ «Все»")
                if (code == 404) throw SheetException("Скрипт не найден: проверьте адрес …/exec")
                if (code >= 400) throw SheetException("Ошибка сервера Google ($code)")
                if (text.trimStart().startsWith("<")) {
                    throw SheetException("Google вернул страницу вместо данных: проверьте адрес и доступ «Все» в развертывании")
                }
                val json = try {
                    JSONObject(text)
                } catch (e: JSONException) {
                    throw SheetException("Некорректный ответ таблицы")
                }
                if (!json.optBoolean("ok")) throw SheetException(json.optString("error").ifBlank { "Ошибка таблицы" })
                return@withContext json
            } finally {
                conn.disconnect()
            }
        }
        throw SheetException("Слишком много перенаправлений")
    }

    private fun rows(array: JSONArray?): List<List<String>> =
        if (array == null) emptyList()
        else (0 until array.length()).map { i ->
            val row = array.optJSONArray(i) ?: JSONArray()
            (0 until row.length()).map { j -> row.opt(j)?.toString().orEmpty() }
        }

    private companion object {
        const val MAX_REDIRECTS = 5
        const val TIMEOUT_MS = 25_000
    }
}
