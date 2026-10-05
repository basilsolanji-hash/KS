package com.knit.calculator.comms

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

/** Канал связи в приложении: почтовый ящик, Макс или Telegram (веб-версия). */
data class Channel(
    val id: String,
    val name: String,
    val url: String,
    val kind: String,
    /** Открывать как на компьютере (если мобильная веб-версия просит приложение). */
    val desktop: Boolean = false,
) {
    companion object {
        const val MAIL = "mail"
        const val MAX = "max"
        const val TELEGRAM = "telegram"
    }
}

/** Готовые адреса почтовых сервисов для «Добавить ящик». */
data class MailPreset(val title: String, val url: String, val note: String? = null)

val MAIL_PRESETS = listOf(
    MailPreset("Яндекс Почта", "https://mail.yandex.ru"),
    MailPreset("Mail.ru", "https://e.mail.ru"),
    MailPreset("Gmail", "https://mail.google.com", "Google может не пустить во встроенный браузер — тогда откройте в Chrome."),
)

/** Список каналов на этом телефоне (входы в ящики и мессенджеры хранятся только здесь). */
class ChannelStore(context: Context) {
    private val prefs = context.getSharedPreferences("comms", Context.MODE_PRIVATE)

    fun load(): List<Channel> {
        val raw = prefs.getString(KEY, null) ?: return DEFAULTS
        return runCatching {
            val a = JSONArray(raw)
            (0 until a.length()).map { a.getJSONObject(it) }.map {
                Channel(it.getString("id"), it.getString("name"), it.getString("url"), it.optString("kind", Channel.MAIL), it.optBoolean("desktop"))
            }
        }.getOrDefault(DEFAULTS)
    }

    fun save(list: List<Channel>) {
        val a = JSONArray()
        list.forEach { c ->
            a.put(JSONObject().put("id", c.id).put("name", c.name).put("url", c.url).put("kind", c.kind).put("desktop", c.desktop))
        }
        prefs.edit().putString(KEY, a.toString()).apply()
    }

    var selected: String?
        get() = prefs.getString("selected", null)
        set(v) { prefs.edit().putString("selected", v).apply() }

    companion object {
        private const val KEY = "channels"
        val DEFAULTS = listOf(
            Channel("max", "Макс", "https://web.max.ru", Channel.MAX),
            Channel("telegram", "Telegram", "https://web.telegram.org/a/", Channel.TELEGRAM),
        )

        fun newMail(name: String, url: String) = Channel("mail-" + UUID.randomUUID().toString().take(8), name, url, Channel.MAIL)

        /** Адрес ящика: допускаем «mail.yandex.ru» без https://. */
        fun normalizeUrl(text: String): String? {
            val t = text.trim()
            if (t.isEmpty() || t.contains(' ')) return null
            val url = if (t.startsWith("https://") || t.startsWith("http://")) t else "https://$t"
            val host = android.net.Uri.parse(url).host.orEmpty()
            return if (host.contains('.')) url else null
        }
    }
}
