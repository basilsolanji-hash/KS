package com.knit.calculator.quote

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.Ean13
import com.knit.calculator.core.Product
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.ui.theme.LocalKnitColors
import java.math.BigDecimal

/**
 * Каталог МойСклад без создания КП: поиск, фильтры, избранное; карточка товара — цены по тиражам,
 * остаток, штрихкод, характеристики; из карточки — «В КП» или «Этикетка».
 */
@Composable
fun ProductsScreen(
    quoteVm: QuoteViewModel,
    onBack: () -> Unit,
    onAddToQuote: (Product) -> Unit,
    onLabel: (Product) -> Unit,
) {
    val msProducts by quoteVm.msProducts.collectAsStateWithLifecycle()
    val sync by quoteVm.sync.collectAsStateWithLifecycle()
    val msFilters by quoteVm.msFilters.collectAsStateWithLifecycle()
    val favorites by quoteVm.favorites.collectAsStateWithLifecycle()
    val recent by quoteVm.recent.collectAsStateWithLifecycle()
    var card by remember { mutableStateOf<Product?>(null) }
    LaunchedEffect(Unit) { quoteVm.refreshMs(maxAgeMs = 3_600_000L) }

    ProductPicker(
        calculator = emptyList(),
        msProducts = msProducts,
        sync = sync,
        filterNames = msFilters,
        onRefresh = { quoteVm.refreshMs(force = true) },
        onPick = { card = it },
        onDismiss = onBack,
        favorites = favorites,
        recent = recent,
        onToggleFavorite = quoteVm::toggleFavorite,
        title = stringResource(R.string.products_title),
    )
    card?.let { p ->
        ProductCard(
            p, sync.msStore,
            onDismiss = { card = null },
            onQuote = { card = null; quoteVm.rememberPicked(p); onAddToQuote(p) },
            onLabel = { card = null; quoteVm.rememberPicked(p); onLabel(p) },
        )
    }
}

/** Цены по тиражам: «от 1 шт — 201,53 ₽», «от 500 шт — 158,34 ₽». */
fun tierPrices(p: Product): List<Pair<BigDecimal, BigDecimal>> {
    val first = (p.minOrder.takeIf { it.signum() > 0 } ?: BigDecimal.ONE) to p.basePrice
    return listOf(first) + p.tiers.map { t -> t.fromQuantity to QuoteCalculator.unitPrice(p, emptyMap(), t.fromQuantity).first }
}

@Composable
private fun ProductCard(p: Product, store: String, onDismiss: () -> Unit, onQuote: () -> Unit, onLabel: () -> Unit) {
    val colors = LocalKnitColors.current
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(p.name, fontSize = 18.sp) },
        text = {
            Column(Modifier.heightIn(max = 460.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                if (p.badges.isNotEmpty()) Badges(p.badges)
                if (p.group.isNotBlank()) Text(p.group, color = colors.textSecondary, fontSize = 13.sp)
                stockText(p, store)?.let { Text(it, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold) }
                Text(stringResource(R.string.products_prices), color = colors.textSecondary, fontSize = 13.sp)
                tierPrices(p).forEach { (qty, price) ->
                    Row(Modifier.fillMaxWidth()) {
                        Text(
                            stringResource(R.string.products_from_qty, QuoteCalculator.formatQuantity(qty), p.unit),
                            color = colors.textPrimary, fontSize = 15.sp, modifier = Modifier.weight(1f),
                        )
                        Text(QuoteCalculator.formatMoney(price) + " ₽", color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                    }
                }
                p.minPrice?.let { Text(stringResource(R.string.products_min_price, QuoteCalculator.formatMoney(it)), color = colors.textSecondary, fontSize = 13.sp) }
                p.buyPrice?.let { Text(stringResource(R.string.products_buy_price, QuoteCalculator.formatMoney(it)), color = colors.textSecondary, fontSize = 13.sp) }
                Text(
                    if (Ean13.isValid(p.barcode)) stringResource(R.string.label_barcode, Ean13.human(p.barcode)) else stringResource(R.string.label_no_barcode),
                    color = colors.textSecondary, fontSize = 13.sp,
                )
                val attrs = p.attributes.filterValues { it.isNotBlank() && it != "-" }
                if (attrs.isNotEmpty()) {
                    Text(stringResource(R.string.products_chars), color = colors.textSecondary, fontSize = 13.sp)
                    attrs.forEach { (k, v) -> Text("$k: $v", color = colors.textPrimary, fontSize = 14.sp) }
                }
            }
        },
        confirmButton = {
            Row {
                TextButton(onClick = onLabel) { Text(stringResource(R.string.products_label), color = colors.textPrimary) }
                TextButton(onClick = onQuote) { Text(stringResource(R.string.products_to_quote), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.products_close), color = colors.textSecondary) } },
        containerColor = colors.panel,
    )
}
