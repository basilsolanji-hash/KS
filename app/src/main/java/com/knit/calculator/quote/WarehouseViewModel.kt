package com.knit.calculator.quote

import android.app.Application
import android.content.Context
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.knit.calculator.core.Product
import com.knit.calculator.core.ShipLine
import com.knit.calculator.core.Warehouse
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal

/** Заказ к отгрузке. */
data class ShipOrder(val id: String, val name: String, val client: String, val moment: Long, val sum: BigDecimal, val shipped: BigDecimal)

/** Документ к приёмке: черновик приёмки или заказ поставщику. */
data class ReceiveDoc(val type: String, val id: String, val name: String, val supplier: String, val moment: Long, val sum: BigDecimal)

/** Посчитанный при инвентаризации товар. */
data class CountItem(val id: String, val type: String, val name: String, val qty: BigDecimal, val stock: BigDecimal?)

/**
 * Склад по сканеру: отгрузка заказа МойСклад (сверка с заказом) и инвентаризация.
 * Сканы отгрузки, приёмки и инвентаризации сохраняются на телефоне — подсчёт не теряется, если приложение закрылось.
 * У каждой операции свой номер (opId): повтор после обрыва связи не создаёт второй документ в МойСклад.
 */
class WarehouseViewModel(application: Application) : AndroidViewModel(application) {
    private val store = QuoteStore(application)
    private val prefs = application.getSharedPreferences("warehouse", Context.MODE_PRIVATE)

    private val _orders = MutableStateFlow<List<ShipOrder>?>(null)
    val orders: StateFlow<List<ShipOrder>?> = _orders.asStateFlow()
    private val _order = MutableStateFlow<ShipOrder?>(null)
    val order: StateFlow<ShipOrder?> = _order.asStateFlow()
    private val _lines = MutableStateFlow<List<ShipLine>>(emptyList())
    val lines: StateFlow<List<ShipLine>> = _lines.asStateFlow()
    private val _count = MutableStateFlow(loadCount())
    val count: StateFlow<List<CountItem>> = _count.asStateFlow()
    private val _loading = MutableStateFlow(false)
    val loading: StateFlow<Boolean> = _loading.asStateFlow()
    private val _message = MutableStateFlow<String?>(null)
    /** Последнее событие для менеджера: «Отгрузка № … создана», ошибка, «нет в заказе». */
    val message: StateFlow<String?> = _message.asStateFlow()

    fun clearMessage() { _message.value = null }

    private fun client(): SheetClient? = store.loadSyncConfig().takeIf { it.enabled }?.let(::SheetClient)

    private fun money(v: Any?) = v?.toString()?.toBigDecimalOrNull() ?: BigDecimal.ZERO

    private fun JSONArray.objects(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }

    fun loadOrders() {
        val c = client() ?: run { _message.value = "Нужно подключение к Google Таблице и МойСклад"; return }
        _loading.value = true
        viewModelScope.launch {
            try {
                _orders.value = c.msShipList().objects().map {
                    ShipOrder(it.optString("id"), it.optString("name"), it.optString("client"), it.optLong("moment"), money(it.opt("sum")), money(it.opt("shipped")))
                }
            } catch (e: Exception) {
                _message.value = e.message ?: "МойСклад недоступен"
            } finally {
                _loading.value = false
            }
        }
    }

    fun openOrder(o: ShipOrder) {
        val c = client() ?: return
        _order.value = o
        _lines.value = emptyList()
        _loading.value = true
        viewModelScope.launch {
            try {
                _lines.value = restoreScans("ship:${o.id}", c.msShipOrder(o.id).objects().map {
                    ShipLine(
                        it.optString("id"), it.optString("type"), it.optString("name"), it.optString("article"), it.optString("barcode"),
                        money(it.opt("quantity")), money(it.opt("shipped")),
                    )
                })
            } catch (e: Exception) {
                _message.value = e.message ?: "МойСклад недоступен"
            } finally {
                _loading.value = false
            }
        }
    }

    fun closeOrder() {
        _order.value = null
        _lines.value = emptyList()
    }

    /** Код со сканера в отгрузке: +1 к строке заказа. */
    fun scanShip(code: String, catalog: List<Product>) {
        when (val r = Warehouse.match(_lines.value, code, catalog)) {
            is com.knit.calculator.core.ScanResult.Matched -> {
                _lines.value = Warehouse.add(_lines.value, r.index)
                _order.value?.let { saveScans("ship:${it.id}", _lines.value) }
                val l = _lines.value[r.index]
                _message.value = if (l.over) "Лишнее: ${l.name} — ${fmt(l.scanned)} из ${fmt(l.remaining)}" else "${l.name}: ${fmt(l.scanned)} из ${fmt(l.remaining)}"
            }
            is com.knit.calculator.core.ScanResult.NotInOrder -> _message.value = "Нет в заказе: ${r.name}"
            com.knit.calculator.core.ScanResult.Unknown -> _message.value = "Штрихкод $code не найден"
        }
    }

    fun adjustShip(index: Int, step: Int) {
        _lines.value = Warehouse.add(_lines.value, index, BigDecimal(step))
        _order.value?.let { saveScans("ship:${it.id}", _lines.value) }
    }

    fun ship(onDone: () -> Unit) {
        val c = client() ?: return
        val o = _order.value ?: return
        val items = JSONArray()
        _lines.value.filter { it.scanned.signum() > 0 }.forEach { items.put(JSONObject().put("id", it.id).put("qty", it.scanned.toDouble())) }
        _loading.value = true
        viewModelScope.launch {
            try {
                val d = c.msShip(o.id, items, opId("ship:${o.id}"))
                forget("ship:${o.id}")
                com.knit.calculator.staff.StaffEvents.log("ship", "${o.name} · ${o.client}")
                _message.value = "Отгрузка № ${d.optString("name")} создана в МойСклад"
                closeOrder()
                loadOrders()
                onDone()
            } catch (e: Exception) {
                _message.value = e.message ?: "МойСклад недоступен"
            } finally {
                _loading.value = false
            }
        }
    }

    // ---------- Приёмка ----------

    private val _receiveDocs = MutableStateFlow<List<ReceiveDoc>?>(null)
    val receiveDocs: StateFlow<List<ReceiveDoc>?> = _receiveDocs.asStateFlow()
    private val _receiveDoc = MutableStateFlow<ReceiveDoc?>(null)
    val receiveDoc: StateFlow<ReceiveDoc?> = _receiveDoc.asStateFlow()
    private val _receiveLines = MutableStateFlow<List<ShipLine>>(emptyList())
    val receiveLines: StateFlow<List<ShipLine>> = _receiveLines.asStateFlow()

    fun loadReceiveDocs() {
        val c = client() ?: run { _message.value = "Нужно подключение к Google Таблице и МойСклад"; return }
        _loading.value = true
        viewModelScope.launch {
            try {
                _receiveDocs.value = c.msReceiveList().objects().map {
                    ReceiveDoc(it.optString("type"), it.optString("id"), it.optString("name"), it.optString("supplier"), it.optLong("moment"), money(it.opt("sum")))
                }
            } catch (e: Exception) {
                _message.value = e.message ?: "МойСклад недоступен"
            } finally {
                _loading.value = false
            }
        }
    }

    fun openReceive(d: ReceiveDoc) {
        val c = client() ?: return
        _receiveDoc.value = d
        _receiveLines.value = emptyList()
        _loading.value = true
        viewModelScope.launch {
            try {
                _receiveLines.value = restoreScans("recv:${d.type}:${d.id}", c.msReceiveDoc(d.type, d.id).objects().map {
                    ShipLine(
                        it.optString("id"), it.optString("type"), it.optString("name"), it.optString("article"), it.optString("barcode"),
                        money(it.opt("quantity")), money(it.opt("shipped")),
                    )
                })
            } catch (e: Exception) {
                _message.value = e.message ?: "МойСклад недоступен"
            } finally {
                _loading.value = false
            }
        }
    }

    fun closeReceive() {
        _receiveDoc.value = null
        _receiveLines.value = emptyList()
    }

    fun scanReceive(code: String, catalog: List<Product>) {
        when (val r = Warehouse.match(_receiveLines.value, code, catalog)) {
            is com.knit.calculator.core.ScanResult.Matched -> {
                _receiveLines.value = Warehouse.add(_receiveLines.value, r.index)
                _receiveDoc.value?.let { saveScans("recv:${it.type}:${it.id}", _receiveLines.value) }
                val l = _receiveLines.value[r.index]
                _message.value = if (l.over) "Больше, чем ждём: ${l.name} — ${fmt(l.scanned)} из ${fmt(l.remaining)}" else "${l.name}: ${fmt(l.scanned)} из ${fmt(l.remaining)}"
            }
            is com.knit.calculator.core.ScanResult.NotInOrder -> _message.value = "Нет в документе: ${r.name}"
            com.knit.calculator.core.ScanResult.Unknown -> _message.value = "Штрихкод $code не найден"
        }
    }

    fun adjustReceive(index: Int, step: Int) {
        _receiveLines.value = Warehouse.add(_receiveLines.value, index, BigDecimal(step))
        _receiveDoc.value?.let { saveScans("recv:${it.type}:${it.id}", _receiveLines.value) }
    }

    /** Провести приёмку: количество — как отсканировали (лишнее поставщика тоже принимается, если подтвердили). */
    fun receive() {
        val c = client() ?: return
        val d = _receiveDoc.value ?: return
        val items = JSONArray()
        _receiveLines.value.filter { it.scanned.signum() > 0 }.forEach { items.put(JSONObject().put("id", it.id).put("qty", it.scanned.toDouble())) }
        _loading.value = true
        viewModelScope.launch {
            try {
                val r = c.msReceive(d.type, d.id, items, opId("recv:${d.type}:${d.id}"))
                forget("recv:${d.type}:${d.id}")
                com.knit.calculator.staff.StaffEvents.log("receive", "${d.name} · ${d.supplier}")
                _message.value = "Приёмка № ${r.optString("name")} проведена в МойСклад"
                closeReceive()
                loadReceiveDocs()
            } catch (e: Exception) {
                _message.value = e.message ?: "МойСклад недоступен"
            } finally {
                _loading.value = false
            }
        }
    }

    // ---------- Инвентаризация ----------

    fun scanCount(code: String, catalog: List<Product>) {
        val p = com.knit.calculator.core.MoySklad.byScan(catalog, code)
        if (p == null) {
            _message.value = "Штрихкод $code не найден"
            return
        }
        val list = _count.value
        val i = list.indexOfFirst { it.id == p.externalId }
        val next = if (i >= 0) list.mapIndexed { k, x -> if (k == i) x.copy(qty = x.qty + BigDecimal.ONE) else x }
        else listOf(CountItem(p.externalId, p.externalType, p.name, BigDecimal.ONE, p.stock)) + list
        setCount(next)
        val item = next.first { it.id == p.externalId }
        _message.value = "${item.name}: ${fmt(item.qty)}"
    }

    fun setCountQty(id: String, qty: BigDecimal) = setCount(_count.value.map { if (it.id == id) it.copy(qty = qty.max(BigDecimal.ZERO)) else it })

    fun removeCount(id: String) = setCount(_count.value.filterNot { it.id == id })

    fun clearCount() {
        setCount(emptyList())
        forget("inventory")
    }

    fun saveInventory() {
        val c = client() ?: return
        val items = JSONArray()
        _count.value.forEach { items.put(JSONObject().put("id", it.id).put("type", it.type).put("qty", it.qty.toDouble())) }
        _loading.value = true
        viewModelScope.launch {
            try {
                val d = c.msInventory(items, opId("inventory"))
                forget("inventory")
                com.knit.calculator.staff.StaffEvents.log("inventory", "${items.length()} поз.")
                _message.value = "Инвентаризация № ${d.optString("name")} создана в МойСклад"
                clearCount()
            } catch (e: Exception) {
                _message.value = e.message ?: "МойСклад недоступен"
            } finally {
                _loading.value = false
            }
        }
    }

    private fun setCount(list: List<CountItem>) {
        _count.value = list
        val a = JSONArray()
        list.forEach {
            a.put(JSONObject().put("id", it.id).put("type", it.type).put("name", it.name).put("qty", it.qty.toPlainString()).put("stock", it.stock?.toPlainString() ?: ""))
        }
        prefs.edit().putString("count", a.toString()).apply()
    }

    private fun loadCount(): List<CountItem> = runCatching {
        JSONArray(prefs.getString("count", "[]")).objects().map {
            CountItem(it.optString("id"), it.optString("type"), it.optString("name"), money(it.opt("qty")), it.optString("stock").toBigDecimalOrNull())
        }
    }.getOrDefault(emptyList())

    private fun fmt(v: BigDecimal) = v.stripTrailingZeros().toPlainString()

    // ---------- Черновики сканов и номер операции ----------

    /** Номер операции документа: создаётся при первой отправке и живёт до успеха. */
    private fun opId(doc: String): String =
        prefs.getString("op:$doc", null) ?: java.util.UUID.randomUUID().toString().also { prefs.edit().putString("op:$doc", it).apply() }

    private fun saveScans(doc: String, lines: List<ShipLine>) {
        val o = JSONObject()
        lines.filter { it.scanned.signum() > 0 }.forEach { o.put(it.id, it.scanned.toPlainString()) }
        prefs.edit().putString("scan:$doc", o.toString()).apply()
    }

    private fun restoreScans(doc: String, lines: List<ShipLine>): List<ShipLine> {
        val o = runCatching { JSONObject(prefs.getString("scan:$doc", "{}") ?: "{}") }.getOrDefault(JSONObject())
        return lines.map { l -> o.optString(l.id).toBigDecimalOrNull()?.let { l.copy(scanned = it) } ?: l }
    }

    private fun forget(doc: String) = prefs.edit().remove("scan:$doc").remove("op:$doc").apply()
}
