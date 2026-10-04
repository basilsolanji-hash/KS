package com.knit.calculator.core

import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel

/** QR-код как матрица модулей (true — тёмный), без полей: поля рисует тот, кто выводит код. */
object QrCode {
    fun matrix(text: String): List<BooleanArray> {
        val hints = mapOf(EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M, EncodeHintType.MARGIN to 0, EncodeHintType.CHARACTER_SET to "UTF-8")
        val bits = QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0, hints)
        return (0 until bits.height).map { y -> BooleanArray(bits.width) { x -> bits.get(x, y) } }
    }
}
