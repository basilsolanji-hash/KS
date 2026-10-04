package com.knit.calculator.quote

import com.knit.calculator.core.CatalogSheets
import com.knit.calculator.core.QuoteStatus
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

/** Логотип из папки Диска: [data] есть, только если версия изменилась. */
data class RemoteLogo(val version: String, val data: ByteArray?)

data class RemoteCatalog(
    val name: String,
    val url: String,
    val sheets: CatalogSheets,
    val logo: RemoteLogo? = null,
    /** Лист «Договор»: текст договора поставки (если лист есть). */
    val contract: List<List<String>> = emptyList(),
)

data class RemoteQuote(
    val number: Int,
    val date: String,
    val client: String,
    val total: Double,
    val author: String,
    val id: String,
    val data: String,
    val status: QuoteStatus = QuoteStatus.SENT,
    val profit: Double? = null,
    /** «2026-10» — для отчёта. */
    val month: String = "",
    /** Действительно до, мс (0 — неизвестно). */
    val validUntil: Long = 0,
    /** Изделие → сумма (из листа «Позиции КП»). */
    val products: Map<String, Double> = emptyMap(),
)

/**
 * Клиент веб-приложения Google Apps Script, привязанного к таблице (см. google-sheets/Code.gs).
 * Только стандартный HttpURLConnection — без сторонних библиотек.
 */
class SheetClient(private val config: SyncConfig) {

    suspend fun catalog(logoVersion: String = ""): RemoteCatalog {
        val o = get("catalog", "logoVersion" to logoVersion)
        val sheets = o.optJSONObject("sheets") ?: throw SheetException("Таблица не вернула справочники")
        return RemoteCatalog(
            name = o.optString("name"),
            url = o.optString("url"),
            sheets = CatalogSheets(
                products = rows(sheets.optJSONArray("products")),
                parameters = rows(sheets.optJSONArray("parameters")),
                volume = rows(sheets.optJSONArray("volume")),
                settings = rows(sheets.optJSONArray("settings")),
                costs = rows(sheets.optJSONArray("costs")),
                yarns = rows(sheets.optJSONArray("yarns")),
                clients = rows(sheets.optJSONArray("clients")),
            ),
            contract = rows(sheets.optJSONArray("contract")),
            logo = o.optJSONObject("logo")?.let { l ->
                RemoteLogo(l.optString("version"), l.optString("data").takeIf { it.isNotBlank() }?.let { android.util.Base64.decode(it, android.util.Base64.DEFAULT) })
            },
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
                status = QuoteStatus.from(it.optString("status")),
                profit = if (it.isNull("profit") || it.optString("profit").isBlank()) null else it.optDouble("profit"),
                month = it.optString("month"),
                validUntil = it.optLong("validUntil", 0),
                products = it.optJSONObject("products")?.let { p -> p.keys().asSequence().associateWith { k -> p.optDouble(k) } }.orEmpty(),
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

    /** Загружает фото образца, PDF КП или документ (kind = "doc") в папку Диска; возвращает id файла. */
    suspend fun uploadFile(
        kind: String, name: String, bytes: ByteArray, mime: String, quoteId: String? = null, invoiceNumber: Int? = null,
    ): String {
        val body = JSONObject().put("action", "uploadFile").put("key", config.key)
            .put("kind", kind).put("name", name).put("mime", mime)
            .put("data", android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP))
        if (quoteId != null) body.put("quoteId", quoteId)
        if (invoiceNumber != null) body.put("invoiceNumber", invoiceNumber)
        val id = request(URL(config.url.trim()), body.toString()).optJSONObject("file")?.optString("id").orEmpty()
        if (id.isBlank()) throw SheetException("Диск не вернул файл")
        return id
    }

    /**
     * Письмо клиенту с PDF — с аккаунта Google владельца таблицы (ответ и копия — на e-mail фабрики).
     * PDF заодно сохраняется в папку «КП (PDF)».
     */
    suspend fun sendEmail(
        quoteId: String, to: String, subject: String, text: String, fileName: String, pdf: ByteArray,
        kind: String = "quote", invoiceNumber: Int? = null,
    ) {
        val body = JSONObject().put("action", "sendEmail").put("key", config.key)
            .put("quoteId", quoteId).put("to", to).put("subject", subject).put("body", text).put("name", fileName)
            .put("kind", kind)
            .put("data", android.util.Base64.encodeToString(pdf, android.util.Base64.NO_WRAP))
        if (invoiceNumber != null) body.put("invoiceNumber", invoiceNumber)
        request(URL(config.url.trim()), body.toString())
    }

    // ---------- Учёт: счета, оплаты, заказы, склад пряжи ----------

    suspend fun ops(): JSONObject = get("ops").optJSONObject("ops") ?: JSONObject()

    /** Новый счёт; возвращает его номер. */
    suspend fun addInvoice(invoice: JSONObject): Int {
        val number = post("addInvoice", "invoice", invoice).optInt("number", 0)
        if (number <= 0) throw SheetException("Таблица не выдала номер счёта")
        return number
    }

    suspend fun addPayment(payment: JSONObject) { post("addPayment", "payment", payment) }

    suspend fun deletePayment(id: String) { post("deletePayment", "id", id) }

    suspend fun saveOrder(order: JSONObject) { post("saveOrder", "order", order) }

    suspend fun addYarnMoves(moves: JSONArray) { post("addYarnMoves", "moves", moves) }

    private suspend fun post(action: String, field: String, value: Any): JSONObject =
        request(URL(config.url.trim()), JSONObject().put("action", action).put("key", config.key).put(field, value).toString())

    /** Скачивает фото образца из папки Диска. */
    suspend fun getFile(fileId: String): ByteArray {
        val body = JSONObject().put("action", "getFile").put("key", config.key).put("fileId", fileId)
        val data = request(URL(config.url.trim()), body.toString()).optString("data")
        return android.util.Base64.decode(data, android.util.Base64.DEFAULT)
    }

    /** Меняет статус КП в листе «КП». */
    suspend fun setStatus(id: String, status: QuoteStatus) {
        val body = JSONObject().put("action", "setStatus").put("key", config.key).put("id", id).put("status", status.title)
        request(URL(config.url.trim()), body.toString())
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
