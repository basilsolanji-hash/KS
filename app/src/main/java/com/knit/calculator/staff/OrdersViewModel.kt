package com.knit.calculator.staff

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

data class MsOrderRow(
    val id: String, val name: String, val moment: String, val agent: String, val organization: String,
    val sum: Double, val payed: Double, val shipped: Double, val state: String, val stateColor: Int,
)

data class MsPosition(
    val id: String, val name: String, val assortmentId: String, val assortmentType: String, val code: String,
    val quantity: Double, val price: Double, val discount: Double, val vat: Int, val shipped: Double,
) {
    val total: Double get() = quantity * price * (1 - discount / 100)
}

data class MsState(val id: String, val name: String)

data class MsRelated(val type: String, val id: String, val name: String, val moment: String, val sum: Double, val applicable: Boolean)

data class MsOrderCard(
    val header: JSONObject, val positions: List<MsPosition>, val states: List<MsState>, val related: List<MsRelated>,
)

data class MsDocCard(val header: JSONObject, val positions: List<MsPosition>)

data class MsTemplate(val kind: String, val id: String, val name: String)

data class MsAssortmentRow(val id: String, val type: String, val name: String, val code: String, val price: Double, val stock: Double?)

/** Названия документов МойСклад. */
val MS_DOC_TITLES = mapOf(
    "customerorder" to "Заказ покупателя", "demand" to "Отгрузка", "invoiceout" to "Счёт покупателю", "paymentin" to "Входящий платёж",
    "cashin" to "Приходный ордер", "factureout" to "Счёт-фактура", "salesreturn" to "Возврат покупателя",
)

/** Заказы МойСклад через сервер фабрики: список, карточка, правка, связанные документы, печать. */
class OrdersViewModel(application: Application) : AndroidViewModel(application) {
    private val store = ServerStore(application)
    private fun client() = ServerClient(store.url, store.token)
    val connected: Boolean get() = store.connected

    private val _orders = MutableStateFlow<List<MsOrderRow>?>(null)
    val orders: StateFlow<List<MsOrderRow>?> = _orders.asStateFlow()
    private val _card = MutableStateFlow<MsOrderCard?>(null)
    val card: StateFlow<MsOrderCard?> = _card.asStateFlow()
    private val _doc = MutableStateFlow<MsDocCard?>(null)
    val doc: StateFlow<MsDocCard?> = _doc.asStateFlow()
    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy.asStateFlow()
    private val _message = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = _message.asStateFlow()
    private val _found = MutableStateFlow<List<MsAssortmentRow>>(emptyList())
    val found: StateFlow<List<MsAssortmentRow>> = _found.asStateFlow()

    fun clearMessage() { _message.value = null }

    private fun run(block: suspend (ServerClient) -> Unit) {
        if (!store.connected) { _message.value = "Сначала подключитесь к серверу фабрики"; return }
        _busy.value = true
        viewModelScope.launch {
            try {
                block(client())
            } catch (e: ServerException) {
                _message.value = e.message
            } finally {
                _busy.value = false
            }
        }
    }

    private var searchJob: Job? = null

    /** Список с поиском (по номеру, контрагенту); набор текста — с паузой 0,4 с. */
    fun load(search: String = "") {
        searchJob?.cancel()
        searchJob = viewModelScope.launch {
            if (search.isNotEmpty()) delay(400)
            run { c ->
                val a = c.call("msOrders", JSONObject().put("search", search).put("limit", 100)).optJSONArray("orders") ?: JSONArray()
                _orders.value = a.objs().map {
                    MsOrderRow(
                        it.optString("id"), it.optString("name"), it.optString("moment"), it.optString("agent"), it.optString("organization"),
                        it.optDouble("sum"), it.optDouble("payed"), it.optDouble("shipped"), it.optString("state"), it.optInt("stateColor"),
                    )
                }
            }
        }
    }

    fun open(id: String) {
        _card.value = null
        run { c -> _card.value = parseCard(c.call("msOrder", JSONObject().put("id", id))) }
    }

    fun close() { _card.value = null }

    private fun parseCard(r: JSONObject) = MsOrderCard(
        r.optJSONObject("order") ?: JSONObject(),
        positions(r.optJSONArray("positions")),
        (r.optJSONArray("states") ?: JSONArray()).objs().map { MsState(it.optString("id"), it.optString("name")) },
        (r.optJSONArray("related") ?: JSONArray()).objs().map {
            MsRelated(it.optString("type"), it.optString("id"), it.optString("name"), it.optString("moment"), it.optDouble("sum"), it.optBoolean("applicable", true))
        },
    )

    /** Сохранить шапку и позиции; [onDone] — после ответа сервера. */
    fun save(id: String, description: String, address: String, stateId: String, positions: List<MsPosition>, onDone: () -> Unit) = run { c ->
        val a = JSONArray()
        positions.forEach {
            val o = JSONObject().put("assortmentId", it.assortmentId).put("assortmentType", it.assortmentType)
                .put("quantity", it.quantity).put("price", it.price).put("discount", it.discount)
            if (it.id.isNotEmpty()) o.put("id", it.id)
            if (it.vat > 0) o.put("vat", it.vat)
            a.put(o)
        }
        val body = JSONObject().put("id", id).put("description", description).put("shipmentAddress", address).put("positions", a)
        if (stateId.isNotEmpty()) body.put("stateId", stateId)
        _card.value = parseCard(c.call("msOrderSave", body))
        _message.value = "Заказ сохранён в МойСклад"
        StaffEvents.log("order", _card.value?.header?.optString("name").orEmpty())
        onDone()
    }

    fun openDoc(type: String, id: String) {
        _doc.value = null
        run { c ->
            val r = c.call("msDoc", JSONObject().put("type", type).put("id", id))
            _doc.value = MsDocCard(r.optJSONObject("doc") ?: JSONObject(), positions(r.optJSONArray("positions")))
        }
    }

    fun closeDoc() { _doc.value = null }

    fun saveDoc(type: String, id: String, description: String, purpose: String?, applicable: Boolean?) = run { c ->
        val body = JSONObject().put("type", type).put("id", id).put("description", description)
        purpose?.let { body.put("paymentPurpose", it) }
        applicable?.let { body.put("applicable", it) }
        val r = c.call("msDocSave", body)
        _doc.value = MsDocCard(r.optJSONObject("doc") ?: JSONObject(), positions(r.optJSONArray("positions")))
        _message.value = "Документ сохранён"
    }

    fun templates(type: String, onResult: (List<MsTemplate>) -> Unit) = run { c ->
        onResult((c.call("msTemplates", JSONObject().put("type", type)).optJSONArray("templates") ?: JSONArray()).objs().map {
            MsTemplate(it.optString("kind"), it.optString("id"), it.optString("name"))
        })
    }

    /** PDF печатной формы — в кэш телефона (для «Поделиться» и печати). */
    fun pdf(type: String, id: String, t: MsTemplate, fileName: String, onFile: (File) -> Unit) = run { c ->
        val r = c.call("msPrint", JSONObject().put("type", type).put("id", id).put("kind", t.kind).put("template", t.id).put("fileName", fileName))
        val bytes = android.util.Base64.decode(r.optString("pdf"), android.util.Base64.DEFAULT)
        val dir = File(getApplication<Application>().cacheDir, "reports").apply { mkdirs() }
        val file = File(dir, r.optString("name", "document.pdf"))
        kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { file.writeBytes(bytes) }
        onFile(file)
    }

    fun search(text: String) {
        searchJob?.cancel()
        if (text.trim().length < 2) { _found.value = emptyList(); return }
        searchJob = viewModelScope.launch {
            delay(400)
            runCatching { client().call("msAssortment", JSONObject().put("search", text.trim())) }.getOrNull()?.let { r ->
                _found.value = (r.optJSONArray("rows") ?: JSONArray()).objs().map {
                    MsAssortmentRow(it.optString("id"), it.optString("type"), it.optString("name"), it.optString("code"), it.optDouble("price"),
                        if (it.isNull("stock") || !it.has("stock")) null else it.optDouble("stock"))
                }
            }
        }
    }

    private fun positions(a: JSONArray?) = (a ?: JSONArray()).objs().map {
        MsPosition(
            it.optString("id"), it.optString("name"), it.optString("assortmentId"), it.optString("assortmentType", "product"), it.optString("code"),
            it.optDouble("quantity"), it.optDouble("price"), it.optDouble("discount"), it.optInt("vat"), it.optDouble("shipped"),
        )
    }
}

private fun JSONArray.objs(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }
