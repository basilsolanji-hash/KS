package com.knit.calculator.staff

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject

/** Этап заказа: кто начал и закончил, когда, сколько штук. */
data class StageRun(
    val id: Int,
    val stage: String,
    val startedBy: String?,
    val startedAt: Long?,
    val finishedBy: String?,
    val finishedAt: Long?,
    val quantity: Int,
    val comment: String,
) {
    val running: Boolean get() = finishedAt == null
    val minutes: Long? get() = if (startedAt != null && finishedAt != null) (finishedAt - startedAt) / 60_000 else null
}

/** Утверждённая версия техкарты, снимок которой закреплён за производственным заданием. */
data class JobTechCard(val code: String, val name: String, val version: Int)

data class Job(
    val id: Int,
    val title: String,
    val client: String,
    val quantity: Int,
    val deadline: Long?,
    val techCard: JobTechCard?,
    val stages: List<StageRun>,
)

/** График: дни недели или сменный цикл «[cycleOn] через [cycleOff]» от даты [anchor] (2/2 и т. п.). */
data class Schedule(val days: List<Int>, val start: String, val end: String, val cycleOn: Int = 0, val cycleOff: Int = 0, val anchor: String = "") {
    val cycle: Boolean get() = cycleOn > 0
    fun toJson(): JSONObject {
        val o = JSONObject().put("days", JSONArray(days)).put("start", start).put("end", end)
        if (cycle) o.put("type", "cycle").put("on", cycleOn).put("off", cycleOff).put("anchor", anchor)
        return o
    }
}

data class Employee(
    val id: Int,
    val name: String,
    val role: String,
    val position: String,
    val msId: String?,
    val active: Boolean,
    val schedule: Schedule,
    val phone: String,
    val email: String,
    val hasKey: Boolean,
)

data class RatingRow(
    val id: Int,
    val name: String,
    val role: String,
    val score: Int,
    val planned: Int?,
    val present: Int?,
    val onTime: Int?,
    val output: Int?,
    val attendance: Int?,
    val punctuality: Int?,
    val activity: Int? = null,
    val activeMinutes: Int? = null,
)

data class StaffSettings(val allowedIps: String, val lateMinutes: Int, val myIp: String, val idleMinutes: Int = 30)

/** Активность сотрудника офиса за период. */
data class ActivityRow(
    val id: Int,
    val name: String,
    val role: String,
    val minutes: Int,
    val sessions: Int,
    val first: Long?,
    val last: Long?,
    val maxGapMinutes: Int,
    val actions: Map<String, Int>,
    val working: Boolean,
    val idle: Boolean,
)

data class FeedRow(val who: String, val kind: String, val detail: String, val time: Long)

data class ActivityReport(val days: Int, val idleMinutes: Int, val people: List<ActivityRow>, val feed: List<FeedRow>, val screens: List<Pair<String, Int>> = emptyList())

data class MsAuditRow(val who: String, val moment: String, val event: String, val entity: String, val count: Int, val info: String)

/**
 * Сервер фабрики: вход сотрудника, производство по этапам, сотрудники (директор), рейтинг.
 * Ошибки — в [message] (показывается на экране).
 */
class StaffViewModel(application: Application) : AndroidViewModel(application) {
    private val store = ServerStore(application)

    private val _me = MutableStateFlow(store.me?.let { runCatching { StaffMe.parse(JSONObject(it)) }.getOrNull() })
    val me: StateFlow<StaffMe?> = _me.asStateFlow()
    private val _jobs = MutableStateFlow<List<Job>?>(null)
    val jobs: StateFlow<List<Job>?> = _jobs.asStateFlow()
    private val _employees = MutableStateFlow<List<Employee>?>(null)
    val employees: StateFlow<List<Employee>?> = _employees.asStateFlow()
    private val _rating = MutableStateFlow<List<RatingRow>?>(null)
    val rating: StateFlow<List<RatingRow>?> = _rating.asStateFlow()
    private val _settings = MutableStateFlow<StaffSettings?>(null)
    val settings: StateFlow<StaffSettings?> = _settings.asStateFlow()
    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy.asStateFlow()
    private val _message = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = _message.asStateFlow()

    val connected: Boolean get() = store.connected
    val url: String get() = store.url

    fun clearMessage() { _message.value = null }

    private fun client() = ServerClient(store.url, store.token)

    private fun run(onDone: () -> Unit = {}, block: suspend (ServerClient) -> Unit) {
        if (!store.connected) { _message.value = "Сначала подключитесь к серверу фабрики"; return }
        if (_busy.value) return // повторное нажатие, пока идёт запрос
        _busy.value = true
        viewModelScope.launch {
            try {
                block(client())
                onDone()
            } catch (e: ServerException) {
                _message.value = e.message
                if (e.message == "Нужен вход") { store.clear(); _me.value = null }
            } finally {
                _busy.value = false
            }
        }
    }

    // ---------- Вход ----------

    /** Вход по ключу сотрудника; [setupKey] — первый вход директора (создаёт его на сервере). */
    fun connect(url: String, key: String, setupKey: String = "", name: String = "", device: String = android.os.Build.MODEL.orEmpty()) {
        val u = url.trim().let { if (it.endsWith("/")) it else "$it/" }
        _busy.value = true
        viewModelScope.launch {
            try {
                val c = ServerClient(u, null)
                val token = if (setupKey.isNotBlank()) {
                    c.call("setup", JSONObject().put("setup_key", setupKey.trim()).put("name", name).put("device", device)).getString("token")
                } else {
                    c.call("login", JSONObject().put("key", key.trim()).put("device", device)).getString("token")
                }
                val me = ServerClient(u, token).call("me")
                store.url = u
                store.token = token
                store.me = me.toString()
                _me.value = StaffMe.parse(me)
                _message.value = "Подключено: ${_me.value?.name} — ${_me.value?.roleTitle}"
                StaffNotifier.schedule(getApplication())
            } catch (e: ServerException) {
                _message.value = e.message
            } finally {
                _busy.value = false
            }
        }
    }

    /** Выход: токен этого телефона отзывается и на сервере. */
    fun disconnect() {
        val c = if (store.connected) client() else null
        c?.let { viewModelScope.launch { runCatching { it.call("logout") } } }
        StaffNotifier.cancel(getApplication())
        store.clear()
        _me.value = null
        _jobs.value = null
        _employees.value = null
    }

    fun refreshMe() = run { c ->
        val me = c.call("me")
        store.me = me.toString()
        _me.value = StaffMe.parse(me)
    }

    // ---------- Производство ----------

    fun loadJobs() = run { c -> loadJobsNow(c) }

    fun saveJob(title: String, client: String, quantity: Int, quoteId: String = "") = run { c ->
        c.call("jobSave", JSONObject().put("job", JSONObject().put("title", title).put("client", client).put("quantity", quantity).put("quoteId", quoteId)))
        _message.value = "Заказ добавлен в производство"
        loadJobsNow(c)
    }

    /** Закрыть заказ: меняется только отметка «готово» (срок и связь с КП остаются). */
    fun closeJob(job: Job) = run { c ->
        c.call("jobSave", JSONObject().put("job", JSONObject().put("id", job.id).put("done", true)))
        _message.value = "Заказ закрыт"
        loadJobsNow(c)
    }

    fun startStage(job: Job, stage: String) = run { c ->
        c.call("stageStart", JSONObject().put("job_id", job.id).put("stage", stage))
        loadJobsNow(c)
    }

    /** Завершить этап; [onDone] — только после ответа сервера (окно не закрывается при ошибке связи). */
    fun finishStage(stageRun: StageRun, quantity: Int, comment: String, onDone: () -> Unit = {}) = run(onDone) { c ->
        val r = c.call("stageFinish", JSONObject().put("id", stageRun.id).put("quantity", quantity).put("comment", comment))
        _message.value = "Этап завершён за ${r.optLong("minutes")} мин"
        loadJobsNow(c)
    }

    private suspend fun loadJobsNow(c: ServerClient) {
        val a = c.call("jobs").optJSONArray("jobs") ?: JSONArray()
        _jobs.value = a.objects().map { j ->
            val techCard = j.optJSONObject("techCard")?.let { tc ->
                JobTechCard(tc.optString("code"), tc.optString("name"), tc.optInt("version"))
            }
            Job(
                j.optInt("id"), j.optString("title"), j.optString("client"), j.optInt("quantity"),
                if (j.isNull("deadline")) null else j.optLong("deadline"),
                techCard,
                (j.optJSONArray("stages") ?: JSONArray()).objects().map { s ->
                    StageRun(
                        s.optInt("id"), s.optString("stage"), s.optString("startedBy").ifBlank { null }, s.longOrNull("startedAt"),
                        s.optString("finishedBy").takeIf { !s.isNull("finishedBy") && it.isNotBlank() }, s.longOrNull("finishedAt"),
                        s.optInt("quantity"), s.optString("comment"),
                    )
                },
            )
        }
    }

    // ---------- Сотрудники (директор) ----------

    fun loadEmployees() = run { c -> loadEmployeesNow(c) }

    /** Сохраняет сотрудника; новому — ключ входа в [onKey] (показать один раз). */
    fun saveEmployee(e: Employee, onKey: (String) -> Unit = {}) = run { c ->
        val body = JSONObject().put("employee", JSONObject()
            .put("id", e.id).put("name", e.name).put("role", e.role).put("position", e.position)
            .put("phone", e.phone).put("email", e.email).put("active", e.active)
            .put("schedule", e.schedule.toJson()))
        val r = c.call("employeeSave", body)
        r.optString("key").takeIf { !r.isNull("key") && it.isNotBlank() }?.let(onKey)
        loadEmployeesNow(c)
    }

    fun newKey(e: Employee, onKey: (String) -> Unit) = run { c ->
        onKey(c.call("employeeKey", JSONObject().put("id", e.id)).getString("key"))
        loadEmployeesNow(c)
    }

    fun importFromMs() = run { c ->
        val r = c.call("msEmployeesImport")
        _message.value = "МойСклад: новых ${r.optInt("added")}, обновлено ${r.optInt("updated")}. Новым выдайте роль и ключ."
        loadEmployeesNow(c)
    }

    fun saveMsCard(msId: String, card: Map<String, String>) = run { c ->
        val o = JSONObject()
        card.forEach { (k, v) -> o.put(k, v) }
        c.call("msEmployeeSave", JSONObject().put("ms_id", msId).put("card", o))
        _message.value = "Карточка сохранена в МойСклад"
    }

    private suspend fun loadEmployeesNow(c: ServerClient) {
        _employees.value = null
        val a = c.call("employees").optJSONArray("employees") ?: JSONArray()
        _employees.value = a.objects().map { e ->
            val s = e.optJSONObject("schedule") ?: JSONObject()
            val days = s.optJSONArray("days") ?: JSONArray()
            Employee(
                e.optInt("id"), e.optString("name"), e.optString("role"), e.optString("position"),
                e.optString("ms_id").takeIf { !e.isNull("ms_id") && it.isNotBlank() }, e.optBoolean("active"),
                Schedule(
                    (0 until days.length()).map { days.optInt(it) }, s.optString("start", "09:00"), s.optString("end", "18:00"),
                    if (s.optString("type") == "cycle") s.optInt("on") else 0, s.optInt("off"), s.optString("anchor"),
                ),
                e.optString("phone"), e.optString("email"), e.optBoolean("hasKey"),
            )
        }
    }

    // ---------- Активность в приложении ----------

    private val _activity = MutableStateFlow<ActivityReport?>(null)
    val activity: StateFlow<ActivityReport?> = _activity.asStateFlow()
    private val _msAudit = MutableStateFlow<List<MsAuditRow>?>(null)
    val msAudit: StateFlow<List<MsAuditRow>?> = _msAudit.asStateFlow()

    /** Отметка «приложение открыто» (раз в минуту, пока на экране); ошибки связи молча пропускаются. */
    fun ping(screen: String = "") {
        if (!store.connected || _me.value?.tracked != true) return
        viewModelScope.launch { runCatching { client().call("ping", JSONObject().put("screen", screen)) } }
    }

    /** Действие в приложении (КП, клиент, товар, отгрузка…) — для отчёта директора. */
    fun event(kind: String, detail: String = "") {
        if (!store.connected || _me.value?.tracked != true) return
        viewModelScope.launch { runCatching { client().call("event", JSONObject().put("kind", kind).put("detail", detail.take(200))) } }
    }

    fun loadActivity(days: Int) = run { c ->
        val r = c.call("activity", JSONObject().put("days", days))
        _activity.value = ActivityReport(
            days,
            r.optInt("idleMinutes", 30),
            (r.optJSONArray("people") ?: JSONArray()).objects().map { p ->
                val a = p.optJSONObject("actions") ?: JSONObject()
                ActivityRow(
                    p.optInt("id"), p.optString("name"), p.optString("role"), p.optInt("minutes"), p.optInt("sessions"),
                    p.longOrNull("first"), p.longOrNull("last"), p.optInt("maxGapMinutes"),
                    a.keys().asSequence().associateWith { a.optInt(it) }, p.optBoolean("working"), p.optBoolean("idle"),
                )
            },
            (r.optJSONArray("feed") ?: JSONArray()).objects().map { f -> FeedRow(f.optString("who"), f.optString("kind"), f.optString("detail"), f.optLong("time")) },
            (r.optJSONArray("screens") ?: JSONArray()).objects().map { it.optString("screen") to it.optInt("minutes") },
        )
    }

    fun loadMsAudit(days: Int) = run { c ->
        _msAudit.value = (c.call("msAudit", JSONObject().put("days", days)).optJSONArray("events") ?: JSONArray()).objects().map {
            MsAuditRow(it.optString("who"), it.optString("moment"), it.optString("event"), it.optString("entity"), it.optInt("count"), it.optString("info"))
        }
    }

    // ---------- Рейтинг и настройки ----------

    fun loadRating(month: String) = run { c ->
        _rating.value = (c.call("rating", JSONObject().put("month", month)).optJSONArray("rating") ?: JSONArray()).objects().map { r ->
            RatingRow(
                r.optInt("id"), r.optString("name"), r.optString("role"), r.optInt("score"),
                r.intOrNull("planned"), r.intOrNull("present"), r.intOrNull("onTime"), r.intOrNull("output"),
                r.intOrNull("attendance"), r.intOrNull("punctuality"), r.intOrNull("activity"), r.intOrNull("activeMinutes"),
            )
        }
    }

    fun loadSettings() = run { c ->
        val s = c.call("settings")
        _settings.value = StaffSettings(s.optString("allowed_ips"), s.optInt("late_minutes", 10), s.optString("my_ip"), s.optInt("idle_minutes", 30))
    }

    fun saveSettings(allowedIps: String, lateMinutes: Int, idleMinutes: Int) = run { c ->
        val s = c.call("settingsSave", JSONObject().put("allowed_ips", allowedIps).put("late_minutes", lateMinutes).put("idle_minutes", idleMinutes))
        _settings.value = StaffSettings(s.optString("allowed_ips"), s.optInt("late_minutes", 10), s.optString("my_ip"), s.optInt("idle_minutes", 30))
        _message.value = "Настройки сервера сохранены"
    }
}

private fun JSONArray.objects(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }
private fun JSONObject.longOrNull(name: String): Long? = if (isNull(name) || !has(name)) null else optLong(name)
private fun JSONObject.intOrNull(name: String): Int? = if (isNull(name) || !has(name)) null else optInt(name)
