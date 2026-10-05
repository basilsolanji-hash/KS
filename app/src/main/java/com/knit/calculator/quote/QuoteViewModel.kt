package com.knit.calculator.quote

import android.app.Application
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.knit.calculator.core.CatalogParser
import com.knit.calculator.report.BrandLogo
import com.knit.calculator.core.Client
import com.knit.calculator.core.CostCalculator
import com.knit.calculator.core.LineEconomics
import com.knit.calculator.core.MonthReport
import com.knit.calculator.core.OrderYarn
import com.knit.calculator.core.OrderYarnCalculator
import com.knit.calculator.core.QuoteStatus
import com.knit.calculator.core.QuoteSummary
import com.knit.calculator.core.ReportCalculator
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
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal
import java.util.UUID

/** Строка КП вместе с расчётом (или `null`, если количество ещё не введено) и экономикой для менеджера. */
data class DraftLineView(
    val draft: DraftLine,
    val product: Product,
    val line: QuoteLine?,
    val economics: LineEconomics? = null,
    /** Скидка больше разрешённой — поле подсвечивается, применяется максимум. */
    val discountTooHigh: Boolean = false,
)

/** КП в истории — из Google Таблицы или из архива этого телефона. */
data class HistoryItem(
    val id: String,
    val number: Int,
    val date: String,
    val month: String,
    val client: String,
    val total: BigDecimal,
    val profit: BigDecimal?,
    val status: QuoteStatus,
    val author: String,
    val data: String,
    val validUntil: Long,
    val products: Map<String, BigDecimal>,
)

/** Состояние связи с Google Таблицей. */
data class SyncStatus(
    val connected: Boolean = false,
    val loading: Boolean = false,
    val lastSync: Long? = null,
    val error: String? = null,
    val warnings: List<String> = emptyList(),
    val sheetUrl: String = "",
    /** МойСклад подключён в скрипте таблицы. */
    val msEnabled: Boolean = false,
    val msLoading: Boolean = false,
    val msLoadedAt: Long? = null,
    val msStore: String = "",
    val msError: String? = null,
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

    private val _history = MutableStateFlow<List<HistoryItem>?>(null)
    val history: StateFlow<List<HistoryItem>?> = _history.asStateFlow()

    /** Затраты, цены пряжи и клиенты — из таблицы или стартовые. */
    private var parsedExtras = DefaultCatalog.parsed

    private val _clients = MutableStateFlow<List<Client>>(emptyList())
    val clients: StateFlow<List<Client>> = _clients.asStateFlow()

    private val _historyError = MutableStateFlow<String?>(null)
    val historyError: StateFlow<String?> = _historyError.asStateFlow()

    private val _unlockedPin = MutableStateFlow(store.directorPin)

    /** Роль по ключу доступа (с таблицей решает сервер: себестоимость менеджеру и не приходит). */
    private val _serverRole = MutableStateFlow(store.serverRole)

    /**
     * Режим директора: себестоимость, прибыль и экономика видны. С таблицей — по роли ключа (лист «Менеджеры»),
     * без таблицы — по PIN (PIN не задан — режим у всех).
     */
    val director: StateFlow<Boolean> = kotlinx.coroutines.flow.combine(_settings, _unlockedPin, _serverRole, _syncConfig) { s, pin, role, config ->
        if (config.enabled) role == "director" else s.directorPin.isBlank() || pin == s.directorPin
    }.stateIn(viewModelScope, kotlinx.coroutines.flow.SharingStarted.Eagerly, true)

    private fun applyRole(remote: RemoteCatalog) {
        store.serverRole = remote.role
        store.serverManager = remote.manager
        _serverRole.value = remote.role
    }

    fun unlockDirector(pin: String): Boolean {
        val ok = pin.trim().isNotEmpty() && pin.trim() == _settings.value.directorPin
        if (ok) {
            store.directorPin = pin.trim()
            _unlockedPin.value = pin.trim()
        }
        return ok
    }

    fun lockDirector() {
        store.directorPin = ""
        _unlockedPin.value = ""
    }

    // ---------- МойСклад ----------

    private val _msProducts = MutableStateFlow<List<Product>>(emptyList())
    /** Товары МойСклад с ценами по тиражам и остатками склада. */
    val msProducts: StateFlow<List<Product>> = _msProducts.asStateFlow()
    private var msIndex: Map<Long, Product> = emptyMap()
    private var msClients: List<Client> = emptyList()

    /** Короткие сообщения для пользователя (например, предупреждение МойСклад). */
    private val _notices = kotlinx.coroutines.flow.MutableSharedFlow<String>(extraBufferCapacity = 8)
    val notices: kotlinx.coroutines.flow.SharedFlow<String> = _notices

    private val _msFilters = MutableStateFlow<List<String>>(emptyList())
    /** Характеристики-фильтры выбора позиции (лист «Настройки» → «МойСклад: фильтры»). */
    val msFilters: StateFlow<List<String>> = _msFilters.asStateFlow()

    private fun applyMs(o: JSONObject?) {
        fun strings(a: JSONArray?) = if (a == null) emptyList() else (0 until a.length()).map { a.optString(it) }.filter { it.isNotBlank() }
        val clientChars = strings(o?.optJSONArray("clientChars"))
        _msFilters.value = strings(o?.optJSONArray("filters"))
        val products = o?.optJSONArray("products")
        val items = if (products == null) emptyList() else (0 until products.length()).mapNotNull { products.optJSONObject(it) }.mapNotNull { p ->
            fun money(key: String) = p.optString(key).takeIf { it.isNotBlank() && it != "null" }?.toBigDecimalOrNull()
            val tiers = p.optJSONArray("tiers")?.let { a ->
                (0 until a.length()).mapNotNull { a.optJSONObject(it) }.mapNotNull { t ->
                    val from = t.optString("from").toBigDecimalOrNull() ?: return@mapNotNull null
                    val price = t.optString("price").toBigDecimalOrNull() ?: return@mapNotNull null
                    from to price
                }
            }.orEmpty()
            com.knit.calculator.core.MoySklad.product(
                com.knit.calculator.core.MsItem(
                    id = p.optString("id"), name = p.optString("name"), article = p.optString("article"),
                    group = p.optString("group"), weightGrams = money("weight"), buyPrice = money("buyPrice"),
                    minPrice = money("minPrice"), description = p.optString("description"), tiers = tiers,
                    stock = if (p.isNull("stock")) null else money("stock"),
                    type = p.optString("type").ifBlank { "product" },
                    chars = p.optJSONObject("chars")?.let { c -> c.keys().asSequence().associateWith { k -> c.optString(k) } }.orEmpty(),
                    badges = strings(p.optJSONArray("badges")),
                    barcode = p.optString("barcode"),
                ),
                clientChars,
            )
        }
        _msProducts.value = items
        msIndex = items.associateBy { it.id }
        val clients = o?.optJSONArray("clients")
        msClients = if (clients == null) emptyList() else (0 until clients.length()).mapNotNull { clients.optJSONObject(it) }.map {
            Client(it.optString("name"), "", it.optString("email"), it.optString("phone"), it.optString("inn"))
        }
        _clients.value = mergeClients(_clients.value, msClients)
        _sync.update {
            it.copy(
                msEnabled = o != null, msStore = o?.optString("store").orEmpty(),
                msLoadedAt = o?.optLong("loadedAt")?.takeIf { t -> t > 0 },
            )
        }
    }

    /** Товары МойСклад: при подключённой таблице — раз в 12 часов или по кнопке. */
    fun refreshMs(force: Boolean = false, maxAgeMs: Long = 12 * 3_600_000L) {
        val config = _syncConfig.value
        if (!config.enabled || _sync.value.msLoading) return
        val age = System.currentTimeMillis() - (_sync.value.msLoadedAt ?: 0L)
        if (!force && _sync.value.msLoadedAt != null && age < maxAgeMs) return
        _sync.update { it.copy(msLoading = true, msError = null) }
        viewModelScope.launch {
            try {
                val o = SheetClient(config).msCatalog(fresh = force)
                store.saveMsCatalog(o)
                applyMs(o)
                _sync.update { it.copy(msLoading = false) }
            } catch (e: Exception) {
                _sync.update { it.copy(msLoading = false, msError = e.message ?: "Нет связи с МойСклад") }
            }
        }
    }

    /**
     * Штрихкод для этикетки: из МойСклад, а если его нет — создаётся там же (внутренний EAN-13).
     * Новый код сразу попадает в каталог на телефоне.
     */
    suspend fun ensureBarcode(product: Product): Result<String> {
        if (product.barcode.isNotBlank()) return Result.success(product.barcode)
        val config = _syncConfig.value
        if (!config.enabled || product.externalId.isBlank()) return Result.failure(SheetException("Нужно подключение к МойСклад"))
        return try {
            val code = SheetClient(config).msBarcode(product.externalType, product.externalId)
            val updated = product.copy(barcode = code)
            _msProducts.update { list -> list.map { if (it.id == product.id) updated else it } }
            msIndex = msIndex + (product.id to updated)
            store.loadMsCatalog()?.let { o ->
                o.optJSONArray("products")?.let { a ->
                    for (i in 0 until a.length()) a.optJSONObject(i)?.takeIf { it.optString("id") == product.externalId }?.put("barcode", code)
                }
                store.saveMsCatalog(o)
            }
            Result.success(code)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    /** Позиции МойСклад заказа (КП) с количеством — для этикеток. */
    fun labelProducts(item: HistoryItem): List<Pair<Product, java.math.BigDecimal>> {
        val draft = QuoteStore.draftFromJson(fullItem(item).data) ?: return emptyList()
        return draft.lines.mapNotNull { l ->
            val p = msIndex[l.productId] ?: return@mapNotNull null
            p to (com.knit.calculator.core.YarnCalculator.parseDecimal(l.quantity) ?: java.math.BigDecimal.ONE)
        }
    }

    fun msProduct(id: Long): Product? = msIndex[id]

    init {
        if (_syncConfig.value.enabled) applyMs(store.loadMsCatalog())
        applySources()
        if (_syncConfig.value.enabled) refresh()
    }

    /** Справочники: из таблицы (или её сохранённой копии), если подключена; иначе — локальные. */
    private fun applySources() {
        val config = _syncConfig.value
        val c = cache
        if (config.enabled && c != null) {
            val parsed = CatalogParser.parse(c.sheets)
            parsedExtras = parsed
            _catalog.value = parsed.products
            _settings.value = CompanySettings.fromSheet(parsed.settings)
            _clients.value = mergeClients(parsed.clients, store.loadLocalClients() + msClients)
            _sync.update { it.copy(connected = true, lastSync = c.loadedAt, warnings = parsed.warnings, sheetUrl = c.sheetUrl) }
        } else {
            parsedExtras = DefaultCatalog.parsed
            _catalog.value = localCatalog
            _settings.value = localSettings
            _clients.value = mergeClients(store.loadLocalClients(), msClients)
            // Поля МойСклад не сбрасываем: товары могут быть загружены и без копии справочников.
            _sync.update { it.copy(connected = config.enabled, lastSync = null, warnings = emptyList(), sheetUrl = "") }
        }
    }

    // ---------- Google Таблица ----------

    fun refresh() {
        val config = _syncConfig.value
        if (!config.enabled || _sync.value.loading) return
        _sync.update { it.copy(loading = true, error = null) }
        viewModelScope.launch {
            try {
                val remote = SheetClient(config).catalog(store.logoVersion)
                applyLogo(remote.logo)
                applyRole(remote)
                val newCache = SheetCache(remote.sheets, remote.url, System.currentTimeMillis(), remote.contract)
                cache = newCache
                store.saveSheetCache(newCache)
                applySources()
                _sync.update { it.copy(loading = false, error = null) }
                refreshMs()
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
                applyLogo(remote.logo)
                applyRole(remote)
                _syncConfig.value = config
                store.saveSyncConfig(config)
                cache = SheetCache(remote.sheets, remote.url, System.currentTimeMillis(), remote.contract).also(store::saveSheetCache)
                applySources()
                _sync.update { it.copy(loading = false) }
                dropMissingLines()
                onResult(null)
                refreshMs(force = true)
            } catch (e: Exception) {
                _sync.update { it.copy(loading = false) }
                onResult(e.message ?: "Нет связи с таблицей")
            }
        }
    }

    /** Логотип из папки Диска: новый — сохраняем; папка пуста — встроенный логотип. */
    private fun applyLogo(logo: RemoteLogo?) {
        logo ?: return
        val data = logo.data
        when {
            data != null -> BrandLogo.save(getApplication(), data)
            logo.version.isBlank() -> BrandLogo.clear(getApplication())
        }
        store.logoVersion = logo.version
    }

    /** Фото, которых ещё нет на Диске, загружаем перед сохранением КП (ошибка не мешает КП). */
    private suspend fun uploadPhotos(config: SyncConfig) {
        val d = _draft.value
        d.lines.filter { it.photoPath != null && it.photoFileId == null }.forEach { line ->
            val file = java.io.File(line.photoPath!!)
            if (!file.exists()) return@forEach
            try {
                val id = SheetClient(config).uploadFile("photo", "КП-${d.id.take(8)}-${line.id}.jpg", file.readBytes(), "image/jpeg")
                updateLine(line.id) { it.copy(photoFileId = id) }
            } catch (e: Exception) {
                // Фото останется только на этом телефоне.
            }
        }
    }

    /** Фото из КП другого телефона скачиваем с Диска. */
    private fun downloadMissingPhotos() {
        val config = _syncConfig.value
        if (!config.enabled) return
        val missing = _draft.value.lines.filter { it.photoFileId != null && (it.photoPath == null || !java.io.File(it.photoPath).exists()) }
        if (missing.isEmpty()) return
        viewModelScope.launch {
            missing.forEach { line ->
                try {
                    val bytes = SheetClient(config).getFile(line.photoFileId!!)
                    val dir = java.io.File(getApplication<Application>().filesDir, "photos").apply { mkdirs() }
                    val file = java.io.File(dir, "${line.id}-${line.photoFileId}.jpg")
                    kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { file.writeBytes(bytes) }
                    updateLine(line.id) { it.copy(photoPath = file.absolutePath) }
                } catch (e: Exception) {
                    // Без фото КП всё равно откроется.
                }
            }
        }
    }

    /** Копия PDF КП сразу (кнопка «Сохранить в облако»): `null` — успех, иначе текст ошибки. */
    suspend fun uploadPdfNow(file: java.io.File): String? {
        val config = _syncConfig.value
        if (!config.enabled) return null
        return try {
            val bytes = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { file.readBytes() }
            SheetClient(config).uploadFile("pdf", file.name, bytes, "application/pdf", _draft.value.id)
            null
        } catch (e: Exception) {
            e.message ?: "Нет связи с Google Диском"
        }
    }

    /** Письмо клиенту через Google-скрипт: `null` — отправлено, иначе текст ошибки. */
    suspend fun sendEmailNow(file: java.io.File, to: String, subject: String, text: String): String? {
        val config = _syncConfig.value
        if (!config.enabled) return "Google Таблица не подключена"
        return try {
            val bytes = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { file.readBytes() }
            SheetClient(config).sendEmail(_draft.value.id, to, subject, text, file.name, bytes)
            null
        } catch (e: Exception) {
            e.message ?: "Нет связи с Google"
        }
    }

    /** Копия PDF КП — в папку «КП (PDF)» Диска, ссылка — в лист «КП». В фоне, ошибки не мешают отправке. */
    fun uploadPdf(file: java.io.File) {
        val config = _syncConfig.value
        val d = _draft.value
        if (!config.enabled || !d.saved) return
        viewModelScope.launch {
            runCatching {
                val bytes = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { file.readBytes() }
                SheetClient(config).uploadFile("pdf", file.name, bytes, "application/pdf", d.id)
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
        store.saveMsCatalog(null)
        applyMs(null)
        _history.value = null
        _sync.value = SyncStatus()
        applySources()
        dropMissingLines()
    }

    /**
     * Сохраняет КП: в архив этого телефона (для истории, отчёта и напоминаний) и в таблицу,
     * если она подключена (таблица выдаёт номер).
     */
    suspend fun saveQuote(views: List<DraftLineView>, totals: QuoteTotals): SaveResult {
        val config = _syncConfig.value
        val settings = _settings.value
        val economics = CostCalculator.quote(views.mapNotNull { v -> v.line?.let { v.economics } })
        val profit = economics.totalProfit.takeIf { views.any { it.economics != null } }
        val validUntil = validUntilMillis(settings)
        val products = linkedMapOf<String, BigDecimal>()
        views.forEach { v -> v.line?.let { products[v.product.name] = (products[v.product.name] ?: BigDecimal.ZERO) + it.total } }
        rememberClient()
        updateDraft { d ->
            d.copy(snapshot = views.mapNotNull { v -> v.line?.let { SnapshotLine(it.description, it.quantity, v.product.unit, it.unitPrice, it.total) } })
        }

        var number = _draft.value.number
        var result: SaveResult = SaveResult.Local
        if (config.enabled) {
            uploadPhotos(config)
            val d = _draft.value
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
                        .put("sum", l.total.toDouble())
                        .put("discount", l.discountPercent.toDouble())
                        .put("msId", v.product.externalId)
                        .put("msType", v.product.externalType),
                )
            }
            val payload = JSONObject()
                .put("id", d.id)
                .put("client", d.clientCompany)
                .put("contact", d.clientContact)
                .put("email", d.clientEmail)
                .put("phone", d.clientPhone)
                .put("inn", d.clientInn)
                .put("kpp", d.clientKpp)
                .put("address", d.clientAddress)
                .put("subtotal", totals.totalWithoutVat.toDouble())
                .put("vat", totals.vat.toDouble())
                .put("total", totals.total.toDouble())
                .put("cost", if (profit != null) economics.totalCost.toDouble() else "")
                .put("profit", profit?.toDouble() ?: "")
                .put("validUntil", validUntil ?: 0L)
                .put("delivery", deliveryText(totals, settings))
                .put("author", author())
                .put("data", QuoteStore.draftToJson(d.copy(saved = true)))
                .put("lines", lines)
            result = try {
                val (saved, ms) = SheetClient(config).saveQuote(payload)
                number = saved
                ms?.error?.let { _notices.tryEmit("КП № $saved сохранено, но в МойСклад не записано: $it") }
                SaveResult.Saved(number)
            } catch (e: Exception) {
                return SaveResult.Failed(e.message ?: "Нет связи с таблицей")
            }
        }
        updateDraft { it.copy(number = number, saved = true) }
        val d = _draft.value
        val previous = store.loadArchive().firstOrNull { it.id == d.id }
        val archived = ArchivedQuote(
            id = d.id,
            number = number,
            createdAt = previous?.createdAt ?: System.currentTimeMillis(),
            client = d.clientCompany,
            total = totals.total,
            profit = profit,
            status = previous?.status ?: QuoteStatus.SENT,
            validUntil = validUntil,
            manager = author(),
            data = QuoteStore.draftToJson(d),
            products = products,
        )
        store.saveArchive(listOf(archived) + store.loadArchive().filterNot { it.id == d.id })
        if (validUntil != null) {
            QuoteReminders.schedule(getApplication(), d.id, number, d.clientCompany, validUntil, settings.reminderDaysValue)
        }
        return result
    }

    private fun author() = listOf(store.serverManager.ifBlank { _syncConfig.value.manager }, Build.MODEL.orEmpty())
        .filter { it.isNotBlank() }.joinToString(" / ")

    private fun validUntilMillis(settings: CompanySettings): Long? {
        val days = YarnCalculator.parseDecimal(settings.validityDays)?.toInt()?.takeIf { it > 0 } ?: return null
        return java.util.Calendar.getInstance().apply {
            add(java.util.Calendar.DAY_OF_YEAR, days)
            set(java.util.Calendar.HOUR_OF_DAY, 23)
            set(java.util.Calendar.MINUTE, 59)
        }.timeInMillis
    }

    /** Клиент из КП — в справочник этого телефона (в таблице его добавляет скрипт). */
    private fun rememberClient() {
        val d = _draft.value
        val company = d.clientCompany.trim()
        if (company.isEmpty()) return
        val local = store.loadLocalClients().filterNot { it.company.equals(company, ignoreCase = true) }
        val client = Client(company, d.clientContact.trim(), d.clientEmail.trim(), d.clientPhone.trim(), d.clientInn.trim())
        store.saveLocalClients(listOf(client) + local)
        _clients.value = mergeClients(_clients.value, listOf(client))
    }

    private fun mergeClients(primary: List<Client>, extra: List<Client>): List<Client> =
        (primary + extra).distinctBy { it.company.trim().lowercase() }

    fun applyClient(client: Client) = updateDraft {
        it.copy(
            clientCompany = client.company,
            clientContact = client.contact.ifBlank { it.clientContact },
            clientEmail = client.email.ifBlank { it.clientEmail },
            clientPhone = client.phone.ifBlank { it.clientPhone },
            clientInn = client.inn.ifBlank { it.clientInn },
        )
    }

    fun loadHistory() {
        val config = _syncConfig.value
        _historyError.value = null
        if (!config.enabled) {
            _history.value = archiveItems()
            return
        }
        viewModelScope.launch {
            try {
                _history.value = SheetClient(config).quotes(200).map(::historyItem)
            } catch (e: Exception) {
                _historyError.value = e.message ?: "Нет связи с таблицей"
                if (_history.value == null) _history.value = archiveItems()
            }
        }
    }

    private fun historyItem(q: RemoteQuote) = HistoryItem(
        id = q.id, number = q.number, date = q.date, month = q.month, client = q.client,
        total = BigDecimal.valueOf(q.total), profit = q.profit?.let(BigDecimal::valueOf),
        status = q.status, author = q.author, data = q.data, validUntil = q.validUntil,
        products = q.products.mapValues { BigDecimal.valueOf(it.value) },
    )

    // ---------- Все КП: долги и отчёт не ограничены последними 200 ----------

    private val _deals = MutableStateFlow<List<HistoryItem>?>(null)
    /** Все КП таблицы (без данных черновика) — для долгов; без таблицы — архив телефона. */
    val deals: StateFlow<List<HistoryItem>?> = _deals.asStateFlow()

    private val _mySalary = MutableStateFlow<SalaryData?>(null)
    /** Заработок менеджера в этом месяце (для главного экрана). */
    val mySalary: StateFlow<SalaryData?> = _mySalary.asStateFlow()
    private var salaryLoadedAt = 0L

    fun loadMySalaryIfStale(maxAgeMs: Long = 10 * 60_000L) {
        if (System.currentTimeMillis() - salaryLoadedAt < maxAgeMs) return
        loadMySalary()
    }

    fun loadMySalary() {
        val config = _syncConfig.value
        if (!config.enabled) return
        salaryLoadedAt = System.currentTimeMillis()
        viewModelScope.launch {
            _mySalary.value = runCatching { SalaryData.parse(SheetClient(config).salary()) }.getOrNull()
        }
    }

    private var dealsLoadedAt = 0L

    private fun dealsCache() = java.io.File(getApplication<Application>().filesDir, "deals_cache.json")

    /** Для панели «Сегодня»: не чаще раза в [maxAgeMs], чтобы главный экран не ждал таблицу. */
    fun loadDealsIfStale(maxAgeMs: Long = 5 * 60_000L) {
        // Сразу показываем сохранённую копию — графики и «Сегодня» не ждут таблицу.
        if (_deals.value == null && _syncConfig.value.enabled) {
            _deals.value = runCatching { parseQuotes(JSONArray(dealsCache().readText())).map(::historyItem) }.getOrNull()
        }
        if (_deals.value != null && System.currentTimeMillis() - dealsLoadedAt < maxAgeMs) return
        loadDeals()
    }

    fun loadDeals() {
        dealsLoadedAt = System.currentTimeMillis()
        val config = _syncConfig.value
        if (!config.enabled) {
            _deals.value = archiveItems()
            return
        }
        viewModelScope.launch {
            try {
                val raw = SheetClient(config).quotesJson(light = true)
                _deals.value = parseQuotes(raw).map(::historyItem)
                runCatching { dealsCache().writeText(raw.toString()) }
            } catch (e: Exception) {
                _historyError.value = e.message ?: "Нет связи с таблицей"
            }
        }
    }

    private val _months = MutableStateFlow<Map<String, List<HistoryItem>>>(emptyMap())
    val months: StateFlow<Map<String, List<HistoryItem>>> = _months.asStateFlow()

    /** Все КП месяца из таблицы — для отчёта. */
    fun loadMonth(month: String) {
        val config = _syncConfig.value
        if (!config.enabled) return
        viewModelScope.launch {
            try {
                val items = SheetClient(config).quotes(month = month).map(::historyItem)
                _months.update { it + (month to items) }
            } catch (e: Exception) {
                _historyError.value = e.message ?: "Нет связи с таблицей"
            }
        }
    }

    /** Полное КП (с черновиком) для документов: из истории или, у старых КП, загружаем отдельно. */
    fun fullItem(item: HistoryItem): HistoryItem =
        if (item.data.isNotBlank()) item else _history.value?.firstOrNull { it.id == item.id } ?: item

    private fun archiveItems(): List<HistoryItem> {
        val dateFormat = java.text.SimpleDateFormat("dd.MM.yyyy HH:mm", java.util.Locale.getDefault())
        val monthFormat = java.text.SimpleDateFormat("yyyy-MM", java.util.Locale.US)
        return store.loadArchive().sortedByDescending { it.createdAt }.map { a ->
            HistoryItem(
                id = a.id, number = a.number, date = dateFormat.format(java.util.Date(a.createdAt)),
                month = monthFormat.format(java.util.Date(a.createdAt)), client = a.client, total = a.total,
                profit = a.profit, status = a.status, author = a.manager, data = a.data,
                validUntil = a.validUntil ?: 0, products = a.products,
            )
        }
    }

    /** Меняет статус КП (в таблице и в архиве телефона); `null` — успех, иначе текст ошибки. */
    suspend fun setStatus(item: HistoryItem, status: QuoteStatus): String? {
        val config = _syncConfig.value
        if (config.enabled) {
            try {
                SheetClient(config).setStatus(item.id, status)?.error?.let {
                    _notices.tryEmit("Статус сохранён, но в МойСклад не изменён: $it")
                }
            } catch (e: Exception) {
                return e.message ?: "Нет связи с таблицей"
            }
        }
        val archive = store.loadArchive()
        if (archive.any { it.id == item.id }) {
            store.saveArchive(archive.map { if (it.id == item.id) it.copy(status = status) else it })
        }
        if (status != QuoteStatus.SENT) QuoteReminders.cancel(getApplication(), item.id)
        _history.value = _history.value?.map { if (it.id == item.id) it.copy(status = status) else it }
        return null
    }

    /** Повтор заказа: копия КП как новое КП (номер выдаётся заново, цены — по текущему прайсу). */
    fun repeatQuote(item: HistoryItem): Boolean {
        val draft = QuoteStore.draftFromJson(item.data) ?: return false
        val local = !_syncConfig.value.enabled
        updateDraft {
            draft.copy(
                id = UUID.randomUUID().toString(),
                number = if (local) (store.loadArchive().maxOfOrNull { a -> a.number } ?: it.number) + 1 else 0,
                saved = false,
                lines = draft.lines.map { l -> l.copy(id = newId()) },
                snapshot = emptyList(),
            )
        }
        downloadMissingPhotos()
        return true
    }

    fun report(month: String): MonthReport? {
        val items = (if (_syncConfig.value.enabled) _months.value[month] else null) ?: _history.value ?: return null
        return ReportCalculator.month(
            items.map { QuoteSummary(it.month, it.status, it.total, it.profit, it.author, it.products) },
            month,
        )
    }

    fun orderYarn(views: List<DraftLineView>): OrderYarn =
        OrderYarnCalculator.calculate(views.mapNotNull { it.line }, _settings.value.yarnWaste, parsedExtras.yarnPrices)

    /** Открывает сохранённое КП для просмотра, изменения или повторной отправки. */
    fun openQuote(item: HistoryItem): Boolean {
        val draft = QuoteStore.draftFromJson(item.data) ?: return false
        updateDraft { draft.copy(id = item.id.ifBlank { draft.id }, number = item.number, saved = true) }
        downloadMissingPhotos()
        return true
    }

    // ---------- Документы по сделке ----------

    /** Сделка из истории: позиции с ценами на момент сохранения (у старых КП — по текущему прайсу). */
    fun deal(item: HistoryItem): DealDoc? {
        // У старых КП из «всех КП» нет черновика — документ без позиций (счёт одной строкой).
        val full = fullItem(item)
        val draft = if (full.data.isBlank()) QuoteDraft(clientCompany = full.client) else QuoteStore.draftFromJson(full.data) ?: return null
        val settings = _settings.value
        val lines = draft.snapshot.ifEmpty {
            lineViews(draft, _catalog.value, settings).mapNotNull { v ->
                v.line?.let { SnapshotLine(it.description, it.quantity, v.product.unit, it.unitPrice, it.total) }
            }
        }
        val total = item.total
        return DealDoc(
            quoteId = item.id,
            quoteNumber = item.number,
            quoteDate = item.date.substringBefore(' '),
            client = item.client.ifBlank { draft.clientCompany },
            clientInn = draft.clientInn,
            clientKpp = draft.clientKpp,
            clientAddress = draft.clientAddress,
            clientEmail = draft.clientEmail,
            lines = lines,
            total = total,
            vat = DocPdf.vatIn(total, settings),
        )
    }

    /** Изделия и пряжа заказа на производство (пряжа — с учётом брака, по весу и составу изделий). */
    fun orderContent(item: HistoryItem): Pair<String, List<com.knit.calculator.core.YarnAmount>> {
        val draft = QuoteStore.draftFromJson(item.data) ?: return "" to emptyList()
        val views = lineViews(draft, _catalog.value, _settings.value)
        val items = draft.snapshot.ifEmpty {
            views.mapNotNull { v -> v.line?.let { SnapshotLine(it.description, it.quantity, v.product.unit, it.unitPrice, it.total) } }
        }.joinToString("\n") { "${it.name} — ${QuoteCalculator.formatQuantity(it.quantity)} ${it.unit}" }
        val yarn = orderYarn(views).needs.map { com.knit.calculator.core.YarnAmount(it.yarn, it.totalKg) }
        return items to yarn
    }

    /** Текст договора: лист «Договор» таблицы или текст по умолчанию. */
    fun contractParagraphs(): List<String> {
        val c = cache
        return if (_syncConfig.value.enabled && c != null) com.knit.calculator.core.ContractTemplate.fromSheet(c.contract)
        else com.knit.calculator.core.ContractTemplate.DEFAULT
    }

    /** Названия пряжи из листа «Пряжа» — подсказки для склада. */
    fun yarnNames(): List<String> = parsedExtras.yarnPrices.keys.map { k -> k.replaceFirstChar { it.uppercaseChar() } }

    /** Документ (счёт, договор, прайс-лист) — клиенту письмом через Google: `null` — отправлено. */
    suspend fun sendDocument(file: java.io.File, to: String, subject: String, text: String, kind: String, quoteId: String, invoiceNumber: Int?): String? {
        val config = _syncConfig.value
        if (!config.enabled) return "Google Таблица не подключена"
        return try {
            val bytes = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { file.readBytes() }
            SheetClient(config).sendEmail(quoteId, to, subject, text, file.name, bytes, kind, invoiceNumber)
            null
        } catch (e: Exception) {
            e.message ?: "Нет связи с Google"
        }
    }

    /** Копия документа в папку «Документы» Диска (в фоне). */
    fun uploadDocument(file: java.io.File, invoiceNumber: Int?) {
        val config = _syncConfig.value
        if (!config.enabled) return
        viewModelScope.launch {
            runCatching {
                val bytes = kotlinx.coroutines.withContext(kotlinx.coroutines.Dispatchers.IO) { file.readBytes() }
                SheetClient(config).uploadFile("doc", file.name, bytes, "application/pdf", invoiceNumber = invoiceNumber)
            }
        }
    }

    // ---------- Расчёт ----------

    fun lineViews(draft: QuoteDraft, catalog: List<Product>, settings: CompanySettings): List<DraftLineView> {
        val costSettings = settings.costSettings()
        val max = settings.maxDiscount
        return draft.lines.mapNotNull { d ->
            val product = catalog.firstOrNull { it.id == d.productId } ?: msIndex[d.productId] ?: return@mapNotNull null
            val qty = YarnCalculator.parseDecimal(d.quantity)?.takeIf { it.signum() > 0 }
            val wanted = YarnCalculator.parseDecimal(d.discount)?.max(BigDecimal.ZERO) ?: BigDecimal.ZERO
            val discount = wanted.min(max)
            val line = qty?.let { QuoteCalculator.line(QuoteLineInput(product, d.selected, it, discount)) }
            val economics = line?.let {
                val buy = product.buyPrice
                // Товар МойСклад: себестоимость — закупочная цена (уже полная, без доли постоянных расходов).
                if (buy != null) CostCalculator.line(it, com.knit.calculator.core.ProductCost(yarnPerUnit = buy), costSettings.copy(fixedMonthly = BigDecimal.ZERO), emptyMap())
                else CostCalculator.line(it, parsedExtras.costs[product.code.lowercase()], costSettings, parsedExtras.yarnPrices)
            }
            DraftLineView(d, product, line, economics, discountTooHigh = wanted > max)
        }
    }

    fun totals(views: List<DraftLineView>, settings: CompanySettings): QuoteTotals =
        QuoteCalculator.totals(views.mapNotNull { it.line }, settings.vat())

    // ---------- Черновик КП ----------

    fun updateDraft(transform: (QuoteDraft) -> QuoteDraft) {
        _draft.value = transform(_draft.value)
        store.saveDraft(_draft.value)
    }

    private val _favorites = MutableStateFlow(store.favoriteMs)
    val favorites: StateFlow<Set<String>> = _favorites.asStateFlow()
    private val _recent = MutableStateFlow(store.recentMs)
    val recent: StateFlow<List<String>> = _recent.asStateFlow()

    fun toggleFavorite(product: Product) {
        val id = product.externalId.ifBlank { return }
        val next = if (id in _favorites.value) _favorites.value - id else _favorites.value + id
        _favorites.value = next
        store.favoriteMs = next
    }

    /** Товар МойСклад выбран — в «Недавние». */
    fun rememberPicked(product: Product) {
        val id = product.externalId.ifBlank { return }
        val next = (listOf(id) + _recent.value.filterNot { it == id }).take(12)
        _recent.value = next
        store.recentMs = next
    }

    fun addLine(product: Product) = updateDraft { d ->
        rememberPicked(product)
        val quantity = product.minOrder.takeIf { it.signum() > 0 }
            ?.stripTrailingZeros()?.toPlainString()?.replace('.', ',')
            .orEmpty()
        d.copy(lines = d.lines + DraftLine(newId(), product.id, quantity = quantity))
    }

    fun updateLine(id: Long, transform: (DraftLine) -> DraftLine) = updateDraft { d ->
        d.copy(lines = d.lines.map { if (it.id == id) transform(it) else it })
    }

    /** Удалённая позиция и её место — для «Отменить» (несколько секунд после удаления). */
    private val _removed = MutableStateFlow<Pair<Int, DraftLine>?>(null)
    val removed: StateFlow<Pair<Int, DraftLine>?> = _removed.asStateFlow()

    fun removeLine(id: Long) {
        val d = _draft.value
        val index = d.lines.indexOfFirst { it.id == id }
        if (index < 0) return
        _removed.value?.second?.photoPath?.let { PhotoStore.delete(it) }
        _removed.value = index to d.lines[index]
        updateDraft { it.copy(lines = it.lines.filterNot { l -> l.id == id }) }
        undoTimer?.cancel()
        undoTimer = viewModelScope.launch {
            kotlinx.coroutines.delay(6_000)
            forgetRemoved()
        }
    }

    private var undoTimer: kotlinx.coroutines.Job? = null

    private val _templates = MutableStateFlow(store.loadTemplates())
    val templates: StateFlow<List<QuoteTemplate>> = _templates.asStateFlow()

    /** Позиции текущего КП — в шаблон (с тем же названием — заменяется). */
    fun saveTemplate(name: String) {
        val clean = name.trim().ifBlank { return }
        val lines = _draft.value.lines.map { it.copy(id = 0, photoPath = null, photoFileId = null) }
        if (lines.isEmpty()) return
        val next = listOf(QuoteTemplate(clean, lines)) + _templates.value.filterNot { it.name.equals(clean, ignoreCase = true) }
        _templates.value = next
        store.saveTemplates(next)
    }

    fun deleteTemplate(name: String) {
        val next = _templates.value.filterNot { it.name == name }
        _templates.value = next
        store.saveTemplates(next)
    }

    /** Позиции шаблона — в конец КП (товары, которых больше нет в каталоге, пропускаются). */
    fun applyTemplate(t: QuoteTemplate): Int {
        val known = (_catalog.value + _msProducts.value).map { it.id }.toSet()
        val lines = t.lines.filter { it.productId in known }.map { it.copy(id = newId()) }
        updateDraft { it.copy(lines = it.lines + lines) }
        return lines.size
    }

    /** Копия позиции сразу после неё (без фото): тот же товар — меняют размер, цвет или количество. */
    fun duplicateLine(id: Long) = updateDraft { d ->
        val index = d.lines.indexOfFirst { it.id == id }
        if (index < 0) d
        else d.copy(lines = d.lines.toMutableList().apply { add(index + 1, d.lines[index].copy(id = newId(), photoPath = null, photoFileId = null)) })
    }

    fun undoRemove() {
        val (index, line) = _removed.value ?: return
        undoTimer?.cancel()
        _removed.value = null
        updateDraft { it.copy(lines = it.lines.toMutableList().apply { add(index.coerceIn(0, size), line) }) }
    }

    /** Время на «Отменить» вышло: фото удалённой позиции больше не нужно. */
    fun forgetRemoved() {
        _removed.value?.second?.photoPath?.let { PhotoStore.delete(it) }
        _removed.value = null
    }

    /** Реквизиты клиента по ИНН (DaData): название, КПП, адрес; контакт — директор, если пусто. `null` — успех. */
    suspend fun lookupInn(): String? {
        val config = _syncConfig.value
        if (!config.enabled) return "Нужно подключение к Google Таблице"
        return try {
            val p = SheetClient(config).innLookup(_draft.value.clientInn)
            updateDraft {
                it.copy(
                    clientCompany = p.optString("name").ifBlank { it.clientCompany },
                    clientKpp = p.optString("kpp").ifBlank { it.clientKpp },
                    clientAddress = p.optString("address").ifBlank { it.clientAddress },
                    clientContact = it.clientContact.ifBlank { p.optString("director") },
                )
            }
            if (p.optString("status").let { s -> s.isNotBlank() && s != "ACTIVE" }) "Внимание: организация не действует (${p.optString("status")})" else null
        } catch (e: Exception) {
            e.message ?: "DaData недоступна"
        }
    }

    /** Новое КП. В локальном режиме номер увеличивается на 1, с таблицей — выдаётся при сохранении. */
    fun newQuote() = updateDraft {
        val next = maxOf(it.number, store.loadArchive().maxOfOrNull { a -> a.number } ?: 0) + 1
        QuoteDraft(id = UUID.randomUUID().toString(), number = if (_syncConfig.value.enabled) 0 else next)
    }

    private fun dropMissingLines() {
        // Товары МойСклад могут быть ещё не загружены — такие строки не трогаем.
        val msPending = _syncConfig.value.enabled && _msProducts.value.isEmpty()
        val ids = (_catalog.value + _msProducts.value).map { it.id }.toSet()
        updateDraft { d -> d.copy(lines = d.lines.filter { it.productId in ids || msPending }) }
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

/**
 * «Бесплатно по Москве и Московской области» или «Бесплатно по Москве и Московской области при заказе от 50 000,00 ₽»;
 * пусто, если порог не задан.
 */
fun deliveryText(totals: QuoteTotals, settings: CompanySettings): String {
    val threshold = settings.freeDeliveryThreshold ?: return ""
    val free = listOf("Бесплатно", settings.deliveryArea.trim()).filter { it.isNotEmpty() }.joinToString(" ")
    return if (totals.total >= threshold) free
    else "$free при заказе от ${QuoteCalculator.formatMoney(threshold)} ₽"
}
