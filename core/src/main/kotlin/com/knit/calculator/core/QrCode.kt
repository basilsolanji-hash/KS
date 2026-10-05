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

/**
 * Платёжный QR по ГОСТ Р 56042-2014 (кодировка UTF-8, «ST00012»): клиент сканирует камерой
 * в приложении банка — реквизиты, сумма и назначение подставляются сами.
 */
object PaymentQr {
    private fun clean(v: String) = v.replace('|', ' ').replace('\n', ' ').trim()

    fun text(
        name: String,
        account: String,
        bank: String,
        bik: String,
        corrAccount: String,
        inn: String,
        kpp: String,
        purpose: String,
        sum: java.math.BigDecimal,
    ): String {
        val kopecks = sum.movePointRight(2).setScale(0, java.math.RoundingMode.HALF_UP).toPlainString()
        return listOf(
            "ST00012",
            "Name=" + clean(name),
            "PersonalAcc=" + clean(account),
            "BankName=" + clean(bank),
            "BIC=" + clean(bik),
            "CorrespAcc=" + clean(corrAccount),
            "PayeeINN=" + clean(inn),
            "KPP=" + clean(kpp),
            "Purpose=" + clean(purpose),
            "Sum=" + kopecks,
        ).joinToString("|")
    }
}
