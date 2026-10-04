package com.knit.calculator.quote

import android.app.Application
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.knit.calculator.core.CatalogParser
import com.knit.calculator.core.Product
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.QuoteLine
import com.knit.calculator.core.QuoteLineInput
import com.knit.calculator.core.QuoteTotals
import com.knit.calculator.core.VatSettings
import com.knit.calculator.core.YarnCalculator
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal
import java.util.UUID

/** Строка КП вместе с расчётом (или `null`, если количество ещё не введено). */
data class DraftLineView(val draft: DraftLine, val product: Product, val line: QuoteLine?)

/** Состояние связи с Google Таблицей. */
data class SyncStatus(
    val connected: Boolean = false,
    val loading: Boolean = false,
    val lastSync: Long? = null,
    val error: String? = null,
    val warnings: List<String> = emptyList(),
    val sheetUrl: String = "",
)

/** Результат сохранения КП перед формированием PDF. */
sealed interface SaveResult {
    data class Saved(val number: Int) : SaveResult
    data object Local : SaveResult
    data class Failed(val message: String) : SaveResult
}

class QuoteViewModel(application: Application) : AndroidViewModel(application) {
    private val store = QuoteStore(application)

    private val _syncConfig = MutableStateFlow(store.loadSyncConfig())
    val syncConfig: StateFlow<SyncConfig> = _syncConfig.asStateFlow()

    private var localCatalog = store.loadCatalog()
    private var localSettings = store.loadSettings()
    private var cache: SheetCache? = store.loadSheetCache()

    private val _catalog = MutableStateFlow<List<Product>>(emptyList())
    val catalog: StateFlow<List<Product>> = _catalog.asStateFlow()

    private val _settings = MutableStateFlow(CompanySettings())
    val settings: StateFlow<CompanySettings> = _settings.asStateFlow()

    private val _draft = MutableStateFlow(store.loadDraft())
    val draft: StateFlow<QuoteDraft> = _draft.asStateFlow()

    private val _sync = MutableStateFlow(SyncStatus())
    val sync: StateFlow<SyncStatus> = _sync.asStateFlow()

    private val _history = MutableStateFlow<List<RemoteQuote>?>(null)
    val history: StateFlow<List<RemoteQuote>?> = _history.asStateFlow()

    private val _historyError = MutableStateFlow<String?>(null)
    val historyError: StateFlow<String?> = _historyError.asStateFlow()

    init {
        applySources()
        if (_syncConfig.value.enabled) refresh()
    }

    /** Справочники: из таблицы (или её сохранённой копии), если подключена; иначе — локальные. */
    private fun applySources() {
        val config = _syncConfig.value
        val c = cache
        if (config.enabled && c != null) {
            val parsed = CatalogParser.parse(c.sheets)
            _catalog.value = parsed.products
            _settings.value = CompanySettings.fromSheet(parsed.settings)
            _sync.update { it.copy(connected = true, lastSync = c.loadedAt, warnings = parsed.warnings, sheetUrl = c.sheetUrl) }
        } else {
            _catalog.value = localCatalog
            _settings.value = localSettings
            _sync.update { SyncStatus(connected = config.enabled, loading = it.loading, error = it.error) }
        }
    }

    // ---------- Google Таблица ----------

    fun refresh() {
        val config = _syncConfig.value
        if (!config.enabled || _sync.value.loading) return
        _sync.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                val remote = SheetClient(config).catalog()
                val newCache = SheetCache(remote.sheets, remote.url, System.currentTimeMillis())
                cache = newCache
                store.saveSheetCache(newCache)
                applySources()
                _sync.update { it.copy(loading = false, error = null) }
            } catch (e: Exception) {
                _sync.update { it.copy(loading = false, error = e.message ?: "Нет связи с таблицей") }
            }
        }
    }

    /** Подключает таблицу: проверяет адрес и ключ, загружает справочники. */
    fun connect(url: String, key: String, manager: String, onResult: (String?) -> Unit) {
        val config = SyncConfig(url.trim(), key.trim(), manager.trim())
        if (!config.url.startsWith("https://script.google.com/")) {
            onResult("Адрес должен начинаться с https://script.google.com/ и заканчиваться на /exec")
            return
        }
        _sync.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                val remote = SheetClient(config).catalog()
                _syncConfig.value = config
                store.saveSyncConfig(config)
                cache = SheetCache(remote.sheets, remote.url, System.currentTimeMillis()).also(store::saveSheetCache)
                applySources()
                _sync.update { it.copy(loading = false) }
                dropMissingLines()
                onResult(null)
            } catch (e: Exception) {
                _sync.update { it.copy(loading = false) }
                onResult(e.message ?: "Нет связи с таблицей")
            }
        }
    }

    fun updateManager(name: String) {
        _syncConfig.value = _syncConfig.value.copy(manager = name)
        store.saveSyncConfig(_syncConfig.value)
    }

    fun disconnect() {
        _syncConfig.value = SyncConfig(manager = _syncConfig.value.manager)
        store.saveSyncConfig(_syncConfig.value)
        cache = null
        store.saveSheetCache(null)
        _history.value = null
        _sync.value = SyncStatus()
        applySources()
        dropMissingLines()
    }

    /** Сохраняет КП в таблицу (если подключена) и присваивает номер. */
    suspend fun saveToSheet(views: List<DraftLineView>, totals: QuoteTotals): SaveResult {
        val config = _syncConfig.value
        if (!config.enabled) return SaveResult.Local
        val d = _draft.value
        val settings = _settings.value
        val lines = JSONArray()
        views.forEach { v ->
            val l = v.line ?: return@forEach
            lines.put(
                JSONObject()
                    .put("code", v.product.code)
                    .put("product", v.product.name)
                    .put("params", l.parameters)
                    .put("qty", l.quantity.toDouble())
                    .put("unit", v.product.unit)
                    .put("price", l.unitPrice.toDouble())
                    .put("sum", l.total.toDouble()),
            )
        }
        val payload = JSONObject()
            .put("id", d.id)
            .put("client", d.clientCompany)
            .put("contact", d.clientContact)
            .put("email", d.clientEmail)
            .put("subtotal", totals.totalWithoutVat.toDouble())
            .put("vat", totals.vat.toDouble())
            .put("total", totals.total.toDouble())
            .put("delivery", deliveryText(totals, settings))
            .put("author", listOf(config.manager, Build.MODEL.orEmpty()).filter { it.isNotBlank() }.joinToString(" / "))
            .put("data", QuoteStore.draftToJson(d.copy(saved = true)))
            .put("lines", lines)
        return try {
            val number = SheetClient(config).saveQuote(payload)
            updateDraft { it.copy(number = number, saved = true) }
            SaveResult.Saved(number)
        } catch (e: Exception) {
            SaveResult.Failed(e.message ?: "Нет связи с таблицей")
        }
    }

    fun loadHistory() {
        val config = _syncConfig.value
        if (!config.enabled) return
        _historyError.value = null
        viewModelScope.launch {
            try {
                _history.value = SheetClient(config).quotes()
            } catch (e: Exception) {
                _historyError.value = e.message ?: "Нет связи с таблицей"
            }
        }
    }

    /** Открывает сохранённое КП для просмотра, изменения или повторной отправки. */
    fun openQuote(remote: RemoteQuote): Boolean {
        val draft = QuoteStore.draftFromJson(remote.data) ?: return false
        updateDraft { draft.copy(id = remote.id.ifBlank { draft.id }, number = remote.number, saved = true) }
        return true
    }

    // ---------- Расчёт ----------

    fun lineViews(draft: QuoteDraft, catalog: List<Product>): List<DraftLineView> =
        draft.lines.mapNotNull { d ->
            val product = catalog.firstOrNull { it.id == d.productId } ?: return@mapNotNull null
            val qty = YarnCalculator.parseDecimal(d.quantity)?.takeIf { it.signum() > 0 }
            DraftLineView(d, product, qty?.let { QuoteCalculator.line(QuoteLineInput(product, d.selected, it)) })
        }

    fun totals(views: List<DraftLineView>, settings: CompanySettings): QuoteTotals =
        QuoteCalculator.totals(views.mapNotNull { it.line }, settings.vat())

    // ---------- Черновик КП ----------

    fun updateDraft(transform: (QuoteDraft) -> QuoteDraft) {
        _draft.value = transform(_draft.value)
        store.saveDraft(_draft.value)
    }

    fun addLine(product: Product) = updateDraft { d ->
        val quantity = product.minOrder.takeIf { it.signum() > 0 }
            ?.stripTrailingZeros()?.toPlainString()?.replace('.', ',')
            .orEmpty()
        d.copy(lines = d.lines + DraftLine(newId(), product.id, quantity = quantity))
    }

    fun updateLine(id: Long, transform: (DraftLine) -> DraftLine) = updateDraft { d ->
        d.copy(lines = d.lines.map { if (it.id == id) transform(it) else it })
    }

    fun removeLine(id: Long) = updateDraft { d -> d.copy(lines = d.lines.filterNot { it.id == id }) }

    /** Новое КП. В локальном режиме номер увеличивается на 1, с таблицей — выдаётся при сохранении. */
    fun newQuote() = updateDraft {
        QuoteDraft(id = UUID.randomUUID().toString(), number = if (_syncConfig.value.enabled) 0 else it.number + 1)
    }

    private fun dropMissingLines() {
        val ids = _catalog.value.map { it.id }.toSet()
        updateDraft { d -> d.copy(lines = d.lines.filter { it.productId in ids }) }
    }

    // ---------- Ассортимент (локальный режим, без таблицы) ----------

    /** Изделие, открытое в редакторе (хранится здесь, чтобы пережить поворот экрана). */
    private val _editing = MutableStateFlow<EditableProduct?>(null)
    val editing: StateFlow<EditableProduct?> = _editing.asStateFlow()

    fun startEditing(productId: Long?) {
        _editing.value = _catalog.value.firstOrNull { it.id == productId }?.let(EditableProduct::from) ?: EditableProduct()
    }

    fun edit(transform: (EditableProduct) -> EditableProduct) {
        _editing.value = _editing.value?.let(transform)
    }

    fun isNewProduct(id: Long): Boolean = _catalog.value.none { it.id == id }

    /** Сохраняет редактируемое изделие; `false`, если данные некорректны. */
    fun saveEditing(): Boolean {
        val product = _editing.value?.toProduct() ?: return false
        localCatalog = if (localCatalog.any { it.id == product.id }) localCatalog.map { if (it.id == product.id) product else it } else localCatalog + product
        persistLocalCatalog()
        _editing.value = null
        return true
    }

    fun cancelEditing() {
        _editing.value = null
    }

    fun deleteProduct(id: Long) {
        localCatalog = localCatalog.filterNot { it.id == id }
        persistLocalCatalog()
        dropMissingLines()
    }

    fun moveProduct(id: Long, delta: Int) {
        val list = localCatalog.toMutableList()
        val from = list.indexOfFirst { it.id == id }
        val to = from + delta
        if (from < 0 || to !in list.indices) return
        list.add(to, list.removeAt(from))
        localCatalog = list
        persistLocalCatalog()
    }

    fun resetCatalog() {
        localCatalog = DefaultCatalog.products
        persistLocalCatalog()
        dropMissingLines()
    }

    private fun persistLocalCatalog() {
        store.saveCatalog(localCatalog)
        applySources()
    }

    // ---------- Реквизиты (локальный режим) ----------

    fun updateSettings(transform: (CompanySettings) -> CompanySettings) {
        localSettings = transform(localSettings)
        store.saveSettings(localSettings)
        applySources()
    }

    fun resetSettings() = updateSettings { CompanySettings() }
}

fun CompanySettings.vat(): VatSettings =
    VatSettings(YarnCalculator.parseDecimal(vatRate)?.max(BigDecimal.ZERO) ?: BigDecimal.ZERO, vatIncluded)

/** «Бесплатно» или «Бесплатно при заказе от 50 000,00 ₽»; пусто, если порог не задан. */
fun deliveryText(totals: QuoteTotals, settings: CompanySettings): String {
    val threshold = settings.freeDeliveryThreshold ?: return ""
    return if (totals.total >= threshold) "Бесплатно"
    else "Бесплатно при заказе от ${QuoteCalculator.formatMoney(threshold)} ₽"
}
