package com.knit.calculator.quote

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test
import java.math.BigDecimal

class EditableProductTest {

    @Test fun roundTripKeepsCatalog() {
        DefaultCatalog.products.forEach { p ->
            assertEquals(p.copy(tiers = p.tiers.sortedBy { it.fromQuantity }).normalized(), EditableProduct.from(p).toProduct()!!.normalized())
        }
    }

    @Test fun parsesCommaDecimalsAndSkipsBlankRows() {
        val product = EditableProduct(
            name = "Шнур",
            unit = "м",
            basePrice = "12,5",
            minOrder = "",
            groups = listOf(
                EditableGroup(name = "Диаметр", choices = listOf(EditableChoice(name = "5 мм", priceAdd = "0"), EditableChoice(name = "", priceAdd = "3"))),
                EditableGroup(name = "", choices = listOf(EditableChoice(name = "x"))),
            ),
            tiers = listOf(EditableTier(fromQuantity = "1000", percent = "7,5"), EditableTier()),
        ).toProduct()
        assertNotNull(product)
        product!!
        assertEquals(0, BigDecimal("12.5").compareTo(product.basePrice))
        assertEquals(0, BigDecimal.ZERO.compareTo(product.minOrder))
        assertEquals(1, product.options.size)
        assertEquals(1, product.options[0].choices.size)
        assertEquals(1, product.tiers.size)
    }

    @Test fun rejectsInvalid() {
        assertNull(EditableProduct(name = "", basePrice = "10").toProduct())
        assertNull(EditableProduct(name = "A", basePrice = "").toProduct())
        assertNull(EditableProduct(name = "A", basePrice = "abc").toProduct())
        assertNull(EditableProduct(name = "A", basePrice = "10", tiers = listOf(EditableTier(fromQuantity = "10", percent = "150"))).toProduct())
    }

    private fun com.knit.calculator.core.Product.normalized() = copy(
        basePrice = basePrice.stripTrailingZeros(),
        minOrder = minOrder.stripTrailingZeros(),
        setupFee = setupFee.stripTrailingZeros(),
        options = options.map { g -> g.copy(choices = g.choices.map { it.copy(priceAdd = it.priceAdd.stripTrailingZeros()) }) },
        tiers = tiers.map { it.copy(fromQuantity = it.fromQuantity.stripTrailingZeros(), percent = it.percent.stripTrailingZeros()) },
    )
}
