package com.knit.calculator.core

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.math.BigDecimal

class LabelsTest {
    @Test fun ean13CheckDigit() {
        assertEquals(3, Ean13.checkDigit("460123456789"))
        assertTrue(Ean13.isValid("4601234567893"))
        assertTrue(Ean13.isValid("2000000000015"))
        assertFalse(Ean13.isValid("4601234567897"))
        assertFalse(Ean13.isValid("46012345678"))
        assertEquals("4 601234 567893", Ean13.human("4601234567893"))
        val m = Ean13.modules("4601234567893")
        assertEquals(95, m.size)
        // Крайние и центральные ограничители: 101 … 01010 … 101.
        assertTrue(m[0] && !m[1] && m[2] && m[92] && !m[93] && m[94])
        assertTrue(!m[45] && m[46] && !m[47] && m[48] && !m[49])
    }

    @Test fun labelSize203dpi() {
        val spec = LabelSpec()
        assertEquals(PrinterLanguage.TSPL, spec.language)
        // Рулон 75 мм (599 точек → кратно 8: 592), длина 120 мм (959 точек); макет — альбомный.
        assertEquals(592, spec.paperWidthDots)
        assertEquals(959, spec.paperHeightDots)
        assertEquals(959, spec.widthDots)
        assertEquals(592, spec.heightDots)
        val flat = spec.copy(rotated = false)
        assertEquals(952, flat.widthDots)
        assertEquals(599, flat.heightDots)
    }

    @Test fun rotate90() {
        // 3×2: чёрная точка в левом верхнем углу → после поворота по часовой — в правом верхнем.
        val b = MonoBitmap(3, 2, byteArrayOf(0x80.toByte(), 0x00))
        val r = b.rotate90()
        assertEquals(2, r.width)
        assertEquals(3, r.height)
        assertTrue(r.isBlack(1, 0))
        assertEquals(1, (0 until 2).sumOf { x -> (0 until 3).count { y -> r.isBlack(x, y) } })
    }

    @Test fun tsplAndZplCommands() {
        // 16×2: первая строка — 8 чёрных и 8 белых точек.
        val b = MonoBitmap(16, 2, byteArrayOf(0xFF.toByte(), 0x00, 0x00, 0x00))
        assertTrue(b.isBlack(0, 0) && !b.isBlack(8, 0) && !b.isBlack(0, 1))
        val tspl = LabelPrinter.commands(LabelSpec(rotated = false), b, 3)
        val text = String(tspl, Charsets.ISO_8859_1)
        assertTrue(text.startsWith("SIZE 120 mm,75 mm\r\nGAP 2 mm,0 mm\r\n"))
        assertTrue(text.contains("BITMAP 0,0,2,2,0,"))
        assertTrue(text.endsWith("\r\nPRINT 3,1\r\n"))
        val start = text.indexOf("BITMAP 0,0,2,2,0,") + "BITMAP 0,0,2,2,0,".length
        // TSPL: 0 — чёрная точка.
        assertArrayEquals(byteArrayOf(0x00, 0xFF.toByte(), 0xFF.toByte(), 0xFF.toByte()), tspl.copyOfRange(start, start + 4))
        val zpl = String(LabelPrinter.commands(LabelSpec(language = PrinterLanguage.ZPL, rotated = false), b, 2), Charsets.US_ASCII)
        assertTrue(zpl.startsWith("^XA^PW952^LL599") && zpl.contains("^GFA,4,4,2,FF000000^FS^PQ2^XZ"))
        // Рулон 75 мм: размер бумаги 75×120, картинка повёрнута (2 байта в строке → 1).
        val rotated = String(LabelPrinter.commands(LabelSpec(), b, 1), Charsets.ISO_8859_1)
        assertTrue(rotated.startsWith("SIZE 75 mm,120 mm\r\n") && rotated.contains("BITMAP 0,0,1,16,0,"))
    }

    @Test fun monoFromPixels() {
        val px = intArrayOf(0xFF000000.toInt(), 0xFFFFFFFF.toInt(), 0xFF333333.toInt(), 0x00000000)
        val b = MonoBitmap.fromPixels(4, 1, px)
        assertTrue(b.isBlack(0, 0) && !b.isBlack(1, 0) && b.isBlack(2, 0) && !b.isBlack(3, 0))
    }

    @Test fun labelContentFromVariant() {
        val p = MoySklad.product(
            MsItem(
                "v1", "Подвяз двуслойный 1х1", tiers = listOf(BigDecimal.ONE to BigDecimal(150)), type = "variant", barcode = "2000000000015",
                chars = mapOf("Цвет" to "белый", "Размер" to "115х14 см", "Состав / материала" to "хлопок 95%", "Артикул" to "A-1", "Тип резинки" to "-"),
            ),
            listOf("Цвет", "Размер"),
        )!!
        val c = Labels.content(p, "Фабрика \"KS\"", "fabrika-ks.ru", "10", "10.2026", "ООО «Солвер»", "9705239429", "г. Электросталь, ул. Ялагина, 3")
        assertEquals("Подвяз двуслойный 1х1", c.title)
        assertEquals(
            listOf("Артикул" to "A-1", "Цвет" to "белый", "Размер" to "115х14 см", "Состав" to "хлопок 95%", "Кол-во" to "10 шт", "Дата изготовления" to "10.2026"),
            c.details,
        )
        assertEquals("2000000000015", c.barcode)
        assertEquals(listOf("Изготовитель: ООО «Солвер», ИНН 9705239429", "г. Электросталь, ул. Ялагина, 3", "Сделано в России"), c.maker)
        // Неверный штрихкод из МойСклад не попадает на этикетку.
        assertEquals("", MoySklad.product(MsItem("x", "A", tiers = listOf(BigDecimal.ONE to BigDecimal.ONE), barcode = "123"))!!.barcode)
    }
}
