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

class ValidationTest {
    @Test fun innChecksum() {
        assertTrue(Validation.inn("9705239429"))
        assertTrue(Validation.inn("7726358110"))
        assertFalse(Validation.inn("7701234567"))
        assertTrue(Validation.inn("500100732259"))
        assertFalse(Validation.inn("500100732250"))
        assertFalse(Validation.inn("12345"))
        assertFalse(Validation.inn("97052394a9"))
    }

    @Test fun emailPhoneKpp() {
        assertTrue(Validation.email("Sale@fabrika-ks.ru"))
        assertTrue(Validation.email(" ivan.petrov+kp@mail.example.com "))
        assertFalse(Validation.email("sale@fabrika"))
        assertFalse(Validation.email("sale fabrika.ru"))
        assertTrue(Validation.phone("+7 985 000-79-92"))
        assertTrue(Validation.phone("8 (495) 123-45-67"))
        assertFalse(Validation.phone("123-45"))
        assertFalse(Validation.phone("+7 985 abc"))
        assertTrue(Validation.kpp("772301001"))
        assertTrue(Validation.kpp("7723AB001"))
        assertFalse(Validation.kpp("77230100"))
    }
}

class DashboardTest {
    @Test fun daySummary() {
        val day = 86_400_000L
        val now = 100 * day
        val deals = listOf(
            Deal("a", 1, "А", BigDecimal(100_000), QuoteStatus.APPROVED),
            Deal("b", 2, "Б", BigDecimal(50_000), QuoteStatus.SENT),
            Deal("c", 3, "В", BigDecimal(30_000), QuoteStatus.SENT),
            Deal("d", 4, "Г", BigDecimal(20_000), QuoteStatus.PAID),
        )
        val payments = listOf(Payment("p", "a", 1, "А", 0, BigDecimal(40_000)), Payment("q", "d", 4, "Г", 0, BigDecimal(20_000)))
        val orders = listOf(
            ProductionOrder("a", 1, "А", created = 0, due = now - day),
            ProductionOrder("d", 4, "Г", created = 0, due = now - day, stage = OrderStage.SHIPPED),
        )
        val s = Dashboard.summary(deals, mapOf("b" to now - 5 * day, "c" to now - day), payments, orders, now)
        assertEquals(0, BigDecimal(60_000).compareTo(s.debt))
        assertEquals(1, s.debtors)
        assertEquals(1, s.overdueOrders)
        assertEquals(1, s.waitingQuotes)
        assertFalse(s.isEmpty)
    }
}

class PaymentQrTest {
    @Test fun gostString() {
        val t = PaymentQr.text(
            "ООО «Солвер»", "40702810538710009446", "ПАО Сбербанк", "044525225", "30101810400000000225",
            "9705239429", "772301001", "Оплата по счёту № 7 | КП № 12", BigDecimal("62400.5"),
        )
        assertEquals(
            "ST00012|Name=ООО «Солвер»|PersonalAcc=40702810538710009446|BankName=ПАО Сбербанк|BIC=044525225|" +
                "CorrespAcc=30101810400000000225|PayeeINN=9705239429|KPP=772301001|Purpose=Оплата по счёту № 7   КП № 12|Sum=6240050",
            t,
        )
        assertTrue(QrCode.matrix(t).size >= 25)
    }
}

class FinanceTest {
    private val day = 86_400_000L
    // 2026-10-05 (понедельник) 12:00 МСК.
    private val now = 1_791_190_800_000L

    @Test fun weekStartIsMonday() {
        val mon = CashFlow.weekStart(now)
        assertEquals(mon, CashFlow.weekStart(now + 3 * day))
        assertEquals(mon + 7 * day, CashFlow.weekStart(now + 7 * day))
    }

    @Test fun regularPaymentsByDayOfMonth() {
        val items = CashFlow.regular(listOf(RegularPayment("Аренда", BigDecimal(150_000), 5), RegularPayment("Конец", BigDecimal(1), 31)), now, now + 62 * day)
        // Аренда: 5 октября (сегодня, 12:00), 5 ноября, 5 декабря.
        assertEquals(3, items.count { it.label == "Аренда" })
        assertTrue(items.all { !it.inflow })
        // 31-е: в ноябре — 30-е.
        assertEquals(2, items.count { it.label == "Конец" })
    }

    @Test fun inflowsPrepayAndRest() {
        val deals = listOf(
            Deal("a", 1, "А", BigDecimal(100_000), QuoteStatus.APPROVED),
            Deal("b", 2, "Б", BigDecimal(50_000), QuoteStatus.IN_WORK),
            Deal("c", 3, "В", BigDecimal(70_000), QuoteStatus.SENT),
        )
        val payments = listOf(Payment("p", "b", 2, "Б", 0, BigDecimal(25_000)))
        val orders = listOf(ProductionOrder("b", 2, "Б", created = 0, due = now + 10 * day))
        val items = CashFlow.expectedInflows(deals, payments, orders, BigDecimal(50), now)
        assertEquals(
            listOf("Предоплата КП № 1 · А" to 50_000, "Остаток КП № 1 · А" to 50_000, "Остаток КП № 2 · Б" to 25_000),
            items.map { it.label to it.amount.toInt() },
        )
        assertEquals(now + 21 * day, items[1].date)
        assertEquals(now + 10 * day, items[2].date)
    }

    @Test fun weeksAndGap() {
        val items = listOf(
            CashItem(now - day, BigDecimal(10_000), "просрочено", inflow = false),
            CashItem(now + 8 * day, BigDecimal(100_000), "аренда", inflow = false),
            CashItem(now + 15 * day, BigDecimal(200_000), "оплата", inflow = true),
        )
        val w = CashFlow.weeks(BigDecimal(50_000), items, now, 4)
        assertEquals(listOf(40_000, -60_000, 140_000, 140_000), w.map { it.balance.toInt() })
        assertEquals(listOf(false, true, false, false), w.map { it.gap })
    }

    @Test fun abcAndManagers() {
        val sales = listOf(
            Sale("1", 1, "Крупный", BigDecimal(800), QuoteStatus.PAID, now, "Иван / Pixel", BigDecimal(200), mapOf("Подвяз" to BigDecimal(800))),
            Sale("2", 2, "Средний", BigDecimal(150), QuoteStatus.APPROVED, now, "Иван", null, mapOf("Поло" to BigDecimal(150))),
            Sale("3", 3, "Малый", BigDecimal(50), QuoteStatus.IN_WORK, now, "Мария", null, mapOf("Манжеты" to BigDecimal(50))),
            Sale("4", 4, "Отказник", BigDecimal(999), QuoteStatus.REJECTED, now, "Мария"),
        )
        assertEquals(listOf('A', 'B', 'C'), DirectorReport.clients(sales).map { it.group })
        assertEquals(listOf("Подвяз", "Поло", "Манжеты"), DirectorReport.products(sales).map { it.name })
        val m = DirectorReport.managers(sales)
        assertEquals(listOf("Иван", "Мария"), m.map { it.name })
        assertEquals(100, m[0].conversion)
        assertEquals(50, m[1].conversion)
        assertEquals(0, BigDecimal(200).compareTo(m[0].profit))
        assertEquals(0, BigDecimal("333.33").compareTo(DirectorReport.averageCheck(sales)))
        val months = DirectorReport.months(sales, listOf(Payment("p", "1", 1, "", now, BigDecimal(1_750_000))), BigDecimal(3_500_000), now, 2)
        assertEquals(4, months[0].quotes)
        assertEquals(50, months[0].planPercent)
        assertEquals(0, months[1].quotes)
    }
}

class MonthlySumsTest {
    @Test fun lastMonthsOldestFirst() {
        val now = 1_791_190_800_000L // 05.10.2026
        val day = 86_400_000L
        val r = monthlySums(listOf(now to BigDecimal(100), now - 40 * day to BigDecimal(50), now - 400 * day to BigDecimal(7)), now, 3)
        assertEquals(listOf("2026-08", "2026-09", "2026-10"), r.map { it.first })
        assertEquals(listOf(50, 0, 100), r.map { it.second.toInt() }) // 40 дней назад — 26 августа
    }
}

class ScanAndBadgesTest {
    private fun p(id: String, code: String, barcode: String, badges: List<String> = emptyList()) =
        MoySklad.product(MsItem(id, "Подвяз $id", article = code, tiers = listOf(BigDecimal.ONE to BigDecimal.TEN), badges = badges, barcode = barcode))!!

    private val items = listOf(
        p("a", "PD-1", "4600000000015", listOf("Распродажа")),
        p("b", "PD-2", "4600000000022", listOf("Топ-продажа")),
        p("c", "PD-3", ""),
    )

    @Test fun scanFindsByBarcodeOrArticle() {
        assertEquals("a", MoySklad.byScan(items, " 4600000000015 ")?.externalId)
        assertEquals("c", MoySklad.byScan(items, "pd-3")?.externalId)
        assertEquals(null, MoySklad.byScan(items, "123"))
    }

    @Test fun searchMatchesBarcodeAndBadge() {
        assertEquals(listOf("b"), MoySklad.search(items, "4600000000022").map { it.externalId })
        assertEquals(listOf("a"), MoySklad.search(items, "", badge = "Распродажа").map { it.externalId })
    }

    @Test fun saleBadgeFirst() {
        assertEquals(listOf("Распродажа", "Топ-продажа"), MoySklad.badges(items))
        assertEquals(true, MoySklad.isSale("РАСПРОДАЖА -30%"))
        assertEquals(false, MoySklad.isSale("Популярный"))
    }
}

class PinLockTest {
    @Test fun rejectsWeakPins() {
        assertTrue(PinLock.problem("123") != null)
        assertTrue(PinLock.problem("12a4") != null)
        assertTrue(PinLock.problem("7777") != null)
        assertTrue(PinLock.problem("3456") != null)
        assertTrue(PinLock.problem("6543") != null)
        assertEquals(null, PinLock.problem("2580"))
    }

    @Test fun hashDependsOnSalt() {
        assertEquals(PinLock.hash("2580", "s1"), PinLock.hash("2580", "s1"))
        assertTrue(PinLock.hash("2580", "s1") != PinLock.hash("2580", "s2"))
        assertEquals(64, PinLock.hash("2580", "s").length)
    }

    @Test fun fiveFailsPause() {
        var a = PinLock.Attempts()
        repeat(4) { a = a.failed(1000) }
        assertEquals(1, a.left())
        assertFalse(a.blocked(1000))
        a = a.failed(1000)
        assertTrue(a.blocked(1000 + PinLock.PAUSE_MS - 1))
        assertFalse(a.blocked(1000 + PinLock.PAUSE_MS))
        assertEquals(PinLock.MAX_FAILS, a.left())
    }
}

class ExpenseReportTest {
    private val tz = java.util.TimeZone.getTimeZone("Europe/Moscow")
    private fun t(y: Int, m: Int, d: Int) = java.util.Calendar.getInstance(tz).apply { clear(); set(y, m - 1, d, 12, 0) }.timeInMillis
    private val now = t(2026, 10, 15)
    private val items = listOf(
        Expense(t(2026, 10, 3), BigDecimal("150000"), "Аренда"),
        Expense(t(2026, 10, 10), BigDecimal("50000"), "Пряжа"),
        Expense(t(2026, 9, 3), BigDecimal("100000"), "Аренда"),
        Expense(t(2026, 9, 20), BigDecimal("999"), "Аренда"), // после 15 сентября — не в сравнении для «этого месяца»
        Expense(t(2026, 8, 5), BigDecimal("70000"), ""),
    )

    @Test fun thisMonthComparedToSameDayLastMonth() {
        val rows = ExpenseReport.byCategory(items, ExpensePeriod.THIS_MONTH, now)
        assertEquals(listOf("Аренда", "Пряжа"), rows.map { it.category })
        assertEquals(BigDecimal("75.0"), rows[0].share)
        assertEquals(BigDecimal("100000"), rows[0].previous)
        assertEquals(50, rows[0].changePercent)
        assertEquals(null, rows[1].changePercent)
    }

    @Test fun lastMonthAndQuarter() {
        val last = ExpenseReport.byCategory(items, ExpensePeriod.LAST_MONTH, now)
        assertEquals(BigDecimal("100999"), last.single().sum)
        assertEquals(0, last.single().previous.signum()) // в августе — только «Без статьи»
        val q = ExpenseReport.byCategory(items, ExpensePeriod.QUARTER, now)
        assertEquals(listOf("Аренда", "Без статьи", "Пряжа"), q.map { it.category })
        assertEquals(BigDecimal("250999"), q[0].sum)
    }
}

class CostEconomicsTest {
    @Test fun breakEvenAndTargetPrice() {
        val settings = CostSettings(targetMarginPercent = BigDecimal(30), vat = VatSettings(BigDecimal(22), included = true))
        val unit = CostCalculator.unitCost(ProductCost(yarnPerUnit = BigDecimal(50), knitMinutes = BigDecimal(2), minutePrice = BigDecimal(10)), settings, BigDecimal(50))
        assertEquals(BigDecimal(70), unit.total.stripTrailingZeros().let { BigDecimal(it.toInt()) })
        val e = CostCalculator.economics(unit, settings, BigDecimal("122"), BigDecimal(100))
        // 122 ₽ с НДС 22 % → 100 ₽ фабрике; прибыль 30 ₽/шт, 3 000 ₽ на тираж.
        assertEquals(BigDecimal("30.00"), e.unitProfit)
        assertEquals(BigDecimal("3000.00"), e.totalProfit)
        assertEquals(BigDecimal("86"), e.breakEvenPrice) // 70 × 1,22 = 85,4 → 86
        assertEquals(BigDecimal("112"), e.targetPrice)
    }
}

class WarehouseTest {
    private val catalog = listOf(
        MoySklad.product(MsItem("v9", "Подвяз красный", article = "R-1", tiers = listOf(BigDecimal.ONE to BigDecimal.TEN), barcode = "4600000000022"))!!,
        MoySklad.product(MsItem("x1", "Чужой товар", article = "X-1", tiers = listOf(BigDecimal.ONE to BigDecimal.TEN), barcode = "4600000000015"))!!,
    )
    private val lines = listOf(
        ShipLine("p1", "product", "Подвяз белый", "W-1", "2000000000015", BigDecimal(10), BigDecimal(4)),
        ShipLine("v9", "variant", "Подвяз красный", "", "", BigDecimal(5)),
    )

    @Test fun matchesByBarcodeArticleOrCatalog() {
        assertEquals(ScanResult.Matched(0), Warehouse.match(lines, "2000000000015", catalog))
        assertEquals(ScanResult.Matched(0), Warehouse.match(lines, "w-1", catalog))
        assertEquals(ScanResult.Matched(1), Warehouse.match(lines, "4600000000022", catalog))
        assertEquals(ScanResult.NotInOrder("Чужой товар"), Warehouse.match(lines, "4600000000015", catalog))
        assertEquals(ScanResult.Unknown, Warehouse.match(lines, "123", catalog))
    }

    @Test fun countsRemainingAndOver() {
        var l = lines
        repeat(6) { l = Warehouse.add(l, 0) }
        assertTrue(l[0].done)
        assertEquals(BigDecimal(6), l[0].remaining)
        l = Warehouse.add(l, 0)
        assertTrue(l[0].over)
        l = Warehouse.add(l, 1, BigDecimal(-3))
        assertEquals(BigDecimal.ZERO, l[1].scanned)
        val s = Warehouse.summary(l)
        assertEquals(BigDecimal(7), s.scanned)
        assertEquals(listOf("v9"), s.short.map { it.id })
        assertEquals(listOf("p1"), s.over.map { it.id })
    }
}

class WorkTimeTest {
    private val tz = java.util.TimeZone.getTimeZone("Europe/Moscow")
    private fun t(d: Int, h: Int, m: Int = 0) = java.util.Calendar.getInstance(tz).apply { clear(); set(2026, 9, d, h, m) }.timeInMillis

    @Test fun autoStartOnlyOncePerDay() {
        val now = t(6, 9)
        assertTrue(WorkTime.shouldAutoStart(emptyList(), now))
        // Вчера смену не закрыли — сегодня всё равно начинаем новую.
        assertTrue(WorkTime.shouldAutoStart(listOf(WorkShift("a", "Иван", t(5, 9))), now))
        val closedToday = listOf(WorkShift("b", "Иван", t(6, 8), t(6, 8, 30)))
        assertFalse(WorkTime.shouldAutoStart(closedToday, t(6, 19)))
    }

    @Test fun workedTodayAndTotals() {
        val now = t(6, 13, 15)
        val shifts = listOf(
            WorkShift("y", "Иван", t(5, 9), null),            // вчера не закрыта — 0 и «незакрытая»
            WorkShift("a", "Иван", t(6, 9), t(6, 12)),        // 3 ч
            WorkShift("b", "Иван", t(6, 13), null),           // открыта: 15 мин
            WorkShift("c", "Пётр", t(2, 9), t(2, 18, 30)),    // 9 ч 30
        )
        assertEquals(195L, WorkTime.workedToday(shifts, now))
        assertEquals("b", WorkTime.current(shifts, now)?.id)
        val totals = WorkTime.totals(shifts, "2026-10", now)
        assertEquals(listOf("Иван", "Пётр"), totals.map { it.person })
        assertEquals(2, totals[0].days)
        assertEquals(195L, totals[0].minutes)
        assertEquals(1, totals[0].unclosed)
        assertEquals("9 ч 30 мин", WorkTime.format(totals[1].minutes))
    }
}

class DynamicsTest {
    private val tz = java.util.TimeZone.getTimeZone("Europe/Moscow")
    private fun t(y: Int, m: Int, d: Int, h: Int = 12) = java.util.Calendar.getInstance(tz).apply { clear(); set(y, m - 1, d, h, 0) }.timeInMillis
    private val now = t(2026, 10, 6, 14)
    private val items = listOf(
        t(2026, 10, 6, 9) to BigDecimal(100),   // сегодня 9:00
        t(2026, 10, 5, 9) to BigDecimal(40),    // вчера 9:00
        t(2026, 10, 1) to BigDecimal(200),
        t(2026, 9, 20) to BigDecimal(300),      // в 30 днях
        t(2026, 8, 20) to BigDecimal(500),      // прошлые 30 дней
        t(2025, 10, 3) to BigDecimal(700),      // год назад
    )

    @Test fun todayByHourWithYesterday() {
        val s = Dynamics.series(items, DynPeriod.TODAY, now)
        assertEquals(15, s.labels.size)
        assertEquals(BigDecimal(100), s.current[9])
        assertEquals(BigDecimal(40), s.previous!![9])
    }

    @Test fun thirtyDaysAndPrevious() {
        val s = Dynamics.series(items, DynPeriod.MONTH, now)
        assertEquals(30, s.current.size)
        assertEquals("6", s.labels.last())
        assertEquals(BigDecimal(640), s.total)
        assertEquals(BigDecimal(500), s.previousTotal)
    }

    @Test fun monthsComparedToYearAgoAndYears() {
        val m = Dynamics.series(items, DynPeriod.MONTHS, now)
        assertEquals("окт", m.labels.last())
        assertEquals(BigDecimal(340), m.current.last())
        assertEquals(BigDecimal(700), m.previous!!.last())
        val y = Dynamics.series(items, DynPeriod.YEARS, now)
        assertEquals(listOf("2022", "2023", "2024", "2025", "2026"), y.labels)
        assertEquals(null, y.previous)
        assertEquals(BigDecimal(700), y.current[3])
        assertEquals("вт", Dynamics.series(items, DynPeriod.WEEK, now).labels[6]) // 6 октября 2026 — вторник
    }
}
