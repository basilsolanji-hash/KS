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

/** Ошибка связи с таблицей; [message] понятно пользователю. */
class SheetException(message: String) : IOException(message)

/** Ответ МойСклад на запись: номер документа или ошибка. */
data class MsResult(val name: String, val error: String?) {
    companion object {
        fun from(o: JSONObject?): MsResult? = o?.let { MsResult(it.optString("name"), it.optString("error").ifBlank { null }) }
    }
}

/** Логотип из папки Диска: [data] есть, только если версия изменилась. */
data class RemoteLogo(val version: String, val data: ByteArray?)

data class RemoteCatalog(
    val name: String,
    val url: String,
    val sheets: CatalogSheets,
    val logo: RemoteLogo? = null,
    /** Лист «Договор»: текст договора поставки (если лист есть). */
    val contract: List<List<String>> = emptyList(),
    /** Роль по ключу: «director» или «manager» (себестоимость и прибыль — только директору). */
    val role: String = "director",
    /** Имя менеджера из листа «Менеджеры» (у личного ключа). */
    val manager: String = "",
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
            role = o.optString("role").ifBlank { "director" },
            manager = o.optString("manager"),
            logo = o.optJSONObject("logo")?.let { l ->
                RemoteLogo(l.optString("version"), l.optString("data").takeIf { it.isNotBlank() }?.let { android.util.Base64.decode(it, android.util.Base64.DEFAULT) })
            },
        )
    }

    /** КП: последние [limit]; [month] — все КП месяца («2026-10»); [light] — все КП без данных черновика. */
    suspend fun quotes(limit: Int = 50, month: String = "", light: Boolean = false): List<RemoteQuote> =
        parseQuotes(quotesJson(limit, month, light))

    /** Сырой ответ «quotes» — его можно сохранить на телефоне и разобрать позже ([parseQuotes]). */
    suspend fun quotesJson(limit: Int = 50, month: String = "", light: Boolean = false): JSONArray =
        get("quotes", "limit" to limit.toString(), "month" to month, "light" to (if (light) "1" else ""))
            .optJSONArray("quotes") ?: JSONArray()

    /**
     * Сохраняет КП и возвращает номер (новый или прежний для того же ID) и ответ МойСклад
     * (`null` — МойСклад не подключён; ошибка МойСклад не мешает сохранению в таблицу).
     */
    suspend fun saveQuote(quote: JSONObject): Pair<Int, MsResult?> {
        val body = JSONObject().put("action", "saveQuote").put("key", config.key).put("quote", quote)
        val response = request(URL(config.url.trim()), body.toString())
        val number = response.optInt("number", 0)
        if (number <= 0) throw SheetException("Таблица не выдала номер КП")
        return number to MsResult.from(response.optJSONObject("ms"))
    }

    /** Товары, остатки и клиенты МойСклад (через скрипт таблицы); `null` — МойСклад не подключён. */
    suspend fun msCatalog(fresh: Boolean = false): JSONObject? =
        get("msCatalog", "fresh" to if (fresh) "1" else "").optJSONObject("ms")?.takeIf { it.optBoolean("enabled") }

    /** Зарплата за месяц (yyyy-MM): директору — все менеджеры, менеджеру — только он. */
    suspend fun salary(month: String = ""): JSONObject = get("salary", "month" to month).optJSONObject("salary") ?: JSONObject()

    /** Платёжный календарь: остаток, регулярные платежи, счета поставщиков, факт (только директору). */
    suspend fun finance(fresh: Boolean = false): JSONObject =
        get("finance", "fresh" to if (fresh) "1" else "").optJSONObject("finance") ?: JSONObject()

    /** Реквизиты организации по ИНН (DaData через скрипт таблицы). */
    suspend fun innLookup(inn: String): JSONObject =
        get("innLookup", "inn" to inn).optJSONObject("party") ?: throw SheetException("DaData не вернула реквизиты")

    /** EAN-13 товара или модификации МойСклад; если штрихкода нет — скрипт создаёт его в МойСклад. */
    /** Правка карточки товара МойСклад (директор): название, артикул, описание, вес, мин. цена, цены по тиражам. */
    suspend fun msUpdateProduct(type: String, id: String, changes: JSONObject): JSONObject {
        val body = JSONObject().put("action", "msUpdateProduct").put("key", config.key)
            .put("msType", type).put("msId", id).put("changes", changes)
        return request(URL(config.url.trim()), body.toString())
    }

    suspend fun msBarcode(type: String, id: String): String {
        val code = get("msBarcode", "msType" to type, "msId" to id).optString("barcode")
        if (!com.knit.calculator.core.Ean13.isValid(code)) throw SheetException("МойСклад не вернул штрихкод")
        return code
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

    /** Меняет статус КП в листе «КП» (и в «Заказе покупателя» МойСклад). */
    suspend fun setStatus(id: String, status: QuoteStatus): MsResult? {
        val body = JSONObject().put("action", "setStatus").put("key", config.key).put("id", id).put("status", status.title)
        return MsResult.from(request(URL(config.url.trim()), body.toString()).optJSONObject("ms"))
    }

    /** Чтение — тоже POST: ключ доступа не попадает в адрес запроса (журналы, история). */
    private suspend fun get(action: String, vararg params: Pair<String, String>): JSONObject {
        val body = JSONObject().put("action", action).put("key", config.key)
        params.filter { it.second.isNotEmpty() }.forEach { (k, v) -> body.put(k, v) }
        return request(URL(config.url.trim()), body.toString())
    }

    private suspend fun request(start: URL, postBody: String?): JSONObject = withContext(Dispatchers.IO) {
        if (start.protocol != "https") throw SheetException("Адрес должен начинаться с https://")
        var url = start
        // Просим сжатый ответ: большие справочники и каталог МойСклад приходят в 5–10 раз меньше.
        var body = postBody?.let { b -> if (b.startsWith("{") && b.length > 2) "{\"gz\":true," + b.substring(1) else b }
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
                    JSONObject(text).let { o -> if (o.has("gz")) JSONObject(gunzip(o.optString("gz"))) else o }
                } catch (e: JSONException) {
                    throw SheetException("Некорректный ответ таблицы")
                } catch (e: java.io.IOException) {
                    throw SheetException("Некорректный сжатый ответ таблицы")
                }
                if (!json.optBoolean("ok")) throw SheetException(json.optString("error").ifBlank { "Ошибка таблицы" })
                return@withContext json
            } finally {
                // Без disconnect(): соединение остаётся открытым и следующий запрос не тратит время на TLS.
                runCatching { conn.inputStream.close() }
            }
        }
        throw SheetException("Слишком много перенаправлений")
    }

    private fun gunzip(base64: String): String {
        val bytes = android.util.Base64.decode(base64, android.util.Base64.DEFAULT)
        return java.util.zip.GZIPInputStream(bytes.inputStream()).bufferedReader(Charsets.UTF_8).use { it.readText() }
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

/** Разбор списка КП из ответа таблицы. */
fun parseQuotes(array: JSONArray): List<RemoteQuote> {
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
