package com.knit.calculator.quote

import android.content.Context
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning

/**
 * Сканирование штрихкода товара камерой через сервисы Google (разрешение на камеру не нужно).
 * Ручной сканер (Bluetooth/USB) работает как клавиатура: код попадает в поле поиска.
 */
object BarcodeScan {
    fun scan(context: Context, onCode: (String) -> Unit, onError: (String) -> Unit) {
        val options = GmsBarcodeScannerOptions.Builder()
            .setBarcodeFormats(Barcode.FORMAT_EAN_13, Barcode.FORMAT_EAN_8, Barcode.FORMAT_UPC_A, Barcode.FORMAT_CODE_128, Barcode.FORMAT_QR_CODE)
            .enableAutoZoom()
            .build()
        GmsBarcodeScanning.getClient(context, options).startScan()
            .addOnSuccessListener { code -> code.rawValue?.trim()?.takeIf { it.isNotEmpty() }?.let(onCode) }
            .addOnFailureListener { e -> onError("Сканер недоступен: ${e.message ?: "нужны сервисы Google Play"}") }
    }
}
