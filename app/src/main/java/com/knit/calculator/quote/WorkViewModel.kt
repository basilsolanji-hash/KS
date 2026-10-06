package com.knit.calculator.quote

import android.app.Application
import android.content.Context
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.knit.calculator.core.WorkShift
import com.knit.calculator.core.WorkTime
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

/**
 * Рабочее время: смена начинается сама при первом входе за день, «Закрыть смену» — в конце дня.
 * Смены хранятся на телефоне и отправляются в таблицу (лист «Рабочее время»); без связи — отправятся позже.
 */
class WorkViewModel(application: Application) : AndroidViewModel(application) {
    private val store = QuoteStore(application)
    private val prefs = application.getSharedPreferences("work", Context.MODE_PRIVATE)

    private val _shifts = MutableStateFlow(load())
    /** Свои смены на этом телефоне (последние 120). */
    val shifts: StateFlow<List<WorkShift>> = _shifts.asStateFlow()

    private val _team = MutableStateFlow<List<WorkShift>?>(null)
    /** Смены месяца из таблицы: директору — все сотрудники. */
    val team: StateFlow<List<WorkShift>?> = _team.asStateFlow()
    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error.asStateFlow()

    /** Имя сотрудника на этом телефоне (из подключения к таблице). */
    fun person(): String {
        // Подключён сервер фабрики — имя оттуда.
        val server = com.knit.calculator.staff.ServerStore(getApplication()).me
            ?.let { runCatching { JSONObject(it).getJSONObject("me").optString("name") }.getOrNull() }
        return server?.takeIf { it.isNotBlank() } ?: store.loadSyncConfig().manager.split(" / ").first().trim().ifBlank { "Директор" }
    }

    /** Вход в приложение: смена начинается сама, если сегодня её ещё не было. */
    fun autoStart() {
        val now = System.currentTimeMillis()
        if (WorkTime.shouldAutoStart(_shifts.value, now)) start()
        else sync()
    }

    fun start() {
        val now = System.currentTimeMillis()
        if (WorkTime.current(_shifts.value, now) != null) return
        save(listOf(WorkShift("sh-" + UUID.randomUUID().toString().take(12), person(), now)) + _shifts.value, dirty = setOf(0))
    }

    fun close() {
        val now = System.currentTimeMillis()
        val cur = WorkTime.current(_shifts.value, now) ?: return
        val list = _shifts.value.map { if (it.id == cur.id) it.copy(end = now) else it }
        save(list, dirty = setOf(list.indexOfFirst { it.id == cur.id }))
    }

    /** Директор: смены сотрудников за месяц. */
    fun loadTeam(month: String) {
        val config = store.loadSyncConfig()
        val server = com.knit.calculator.staff.ServerStore(getApplication())
        if (server.connected) {
            viewModelScope.launch {
                try {
                    val a = com.knit.calculator.staff.ServerClient(server.url, server.token).call("shifts", JSONObject().put("month", month)).optJSONArray("shifts") ?: JSONArray()
                    _team.value = (0 until a.length()).mapNotNull { a.optJSONObject(it) }.map {
                        WorkShift(it.optString("id"), it.optString("person"), it.optLong("start"), if (it.isNull("end")) null else it.optLong("end"), suspicious = it.optBoolean("suspicious"))
                    }
                    _error.value = null
                } catch (e: Exception) {
                    _error.value = e.message ?: "Нет связи с сервером фабрики"
                }
            }
            return
        }
        if (!config.enabled) {
            _team.value = _shifts.value.filter { WorkTime.monthKey(it.start) == month }
            return
        }
        viewModelScope.launch {
            try {
                val a = SheetClient(config).shifts(month)
                _team.value = (0 until a.length()).mapNotNull { a.optJSONObject(it) }.map {
                    WorkShift(
                        it.optString("id"), it.optString("person"), it.optLong("start"), if (it.isNull("end")) null else it.optLong("end"),
                        suspicious = it.optBoolean("suspicious"),
                    )
                }
                _error.value = null
            } catch (e: Exception) {
                _error.value = e.message ?: "Нет связи с таблицей"
            }
        }
    }

    /** Директор закрывает забытую смену: [end] — время конца. */
    fun fixShift(s: WorkShift, end: Long, month: String) {
        val config = store.loadSyncConfig()
        if (!config.enabled) return
        viewModelScope.launch {
            try {
                SheetClient(config).shiftSave(JSONObject().put("id", s.id).put("start", s.start).put("end", end))
                loadTeam(month)
            } catch (e: Exception) {
                _error.value = e.message ?: "Нет связи с таблицей"
            }
        }
    }

    // ---------- Хранение и отправка ----------

    private fun save(list: List<WorkShift>, dirty: Set<Int>) {
        val trimmed = list.sortedByDescending { it.start }.take(120)
        _shifts.value = trimmed
        val pending = pendingIds() + dirty.mapNotNull { list.getOrNull(it)?.id }
        val a = JSONArray()
        trimmed.forEach { a.put(JSONObject().put("id", it.id).put("person", it.person).put("start", it.start).put("end", it.end ?: JSONObject.NULL)) }
        prefs.edit().putString("shifts", a.toString()).putStringSet("pending", pending.filter { id -> trimmed.any { it.id == id } }.toSet()).apply()
        sync()
    }

    private fun pendingIds(): Set<String> = prefs.getStringSet("pending", emptySet()).orEmpty()

    /** Неотправленные смены — на сервер фабрики (если подключён), иначе в таблицу. */
    fun sync() {
        val config = store.loadSyncConfig()
        val server = com.knit.calculator.staff.ServerStore(getApplication())
        val ids = pendingIds()
        if ((!config.enabled && !server.connected) || ids.isEmpty()) return
        viewModelScope.launch {
            val sent = mutableSetOf<String>()
            _shifts.value.filter { it.id in ids }.forEach { s ->
                val shift = JSONObject().put("id", s.id).put("start", s.start).put("end", s.end ?: JSONObject.NULL)
                runCatching {
                    if (server.connected) com.knit.calculator.staff.ServerClient(server.url, server.token).call("shiftSave", JSONObject().put("shift", shift))
                    else SheetClient(config).shiftSave(shift)
                }.onSuccess { sent += s.id }
            }
            if (sent.isNotEmpty()) prefs.edit().putStringSet("pending", pendingIds() - sent).apply()
        }
    }

    private fun load(): List<WorkShift> = runCatching {
        val a = JSONArray(prefs.getString("shifts", "[]"))
        (0 until a.length()).map { a.getJSONObject(it) }.map {
            WorkShift(it.getString("id"), it.optString("person"), it.getLong("start"), if (it.isNull("end")) null else it.getLong("end"))
        }
    }.getOrDefault(emptyList())
}
