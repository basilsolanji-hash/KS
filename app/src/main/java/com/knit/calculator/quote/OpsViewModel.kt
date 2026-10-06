package com.knit.calculator.quote

import android.app.Application
import android.content.Context
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.knit.calculator.core.Invoice
import com.knit.calculator.core.OrderStage
import com.knit.calculator.core.Payment
import com.knit.calculator.core.ProductionOrder
import com.knit.calculator.core.YarnAmount
import com.knit.calculator.core.YarnMove
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal
import java.util.UUID

/** Учёт: счета, оплаты, заказы на производство и движение пряжи. */
data class OpsData(
    val invoices: List<Invoice> = emptyList(),
    val payments: List<Payment> = emptyList(),
    val orders: List<ProductionOrder> = emptyList(),
    val moves: List<YarnMove> = emptyList(),
    /** Оплачено по «Заказам покупателя» МойСклад (ID КП → сумма); `null` — МойСклад не подключён. */
    val msPaid: Map<String, BigDecimal>? = null,
    val msError: String? = null,
) {
    /** Оплаты для расчёта долгов: с МойСклад — по его данным (их вносит и бухгалтер), иначе — из листа «Оплаты». */
    val paymentsForDebts: List<Payment>
        get() = msPaid?.map { (quoteId, paid) -> Payment("ms-$quoteId", quoteId, 0, "", 0, paid, "МойСклад") } ?: payments
}

/** JSON учёта: тот же формат, что отдаёт скрипт таблицы (action = ops). */
object OpsJson {
    private fun money(o: JSONObject, key: String) = BigDecimal(o.optString(key, "0").ifBlank { "0" })

    fun invoice(i: Invoice): JSONObject = JSONObject().put("number", i.number).put("quoteId", i.quoteId)
        .put("quoteNumber", i.quoteNumber).put("client", i.client).put("date", i.date)
        .put("amount", i.amount.toPlainString()).put("purpose", i.purpose)

    fun payment(p: Payment): JSONObject = JSONObject().put("id", p.id).put("quoteId", p.quoteId)
        .put("quoteNumber", p.quoteNumber).put("client", p.client).put("date", p.date)
        .put("amount", p.amount.toPlainString()).put("note", p.note)

    fun yarn(list: List<YarnAmount>): String = JSONArray().apply {
        list.forEach { put(JSONObject().put("yarn", it.yarn).put("kg", it.kg.toPlainString())) }
    }.toString()

    fun order(o: ProductionOrder): JSONObject = JSONObject().put("quoteId", o.quoteId).put("quoteNumber", o.quoteNumber)
        .put("client", o.client).put("created", o.created).put("due", o.due).put("stage", o.stage.title)
        .put("stageDate", o.stageDate).put("items", o.items).put("comment", o.comment)
        .put("yarn", yarn(o.yarn)).put("yarnWrittenOff", o.yarnWrittenOff)

    fun move(m: YarnMove): JSONObject = JSONObject().put("id", m.id).put("date", m.date).put("yarn", m.yarn)
        .put("kg", m.kg.toPlainString()).put("reason", m.reason)

    private fun JSONArray?.objects(): List<JSONObject> =
        if (this == null) emptyList() else (0 until length()).mapNotNull { optJSONObject(it) }

    private fun yarnList(raw: String): List<YarnAmount> = try {
        JSONArray(raw).objects().map { YarnAmount(it.optString("yarn"), money(it, "kg")) }
    } catch (e: Exception) {
        emptyList()
    }

    fun parse(o: JSONObject): OpsData = OpsData(
        invoices = o.optJSONArray("invoices").objects().map {
            Invoice(it.optInt("number"), it.optString("quoteId"), it.optInt("quoteNumber"), it.optString("client"),
                it.optLong("date"), money(it, "amount"), it.optString("purpose"))
        },
        payments = o.optJSONArray("payments").objects().map {
            Payment(it.optString("id").ifBlank { UUID.randomUUID().toString() }, it.optString("quoteId"), it.optInt("quoteNumber"),
                it.optString("client"), it.optLong("date"), money(it, "amount"), it.optString("note"))
        },
        orders = o.optJSONArray("orders").objects().map {
            ProductionOrder(
                quoteId = it.optString("quoteId"), quoteNumber = it.optInt("quoteNumber"), client = it.optString("client"),
                created = it.optLong("created"), due = it.optLong("due"), stage = OrderStage.from(it.optString("stage")),
                stageDate = it.optLong("stageDate"), items = it.optString("items"), comment = it.optString("comment"),
                yarn = yarnList(it.optString("yarn")), yarnWrittenOff = it.optBoolean("yarnWrittenOff"),
            )
        },
        moves = o.optJSONArray("moves").objects().map {
            YarnMove(it.optString("id").ifBlank { UUID.randomUUID().toString() }, it.optLong("date"), it.optString("yarn"),
                money(it, "kg"), it.optString("reason"))
        },
        msPaid = o.optJSONObject("ms")?.optJSONArray("orders")?.objects()?.associate { it.optString("quoteId") to money(it, "paid") },
        msError = o.optJSONObject("ms")?.optString("error")?.ifBlank { null },
    )

    fun toJson(d: OpsData): JSONObject = JSONObject()
        .put("invoices", JSONArray().apply { d.invoices.forEach { put(invoice(it)) } })
        .put("payments", JSONArray().apply { d.payments.forEach { put(payment(it)) } })
        .put("orders", JSONArray().apply { d.orders.forEach { put(order(it)) } })
        .put("moves", JSONArray().apply { d.moves.forEach { put(move(it)) } })
}

/** Учёт на этом телефоне (без таблицы) и копия из таблицы для работы без связи. */
class OpsStore(context: Context) {
    private val prefs = context.getSharedPreferences("ops", Context.MODE_PRIVATE)

    fun load(remote: Boolean): OpsData = prefs.getString(if (remote) KEY_CACHE else KEY_LOCAL, null)
        ?.let { runCatching { OpsJson.parse(JSONObject(it)) }.getOrNull() } ?: OpsData()

    fun save(remote: Boolean, data: OpsData) {
        prefs.edit().putString(if (remote) KEY_CACHE else KEY_LOCAL, OpsJson.toJson(data).toString()).apply()
    }

    private companion object {
        const val KEY_LOCAL = "local"
        const val KEY_CACHE = "sheet_cache"
    }
}

/**
 * Счета, оплаты, заказы и склад пряжи. С таблицей всё пишется в её листы (общие для всех телефонов),
 * без таблицы — на этот телефон. При ошибке связи изменения не теряются молча: возвращается текст ошибки.
 */
class OpsViewModel(application: Application) : AndroidViewModel(application) {
    private val quoteStore = QuoteStore(application)
    private val store = OpsStore(application)

    private val _data = MutableStateFlow(store.load(remote = quoteStore.loadSyncConfig().enabled))
    val data: StateFlow<OpsData> = _data.asStateFlow()

    private val _loading = MutableStateFlow(false)
    val loading: StateFlow<Boolean> = _loading.asStateFlow()

    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error.asStateFlow()

    private val config get() = quoteStore.loadSyncConfig()
    private fun author() = listOf(config.manager, Build.MODEL.orEmpty()).filter { it.isNotBlank() }.joinToString(" / ")

    private var loadedAt = 0L

    /** Не чаще раза в [maxAgeMs] (главный экран); экраны учёта вызывают [load] — всегда свежие данные. */
    fun loadIfStale(maxAgeMs: Long = 5 * 60_000L) {
        if (System.currentTimeMillis() - loadedAt < maxAgeMs) return
        load()
    }

    fun load() {
        loadedAt = System.currentTimeMillis()
        val c = config
        _error.value = null
        if (!c.enabled) {
            _data.value = store.load(remote = false)
            return
        }
        _data.value = store.load(remote = true)
        if (_loading.value) return
        _loading.value = true
        viewModelScope.launch {
            try {
                val fresh = OpsJson.parse(SheetClient(c).ops())
                _data.value = fresh
                store.save(remote = true, fresh)
            } catch (e: Exception) {
                _error.value = e.message ?: "Нет связи с таблицей"
            } finally {
                _loading.value = false
            }
        }
    }

    /** Изменение: в таблицу (если подключена), затем в память и на телефон. `null` — успех, иначе текст ошибки. */
    private suspend fun change(remote: suspend (SheetClient) -> Unit, local: (OpsData) -> OpsData): String? {
        val c = config
        if (c.enabled) {
            try {
                remote(SheetClient(c))
            } catch (e: Exception) {
                return e.message ?: "Нет связи с таблицей"
            }
        }
        _data.update(local)
        store.save(remote = c.enabled, _data.value)
        return null
    }

    /** Неудачная оплата: повтор той же оплаты идёт с тем же ID — таблица и МойСклад не создадут дубль. */
    private var pendingPayment: Payment? = null

    suspend fun addPayment(quoteId: String, quoteNumber: Int, client: String, amount: BigDecimal, date: Long, note: String): String? {
        val retry = pendingPayment?.takeIf { it.quoteId == quoteId && it.amount.compareTo(amount) == 0 && it.note == note.trim() }
        val p = retry ?: Payment(UUID.randomUUID().toString(), quoteId, quoteNumber, client, date, amount, note.trim())
        pendingPayment = p
        var msError: String? = null
        val error = change({ msError = it.addPayment(OpsJson.payment(p).put("author", author())) }) { d ->
            if (d.payments.any { it.id == p.id }) d else d.copy(payments = d.payments + p)
        }
        if (error == null) {
            pendingPayment = null
            com.knit.calculator.staff.StaffEvents.log("payment", "КП № $quoteNumber · $client · ${amount.toPlainString()} ₽")
        }
        // Оплаченная сумма в МойСклад пересчитывается там — перечитываем.
        if (error == null && _data.value.msPaid != null) load()
        return error ?: msError?.let { "Оплата записана в таблицу, но не в МойСклад: $it" }
    }

    suspend fun deletePayment(p: Payment): String? =
        change({ it.deletePayment(p.id) }) { d -> d.copy(payments = d.payments.filterNot { it.id == p.id }) }

    /** Новый счёт с номером из таблицы (или следующим на этом телефоне). */
    suspend fun createInvoice(quoteId: String, quoteNumber: Int, client: String, amount: BigDecimal, purpose: String): Result<Invoice> {
        val c = config
        val draft = Invoice(0, quoteId, quoteNumber, client, System.currentTimeMillis(), amount, purpose)
        val number = if (c.enabled) {
            try {
                SheetClient(c).addInvoice(OpsJson.invoice(draft))
            } catch (e: Exception) {
                return Result.failure(e)
            }
        } else {
            (_data.value.invoices.maxOfOrNull { it.number } ?: 0) + 1
        }
        val invoice = draft.copy(number = number)
        _data.update { it.copy(invoices = it.invoices + invoice) }
        store.save(remote = c.enabled, _data.value)
        return Result.success(invoice)
    }

    suspend fun saveOrder(order: ProductionOrder): String? =
        change({ it.saveOrder(OpsJson.order(order)) }) { d -> d.copy(orders = d.orders.filterNot { it.quoteId == order.quoteId } + order) }

    /**
     * Новый этап заказа. При переходе в вязку пряжа заказа один раз списывается со склада.
     */
    suspend fun setStage(order: ProductionOrder, stage: OrderStage, ownYarnStock: Boolean = true): String? {
        val now = System.currentTimeMillis()
        // С МойСклад склад пряжи ведётся там — своё списание не делаем.
        val writeOff = ownYarnStock && stage != OrderStage.NEW && !order.yarnWrittenOff && order.yarn.isNotEmpty()
        if (writeOff) {
            val moves = order.yarn.map {
                YarnMove("order-${order.quoteId}-${it.yarn}", now, it.yarn, it.kg.negate(), "Заказ по КП № ${order.quoteNumber}")
            }
            addMoves(moves)?.let { return it }
        }
        return saveOrder(order.copy(stage = stage, stageDate = now, yarnWrittenOff = order.yarnWrittenOff || writeOff))
    }

    suspend fun addMoves(moves: List<YarnMove>): String? {
        val array = JSONArray().apply { moves.forEach { put(OpsJson.move(it).put("author", author())) } }
        return change({ it.addYarnMoves(array) }) { d -> d.copy(moves = d.moves + moves.filter { m -> d.moves.none { it.id == m.id } }) }
    }

    fun newMoveId(): String = UUID.randomUUID().toString()
}
