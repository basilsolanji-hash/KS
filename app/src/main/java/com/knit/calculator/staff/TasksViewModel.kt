package com.knit.calculator.staff

import android.app.Application
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import androidx.core.content.FileProvider
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File

data class CheckItem(val text: String, val done: Boolean)

data class TaskItem(
    val id: Int, val title: String, val body: String, val authorId: Int, val author: String, val assigneeId: Int, val assignee: String,
    val status: String, val priority: Boolean, val due: Long?, val remind: Long?, val linkType: String, val linkId: String, val linkTitle: String,
    val repeat: String, val checklist: List<CheckItem>, val created: Long, val late: Boolean,
)

data class TaskComment(val who: String, val text: String, val time: Long)
data class TaskFile(val id: Int, val name: String, val mime: String, val size: Int, val who: String, val time: Long)
data class TaskCard(val task: TaskItem, val comments: List<TaskComment>, val files: List<TaskFile>)
data class Person(val id: Int, val name: String, val role: String)

val TASK_STATUS = mapOf("new" to "Новая", "work" to "В работе", "done" to "Сделано", "accepted" to "Принято", "cancelled" to "Отменена")
val TASK_REPEAT = mapOf("" to "Не повторять", "day" to "Каждый день", "week" to "Каждую неделю", "month" to "Каждый месяц")

/** Задачи на сервере фабрики: списки, карточка, статусы, чек-лист, комментарии, файлы. */
class TasksViewModel(application: Application) : AndroidViewModel(application) {
    private val store = ServerStore(application)
    private fun client() = ServerClient(store.url, store.token)
    val connected: Boolean get() = store.connected

    private val _list = MutableStateFlow<List<TaskItem>?>(null)
    val list: StateFlow<List<TaskItem>?> = _list.asStateFlow()
    private val _card = MutableStateFlow<TaskCard?>(null)
    val card: StateFlow<TaskCard?> = _card.asStateFlow()
    private val _people = MutableStateFlow<List<Person>>(emptyList())
    val people: StateFlow<List<Person>> = _people.asStateFlow()
    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy.asStateFlow()
    private val _message = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = _message.asStateFlow()

    fun clearMessage() { _message.value = null }

    private fun run(onDone: () -> Unit = {}, block: suspend (ServerClient) -> Unit) {
        if (!store.connected) { _message.value = "Сначала подключитесь к серверу фабрики"; return }
        _busy.value = true
        viewModelScope.launch {
            try {
                block(client())
                onDone()
            } catch (e: ServerException) {
                _message.value = e.message
            } finally {
                _busy.value = false
            }
        }
    }

    fun load(scope: String, closed: Boolean) = run { c ->
        _list.value = (c.call("tasks", JSONObject().put("scope", scope).put("closed", closed)).optJSONArray("tasks") ?: JSONArray()).objs().map(::task)
    }

    fun loadPeople() = run { c ->
        _people.value = (c.call("employees").optJSONArray("employees") ?: JSONArray()).objs()
            .filter { it.optBoolean("active", true) }.map { Person(it.optInt("id"), it.optString("name"), it.optString("role")) }
    }

    fun open(id: Int) = run { c -> _card.value = card(c.call("task", JSONObject().put("id", id))) }
    fun close() { _card.value = null }

    fun save(t: TaskItem, onDone: () -> Unit) = run(onDone) { c ->
        val o = JSONObject().put("title", t.title).put("body", t.body).put("assigneeId", t.assigneeId).put("priority", t.priority)
            .put("repeat", t.repeat).put("linkTitle", t.linkTitle)
            .put("checklist", JSONArray().apply { t.checklist.forEach { put(JSONObject().put("text", it.text).put("done", it.done)) } })
        if (t.id > 0) o.put("id", t.id)
        t.due?.let { o.put("due", it) }
        t.remind?.let { o.put("remind", it) }
        _card.value = card(c.call("taskSave", JSONObject().put("task", o)))
        _message.value = if (t.id > 0) "Задача сохранена" else "Задача поставлена: ${t.assignee}"
    }

    fun status(id: Int, status: String) = run { c ->
        _card.value = card(c.call("taskStatus", JSONObject().put("id", id).put("status", status)))
        _message.value = "Статус: ${TASK_STATUS[status]}"
    }

    fun check(id: Int, index: Int, done: Boolean) = run { c ->
        _card.value = card(c.call("taskCheck", JSONObject().put("id", id).put("index", index).put("done", done)))
    }

    fun comment(id: Int, text: String, onDone: () -> Unit) = run(onDone) { c ->
        _card.value = card(c.call("taskComment", JSONObject().put("id", id).put("text", text)))
    }

    /** Файл с телефона: фото уменьшается до 1600 px и сжимается (сервер принимает до 700 КБ). */
    fun attach(id: Int, uri: Uri) = run { c ->
        val app = getApplication<Application>()
        val cr = app.contentResolver
        val mime = cr.getType(uri) ?: "application/octet-stream"
        val name = cr.query(uri, arrayOf(android.provider.OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cur ->
            if (cur.moveToFirst()) cur.getString(0) else null
        } ?: "file"
        val bytes = withContext(Dispatchers.IO) {
            val raw = cr.openInputStream(uri)?.use { it.readBytes() } ?: ByteArray(0)
            if (mime.startsWith("image/") && raw.size > 300_000) shrink(raw) else raw
        }
        if (bytes.size > 700_000) throw ServerException("Файл больше 700 КБ")
        val r = c.call("fileUpload", JSONObject().put("taskId", id).put("name", if (mime.startsWith("image/")) name.substringBeforeLast('.') + ".jpg" else name)
            .put("mime", if (mime.startsWith("image/")) "image/jpeg" else mime).put("data", android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP)))
        _card.value = card(r)
        _message.value = "Файл добавлен"
    }

    private fun shrink(raw: ByteArray): ByteArray {
        val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(raw, 0, raw.size, opts)
        var sample = 1
        while (maxOf(opts.outWidth, opts.outHeight) / sample > 1600) sample *= 2
        val bmp = BitmapFactory.decodeByteArray(raw, 0, raw.size, BitmapFactory.Options().apply { inSampleSize = sample }) ?: return raw
        var quality = 85
        var out: ByteArray
        do {
            out = ByteArrayOutputStream().also { bmp.compress(Bitmap.CompressFormat.JPEG, quality, it) }.toByteArray()
            quality -= 15
        } while (out.size > 650_000 && quality > 30)
        return out
    }

    /** Открыть файл задачи в подходящем приложении телефона. */
    fun openFile(f: TaskFile) = run { c ->
        val r = c.call("fileGet", JSONObject().put("id", f.id))
        val app = getApplication<Application>()
        val dir = File(app.cacheDir, "reports").apply { mkdirs() }
        val file = File(dir, r.optString("name", "file").replace(Regex("[\\\\/:*?\"<>|]"), "_"))
        withContext(Dispatchers.IO) { file.writeBytes(android.util.Base64.decode(r.optString("data"), android.util.Base64.DEFAULT)) }
        val uri = FileProvider.getUriForFile(app, "${app.packageName}.reports", file)
        val intent = Intent(Intent.ACTION_VIEW).setDataAndType(uri, r.optString("mime", "*/*"))
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        runCatching { app.startActivity(Intent.createChooser(intent, file.name).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
            .onFailure { _message.value = "Нет приложения, чтобы открыть файл" }
    }

    private fun task(o: JSONObject) = TaskItem(
        o.optInt("id"), o.optString("title"), o.optString("body"), o.optInt("authorId"), o.optString("author"),
        o.optInt("assigneeId"), o.optString("assignee"), o.optString("status"), o.optInt("priority") == 1,
        o.longOrNull("due"), o.longOrNull("remind"), o.optString("linkType"), o.optString("linkId"), o.optString("linkTitle"),
        o.optString("repeat"), (o.optJSONArray("checklist") ?: JSONArray()).objs().map { CheckItem(it.optString("text"), it.optBoolean("done")) },
        o.optLong("created"), o.optBoolean("late"),
    )

    private fun card(r: JSONObject) = TaskCard(
        task(r.optJSONObject("task") ?: JSONObject()),
        (r.optJSONArray("comments") ?: JSONArray()).objs().map { TaskComment(it.optString("who"), it.optString("text"), it.optLong("time")) },
        (r.optJSONArray("files") ?: JSONArray()).objs().map {
            TaskFile(it.optInt("id"), it.optString("name"), it.optString("mime"), it.optInt("size"), it.optString("who"), it.optLong("time"))
        },
    )
}

private fun JSONArray.objs(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }
private fun JSONObject.longOrNull(name: String): Long? = if (isNull(name) || !has(name)) null else optLong(name)
