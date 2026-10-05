package com.knit.calculator.core

import java.math.BigDecimal

/** Позиция заказа при отгрузке по сканеру: заказано, уже отгружено, отсканировано сейчас. */
data class ShipLine(
    val id: String,
    val type: String,
    val name: String,
    val article: String = "",
    val barcode: String = "",
    val quantity: BigDecimal,
    val shipped: BigDecimal = BigDecimal.ZERO,
    val scanned: BigDecimal = BigDecimal.ZERO,
) {
    /** Сколько ещё отгрузить по заказу. */
    val remaining: BigDecimal get() = (quantity - shipped).max(BigDecimal.ZERO)
    val done: Boolean get() = scanned.signum() > 0 && scanned.compareTo(remaining) == 0
    val over: Boolean get() = scanned > remaining
}

/** Результат скана. */
sealed class ScanResult {
    data class Matched(val index: Int) : ScanResult()
    /** Товар есть в каталоге, но не в заказе. */
    data class NotInOrder(val name: String) : ScanResult()
    object Unknown : ScanResult()
}

object Warehouse {
    /**
     * Строка заказа по коду со сканера: штрихкод или артикул позиции; иначе — товар каталога (по штрихкоду/артикулу)
     * и его строка в заказе.
     */
    fun match(lines: List<ShipLine>, code: String, catalog: List<Product>): ScanResult {
        val c = code.trim()
        if (c.isEmpty()) return ScanResult.Unknown
        lines.indexOfFirst { it.barcode.isNotEmpty() && it.barcode == c }.takeIf { it >= 0 }?.let { return ScanResult.Matched(it) }
        lines.indexOfFirst { it.article.isNotEmpty() && it.article.equals(c, ignoreCase = true) }.takeIf { it >= 0 }?.let { return ScanResult.Matched(it) }
        val product = MoySklad.byScan(catalog, c) ?: return ScanResult.Unknown
        val i = lines.indexOfFirst { it.id == product.externalId }
        return if (i >= 0) ScanResult.Matched(i) else ScanResult.NotInOrder(product.name)
    }

    /** +[step] к отсканированному в строке [index] (не меньше нуля). */
    fun add(lines: List<ShipLine>, index: Int, step: BigDecimal = BigDecimal.ONE): List<ShipLine> =
        lines.mapIndexed { i, l -> if (i == index) l.copy(scanned = (l.scanned + step).max(BigDecimal.ZERO)) else l }

    /** Итог для подтверждения: всего отсканировано, недостача и лишнее по строкам. */
    data class Summary(val scanned: BigDecimal, val short: List<ShipLine>, val over: List<ShipLine>)

    fun summary(lines: List<ShipLine>): Summary = Summary(
        lines.fold(BigDecimal.ZERO) { a, l -> a + l.scanned },
        lines.filter { it.scanned < it.remaining },
        lines.filter { it.over },
    )
}
