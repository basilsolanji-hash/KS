package com.knit.calculator.label

import android.app.Application
import android.content.Context
import androidx.lifecycle.AndroidViewModel
import com.knit.calculator.core.LabelSpec
import com.knit.calculator.core.PrinterLanguage
import com.knit.calculator.core.Product
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Этикетка в очереди печати: товар, копии и количество в упаковке. */
data class LabelJob(val product: Product, val copies: Int = 1, val pack: String = "")

/** Принтер и параметры этикетки (запоминаются на телефоне). */
data class PrinterSettings(
    val address: String = "",
    val name: String = "",
    val spec: LabelSpec = LabelSpec(),
)

class LabelStore(context: Context) {
    private val prefs = context.getSharedPreferences("labels", Context.MODE_PRIVATE)

    fun load(): PrinterSettings = PrinterSettings(
        address = prefs.getString("address", "").orEmpty(),
        name = prefs.getString("name", "").orEmpty(),
        spec = LabelSpec(
            dpi = prefs.getInt("dpi", 203),
            gapMm = prefs.getInt("gap", 2),
            density = prefs.getInt("density", 8),
            language = runCatching { PrinterLanguage.valueOf(prefs.getString("language", "").orEmpty()) }.getOrDefault(PrinterLanguage.TSPL),
            rotated = prefs.getBoolean("rotated", true),
        ),
    )

    fun save(s: PrinterSettings) {
        prefs.edit()
            .putString("address", s.address).putString("name", s.name)
            .putInt("dpi", s.spec.dpi).putInt("gap", s.spec.gapMm).putInt("density", s.spec.density)
            .putString("language", s.spec.language.name).putBoolean("rotated", s.spec.rotated)
            .apply()
    }
}

/** Очередь этикеток и настройки принтера. Очередь заполняется с экрана или из заказа (история КП). */
class LabelViewModel(application: Application) : AndroidViewModel(application) {
    private val store = LabelStore(application)

    private val _jobs = MutableStateFlow<List<LabelJob>>(emptyList())
    val jobs: StateFlow<List<LabelJob>> = _jobs.asStateFlow()

    private val _printer = MutableStateFlow(store.load())
    val printer: StateFlow<PrinterSettings> = _printer.asStateFlow()

    /** Дата изготовления на этикетке: месяц.год. */
    private val _madeDate = MutableStateFlow(SimpleDateFormat("MM.yyyy", Locale.getDefault()).format(Date()))
    val madeDate: StateFlow<String> = _madeDate.asStateFlow()

    fun setMadeDate(v: String) { _madeDate.value = v.take(10) }

    fun add(product: Product) {
        _jobs.update { list -> if (list.any { it.product.id == product.id }) list else list + LabelJob(product) }
    }

    fun setJobs(jobs: List<LabelJob>) { _jobs.value = jobs }

    fun update(index: Int, job: LabelJob) { _jobs.update { list -> list.mapIndexed { i, j -> if (i == index) job else j } } }

    fun remove(index: Int) { _jobs.update { list -> list.filterIndexed { i, _ -> i != index } } }

    /** Штрихкод создан в МойСклад — обновляем товар во всех строках. */
    fun barcodeCreated(productId: Long, code: String) {
        _jobs.update { list -> list.map { if (it.product.id == productId) it.copy(product = it.product.copy(barcode = code)) else it } }
    }

    fun setPrinter(s: PrinterSettings) {
        _printer.value = s
        store.save(s)
    }
}
