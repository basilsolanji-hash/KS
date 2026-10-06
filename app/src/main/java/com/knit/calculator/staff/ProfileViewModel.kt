package com.knit.calculator.staff

import android.app.Application
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
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

data class ProfileDoc(val id: Int, val kind: String, val fileId: Int, val name: String, val size: Int)
data class ProfileCard(
    val id: Int, val name: String, val role: String, val position: String, val fields: Map<String, String>,
    val photo: Int?, val payVisible: Boolean, val confirmed: Boolean, val docs: List<ProfileDoc>,
)
data class LegalDoc(val key: String, val title: String, val text: String)
data class PayItem(val id: Int, val kind: String, val amount: Double, val comment: String)
data class Payroll(
    val month: String, val items: List<PayItem>, val accrued: Double, val paid: Double, val balance: Double,
    val days: Int, val hours: Double, val stages: Int, val tasks: Int,
)

/** Поля профиля: ключ → подпись. */
val PROFILE_FIELDS = linkedMapOf(
    "birthday" to "Дата рождения (ДД.ММ.ГГГГ)", "phone" to "Телефон", "email" to "E-mail", "address" to "Адрес проживания",
    "snils" to "СНИЛС", "inn" to "ИНН (12 цифр)", "passportSeries" to "Паспорт: серия", "passportNumber" to "Паспорт: номер",
    "passportIssuedBy" to "Паспорт: кем выдан", "passportIssuedAt" to "Паспорт: дата выдачи", "passportCode" to "Паспорт: код подразделения",
    "card" to "Карта для выплат", "bank" to "Банк", "emergencyName" to "Экстренный контакт: кто", "emergencyPhone" to "Экстренный контакт: телефон",
)
val DOC_KINDS = linkedMapOf(
    "passport" to "Паспорт (разворот с фото)", "passport2" to "Паспорт (прописка)", "snils" to "СНИЛС", "inn" to "ИНН",
    "contract" to "Трудовой договор", "other" to "Другое",
)
val PAY_KINDS = linkedMapOf(
    "salary" to "Оклад", "percent" to "Процент", "bonus" to "Премия", "fine" to "Штраф", "advance" to "Аванс (выплачено)", "payout" to "Выплата",
)

/** Профиль, документы, соглашения и выплаты на сервере фабрики. */
class ProfileViewModel(application: Application) : AndroidViewModel(application) {
    private val store = ServerStore(application)
    private fun client() = ServerClient(store.url, store.token)

    private val _profile = MutableStateFlow<ProfileCard?>(null)
    val profile: StateFlow<ProfileCard?> = _profile.asStateFlow()
    private val _photo = MutableStateFlow<Bitmap?>(null)
    val photo: StateFlow<Bitmap?> = _photo.asStateFlow()
    private val _legal = MutableStateFlow<List<LegalDoc>?>(null)
    val legal: StateFlow<List<LegalDoc>?> = _legal.asStateFlow()
    private val _payroll = MutableStateFlow<Payroll?>(null)
    val payroll: StateFlow<Payroll?> = _payroll.asStateFlow()
    private val _busy = MutableStateFlow(false)
    val busy: StateFlow<Boolean> = _busy.asStateFlow()
    private val _message = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = _message.asStateFlow()

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

    fun load(id: Int?) = run { c ->
        _photo.value = null
        apply(c.call("profile", JSONObject().apply { id?.let { put("id", it) } }), c)
    }

    private suspend fun apply(r: JSONObject, c: ServerClient) {
        val p = r.optJSONObject("profile") ?: return
        val f = p.optJSONObject("fields") ?: JSONObject()
        _profile.value = ProfileCard(
            p.optInt("id"), p.optString("name"), p.optString("role"), p.optString("position"),
            f.keys().asSequence().associateWith { f.optString(it) }, if (p.isNull("photo")) null else p.optInt("photo"),
            p.optBoolean("payVisible"), p.optBoolean("confirmed"),
            (r.optJSONArray("docs") ?: JSONArray()).let { a ->
                (0 until a.length()).mapNotNull { a.optJSONObject(it) }.map { ProfileDoc(it.optInt("id"), it.optString("kind"), it.optInt("fileId"), it.optString("name"), it.optInt("size")) }
            },
        )
        _profile.value?.photo?.let { fid ->
            runCatching {
                val bytes = android.util.Base64.decode(c.call("fileGet", JSONObject().put("id", fid)).optString("data"), android.util.Base64.DEFAULT)
                _photo.value = withContext(Dispatchers.Default) { BitmapFactory.decodeByteArray(bytes, 0, bytes.size) }
            }
        }
    }

    fun save(id: Int, fields: Map<String, String>) = run { c ->
        val o = JSONObject()
        fields.forEach { (k, v) -> o.put(k, v) }
        apply(c.call("profileSave", JSONObject().put("id", id).put("fields", o)), c)
        _message.value = "Профиль сохранён"
    }

    fun confirm(id: Int) = run { c -> apply(c.call("profileConfirm", JSONObject().put("id", id)), c); _message.value = "Профиль подтверждён" }

    fun payVisible(id: Int, visible: Boolean) = run { c -> apply(c.call("payVisible", JSONObject().put("id", id).put("visible", visible)), c) }

    /** Фото профиля (и аватар на главной этого телефона, если профиль свой). */
    fun photo(id: Int, uri: Uri, own: Boolean) = run { c ->
        val bytes = readImage(uri, 800)
        apply(c.call("profilePhoto", JSONObject().put("id", id).put("data", android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP))), c)
        if (own) withContext(Dispatchers.IO) { java.io.File(getApplication<Application>().filesDir, "avatar.jpg").writeBytes(bytes) }
    }

    fun doc(id: Int, kind: String, uri: Uri) = run { c ->
        val bytes = readImage(uri, 2000)
        apply(c.call("profileDoc", JSONObject().put("id", id).put("kind", kind).put("name", "${DOC_KINDS[kind] ?: kind}.jpg").put("mime", "image/jpeg")
            .put("data", android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP))), c)
        _message.value = "Документ загружен"
    }

    fun deleteDoc(docId: Int) = run { c -> apply(c.call("profileDocDelete", JSONObject().put("docId", docId)), c) }

    private suspend fun readImage(uri: Uri, maxSide: Int): ByteArray = withContext(Dispatchers.IO) {
        val cr = getApplication<Application>().contentResolver
        val raw = cr.openInputStream(uri)?.use { it.readBytes() } ?: throw ServerException("Файл не открылся")
        val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(raw, 0, raw.size, opts)
        var sample = 1
        while (maxOf(opts.outWidth, opts.outHeight) / sample > maxSide) sample *= 2
        val bmp = BitmapFactory.decodeByteArray(raw, 0, raw.size, BitmapFactory.Options().apply { inSampleSize = sample })
            ?: throw ServerException("Это не изображение")
        var q = 85
        var out: ByteArray
        do {
            out = ByteArrayOutputStream().also { bmp.compress(Bitmap.CompressFormat.JPEG, q, it) }.toByteArray()
            q -= 15
        } while (out.size > 650_000 && q > 30)
        out
    }

    /** Открыть скан в просмотрщике телефона. */
    fun openDoc(d: ProfileDoc) = run { c ->
        val r = c.call("fileGet", JSONObject().put("id", d.fileId))
        val app = getApplication<Application>()
        val dir = java.io.File(app.cacheDir, "reports").apply { mkdirs() }
        val file = java.io.File(dir, "doc-${d.id}.jpg")
        withContext(Dispatchers.IO) { file.writeBytes(android.util.Base64.decode(r.optString("data"), android.util.Base64.DEFAULT)) }
        val uri = androidx.core.content.FileProvider.getUriForFile(app, "${app.packageName}.reports", file)
        runCatching {
            app.startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW).setDataAndType(uri, "image/jpeg")
                .addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION or android.content.Intent.FLAG_ACTIVITY_NEW_TASK))
        }
    }

    fun loadLegal() = run { c ->
        val r = c.call("legal")
        _legal.value = (r.optJSONArray("docs") ?: JSONArray()).let { a ->
            (0 until a.length()).mapNotNull { a.optJSONObject(it) }.map { LegalDoc(it.optString("key"), it.optString("title"), it.optString("text")) }
        }
    }

    fun acceptLegal(onDone: () -> Unit) = run(onDone) { c -> c.call("legalAccept") }

    fun loadPayroll(id: Int?, month: String) = run { c -> _payroll.value = pay(c.call("payroll", JSONObject().put("month", month).apply { id?.let { put("id", it) } })) }

    fun addPay(id: Int, month: String, kind: String, amount: Double, comment: String) = run { c ->
        _payroll.value = pay(c.call("payrollSave", JSONObject().put("id", id).put("month", month).put("kind", kind).put("amount", amount).put("comment", comment)))
    }

    fun deletePay(rowId: Int) = run { c -> _payroll.value = pay(c.call("payrollDelete", JSONObject().put("rowId", rowId))) }

    private fun pay(r: JSONObject): Payroll {
        val w = r.optJSONObject("work") ?: JSONObject()
        val a = r.optJSONArray("items") ?: JSONArray()
        return Payroll(
            r.optString("month"),
            (0 until a.length()).mapNotNull { a.optJSONObject(it) }.map { PayItem(it.optInt("id"), it.optString("kind"), it.optDouble("amount"), it.optString("comment")) },
            r.optDouble("accrued"), r.optDouble("paid"), r.optDouble("balance"),
            w.optInt("days"), w.optDouble("hours"), w.optInt("stages"), w.optInt("tasks"),
        )
    }
}
