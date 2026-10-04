package com.knit.calculator.quote

import android.app.Application
import androidx.lifecycle.AndroidViewModel
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
import java.math.BigDecimal

/** Строка КП вместе с расчётом (или `null`, если количество ещё не введено). */
data class DraftLineView(val draft: DraftLine, val product: Product, val line: QuoteLine?)

class QuoteViewModel(application: Application) : AndroidViewModel(application) {
    private val store = QuoteStore(application)

    private val _catalog = MutableStateFlow(store.loadCatalog())
    val catalog: StateFlow<List<Product>> = _catalog.asStateFlow()

    private val _settings = MutableStateFlow(store.loadSettings())
    val settings: StateFlow<CompanySettings> = _settings.asStateFlow()

    private val _draft = MutableStateFlow(store.loadDraft())
    val draft: StateFlow<QuoteDraft> = _draft.asStateFlow()

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

    /** Новое КП: следующий номер, пустые клиент и позиции. */
    fun newQuote() = updateDraft { QuoteDraft(number = it.number + 1) }

    // ---------- Ассортимент ----------

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
        saveProduct(product)
        _editing.value = null
        return true
    }

    fun cancelEditing() {
        _editing.value = null
    }

    fun saveProduct(product: Product) {
        val list = _catalog.value
        _catalog.value = if (list.any { it.id == product.id }) list.map { if (it.id == product.id) product else it } else list + product
        store.saveCatalog(_catalog.value)
    }

    fun deleteProduct(id: Long) {
        _catalog.value = _catalog.value.filterNot { it.id == id }
        store.saveCatalog(_catalog.value)
        updateDraft { d -> d.copy(lines = d.lines.filterNot { it.productId == id }) }
    }

    fun moveProduct(id: Long, delta: Int) {
        val list = _catalog.value.toMutableList()
        val from = list.indexOfFirst { it.id == id }
        val to = from + delta
        if (from < 0 || to !in list.indices) return
        list.add(to, list.removeAt(from))
        _catalog.value = list
        store.saveCatalog(list)
    }

    fun resetCatalog() {
        _catalog.value = DefaultCatalog.products
        store.saveCatalog(_catalog.value)
        val ids = _catalog.value.map { it.id }.toSet()
        updateDraft { d -> d.copy(lines = d.lines.filter { it.productId in ids }) }
    }

    // ---------- Реквизиты ----------

    fun updateSettings(transform: (CompanySettings) -> CompanySettings) {
        _settings.value = transform(_settings.value)
        store.saveSettings(_settings.value)
    }

    fun resetSettings() = updateSettings { CompanySettings() }
}

fun CompanySettings.vat(): VatSettings =
    VatSettings(YarnCalculator.parseDecimal(vatRate)?.max(BigDecimal.ZERO) ?: BigDecimal.ZERO, vatIncluded)
