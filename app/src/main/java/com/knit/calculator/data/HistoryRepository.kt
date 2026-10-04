package com.knit.calculator.data

import android.content.Context
import com.knit.calculator.core.HistoryEntry
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject

/**
 * Локальное хранилище истории вычислений (SharedPreferences + JSON).
 * Данные не покидают устройство: резервное копирование отключено в манифесте.
 */
class HistoryRepository(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    fun load(): List<HistoryEntry> {
        val json = prefs.getString(KEY_ENTRIES, null) ?: return emptyList()
        return try {
            val array = JSONArray(json)
            (0 until array.length()).mapNotNull { i ->
                val item = array.optJSONObject(i) ?: return@mapNotNull null
                val expression = item.optString(FIELD_EXPRESSION)
                val result = item.optString(FIELD_RESULT)
                if (expression.isEmpty() || result.isEmpty()) null
                else HistoryEntry(expression, result, item.optLong(FIELD_TIME))
            }.take(MAX_ENTRIES)
        } catch (e: JSONException) {
            emptyList()
        }
    }

    fun save(entries: List<HistoryEntry>) {
        val array = JSONArray()
        entries.take(MAX_ENTRIES).forEach { entry ->
            array.put(
                JSONObject()
                    .put(FIELD_EXPRESSION, entry.expression)
                    .put(FIELD_RESULT, entry.result)
                    .put(FIELD_TIME, entry.timestamp),
            )
        }
        prefs.edit().putString(KEY_ENTRIES, array.toString()).apply()
    }

    companion object {
        const val MAX_ENTRIES = 100
        private const val PREFS = "calculator_history"
        private const val KEY_ENTRIES = "entries"
        private const val FIELD_EXPRESSION = "e"
        private const val FIELD_RESULT = "r"
        private const val FIELD_TIME = "t"
    }
}
