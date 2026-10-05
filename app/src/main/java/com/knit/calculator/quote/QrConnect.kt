package com.knit.calculator.quote

import android.content.Context
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import org.json.JSONObject

/** Данные подключения из QR-кода таблицы: адрес скрипта, ключ и имя менеджера. */
data class QrConfig(val url: String, val key: String, val name: String)

/**
 * Подключение телефона по QR-коду: таблица показывает код (меню «Фабрика KS → QR для подключения телефона»),
 * телефон сканирует его камерой через сервисы Google (разрешение на камеру приложению не нужно).
 */
object QrConnect {
    /** QR: {"ks":1,"u":"https://script.google.com/…/exec","k":"ключ","n":"Имя"}. */
    fun parse(text: String): QrConfig? = runCatching {
        val o = JSONObject(text.trim())
        if (o.optInt("ks") != 1) return null
        val url = o.optString("u").trim()
        val key = o.optString("k").trim()
        if (!url.startsWith("https://script.google.com/") || key.isBlank()) return null
        QrConfig(url, key, o.optString("n").trim())
    }.getOrNull()

    fun scan(context: Context, onResult: (QrConfig) -> Unit, onError: (String) -> Unit) {
        val options = GmsBarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build()
        GmsBarcodeScanning.getClient(context, options).startScan()
            .addOnSuccessListener { code ->
                val config = parse(code.rawValue.orEmpty())
                if (config == null) onError("Это не QR-код подключения «Фабрики KS»") else onResult(config)
            }
            .addOnFailureListener { e -> onError("Сканер недоступен: ${e.message ?: "нужны сервисы Google Play"}") }
    }
}
