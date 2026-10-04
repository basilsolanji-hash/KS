package com.knit.calculator.quote

import com.knit.calculator.core.QuoteCalculator
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test
import java.math.BigDecimal

class EditableProductTest {

    /** Изделие → форма → изделие даёт те же цены на всех вариантах и объёмах. */
    @Test fun roundTripKeepsPrices() {
        DefaultCatalog.products.forEach { p ->
            val copy = EditableProduct.from(p).toProduct()!!
            listOf(1, 9, 10, 50, 100, 499, 500, 1000).forEach { qty ->
                p.options.firstOrNull()?.choices?.forEach { choice ->
                    val selected = mapOf(p.options.first().id to choice.id)
                    assertEquals(
                        "${p.name} / ${choice.name} / $qty",
                        QuoteCalculator.unitPrice(p, selected, BigDecimal(qty)).first,
                        QuoteCalculator.unitPrice(copy, selected, BigDecimal(qty)).first,
                    )
                }
            }
        }
    }

    @Test fun parsesCoefficientsAndSkipsBlankRows() {
        val product = EditableProduct(
            code = "SH",
            name = "Шнур",
            unit = "м",
            basePrice = "12,5",
            minOrder = "",
            rounding = "0,01",
            groups = listOf(
                EditableGroup(name = "Диаметр", choices = listOf(EditableChoice(name = "5 мм", factor = "1"), EditableChoice(name = "", factor = "2"))),
                EditableGroup(name = "", choices = listOf(EditableChoice(name = "x"))),
            ),
            tiers = listOf(EditableTier(fromQuantity = "1000", factor = "0,9"), EditableTier()),
        ).toProduct()
        assertNotNull(product)
        product!!
        assertEquals(0, BigDecimal("12.5").compareTo(product.basePrice))
        assertEquals(1, product.options.size)
        assertEquals(1, product.options[0].choices.size)
        assertEquals(1, product.tiers.size)
        assertEquals(0, BigDecimal("11.25").compareTo(QuoteCalculator.unitPrice(product, emptyMap(), BigDecimal(1000)).first))
    }

    @Test fun rejectsInvalid() {
        assertNull(EditableProduct(name = "", basePrice = "10").toProduct())
        assertNull(EditableProduct(name = "A", basePrice = "").toProduct())
        assertNull(EditableProduct(name = "A", basePrice = "abc").toProduct())
        assertNull(EditableProduct(name = "A", basePrice = "10", tiers = listOf(EditableTier(fromQuantity = "10", factor = "1/0"))).toProduct())
        assertNull(
            EditableProduct(name = "A", basePrice = "10", groups = listOf(EditableGroup(name = "G", choices = listOf(EditableChoice(name = "c", factor = "abc"))))).toProduct(),
        )
    }

    @Test fun settingsFromSheet() {
        val s = CompanySettings.fromSheet(
            mapOf(
                CompanySettings.S_LEAD_TIME to "5–15 рабочих дней",
                CompanySettings.S_VAT_INCLUDED to "нет",
                CompanySettings.S_FREE_DELIVERY to "30 000",
            ),
        )
        assertEquals("5–15 рабочих дней", s.leadTime)
        assertEquals(false, s.vatIncluded)
        assertEquals(0, BigDecimal(30000).compareTo(s.freeDeliveryThreshold))
        assertEquals("ООО «Солвер»", s.legalName) // не задано в таблице — значение по умолчанию
    }

    @Test fun legalTermsFromSheet() {
        val s = CompanySettings.fromSheet(
            mapOf(
                CompanySettings.S_TERM_PAYMENT to "Оплата: 100 % предоплата.",
                CompanySettings.S_TERM_PERSONAL to "",
                CompanySettings.S_KPP to "770501001",
            ),
        )
        // Пустое условие не печатается; заполненное в таблице — заменяет текст по умолчанию.
        assertEquals(5, s.legalTerms.size)
        assertEquals("Оплата: 100 % предоплата.", s.legalTerms[1])
        assertEquals("770501001", s.kpp)
        assertEquals("1257700099832", s.ogrn)
    }

    @Test fun draftJsonRoundTrip() {
        val d = QuoteDraft(
            number = 7, saved = true, clientCompany = "ООО Ромашка",
            lines = listOf(DraftLine(1, 2, mapOf(3L to 4L), "150")),
        )
        assertEquals(d, QuoteStore.draftFromJson(QuoteStore.draftToJson(d)))
    }
}
