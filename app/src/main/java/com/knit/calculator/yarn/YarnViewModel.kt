package com.knit.calculator.yarn

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import com.knit.calculator.core.OrderUnit
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.core.YarnComponent
import com.knit.calculator.core.YarnInput
import com.knit.calculator.core.YarnOutcome
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.math.BigDecimal

/** Дополнительная нить в форме: доп. 1, доп. 2, спандекс (резинка)… */
data class ExtraYarnForm(val id: Long, val name: String, val percent: String)

/** Поля формы в том виде, как их ввёл пользователь. */
data class YarnForm(
    val productName: String = "",
    val orderNumber: String = "",
    val itemWeight: String = "",
    val orderAmount: String = "",
    val orderUnit: OrderUnit = OrderUnit.PIECES,
    val waste: String = "3",
    val mainName: String = "",
    val extras: List<ExtraYarnForm> = emptyList(),
) {
    /** Пустое поле считается нулём, некорректное — `null` (ошибка ввода). */
    fun toInput(defaultMainName: String): YarnInput? {
        fun num(s: String): BigDecimal? = if (s.isBlank()) BigDecimal.ZERO else YarnCalculator.parseDecimal(s)
        return YarnInput(
            itemWeightGrams = num(itemWeight) ?: return null,
            orderAmount = num(orderAmount) ?: return null,
            orderUnit = orderUnit,
            wastePercent = num(waste) ?: return null,
            mainName = mainName.ifBlank { defaultMainName },
            extras = extras.map { YarnComponent(it.name.ifBlank { "—" }, num(it.percent) ?: return null) },
        )
    }
}

/**
 * Состояние экрана расхода пряжи. Форма сохраняется на устройстве, поэтому
 * переживает поворот экрана, закрытие приложения и перезапуск.
 */
class YarnViewModel(application: Application) : AndroidViewModel(application) {
    private val prefs = application.getSharedPreferences(PREFS, Application.MODE_PRIVATE)

    private val _form = MutableStateFlow(load())
    val form: StateFlow<YarnForm> = _form.asStateFlow()

    fun calculate(form: YarnForm, defaultMainName: String): YarnOutcome? =
        form.toInput(defaultMainName)?.let(YarnCalculator::calculate)

    fun update(transform: (YarnForm) -> YarnForm) {
        _form.update(transform)
        save(_form.value)
    }

    fun addExtra(name: String, percent: String = "") = update { f ->
        val id = (f.extras.maxOfOrNull { it.id } ?: 0L) + 1
        f.copy(extras = f.extras + ExtraYarnForm(id, name, percent))
    }

    fun updateExtra(id: Long, transform: (ExtraYarnForm) -> ExtraYarnForm) = update { f ->
        f.copy(extras = f.extras.map { if (it.id == id) transform(it) else it })
    }

    fun removeExtra(id: Long) = update { f -> f.copy(extras = f.extras.filterNot { it.id == id }) }

    /** Новый заказ: очищаем данные заказа, но оставляем состав нитей и % брака как шаблон. */
    fun resetOrder() = update { it.copy(productName = "", orderNumber = "", orderAmount = "") }

    private fun save(form: YarnForm) {
        val extras = JSONArray()
        form.extras.forEach {
            extras.put(JSONObject().put("id", it.id).put("name", it.name).put("percent", it.percent))
        }
        val json = JSONObject()
            .put("productName", form.productName)
            .put("orderNumber", form.orderNumber)
            .put("itemWeight", form.itemWeight)
            .put("orderAmount", form.orderAmount)
            .put("orderUnit", form.orderUnit.name)
            .put("waste", form.waste)
            .put("mainName", form.mainName)
            .put("extras", extras)
        prefs.edit().putString(KEY_FORM, json.toString()).apply()
    }

    private fun load(): YarnForm {
        val raw = prefs.getString(KEY_FORM, null) ?: return YarnForm()
        return try {
            val json = JSONObject(raw)
            val extras = json.optJSONArray("extras") ?: JSONArray()
            YarnForm(
                productName = json.optString("productName"),
                orderNumber = json.optString("orderNumber"),
                itemWeight = json.optString("itemWeight"),
                orderAmount = json.optString("orderAmount"),
                orderUnit = OrderUnit.entries.firstOrNull { it.name == json.optString("orderUnit") } ?: OrderUnit.PIECES,
                waste = json.optString("waste", "3"),
                mainName = json.optString("mainName"),
                extras = (0 until extras.length()).mapNotNull { i ->
                    extras.optJSONObject(i)?.let {
                        ExtraYarnForm(it.optLong("id", i.toLong() + 1), it.optString("name"), it.optString("percent"))
                    }
                },
            )
        } catch (e: JSONException) {
            YarnForm()
        }
    }

    private companion object {
        const val PREFS = "yarn_calculator"
        const val KEY_FORM = "form"
    }
}
