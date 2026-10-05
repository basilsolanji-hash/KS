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
import com.knit.calculator.ui.components.KnitField
import kotlinx.coroutines.launch
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
    var editing by remember { mutableStateOf<Product?>(null) }
    val director by quoteVm.director.collectAsStateWithLifecycle()
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
            onEdit = if (director) ({ card = null; editing = p }) else null,
        )
    }
    editing?.let { p -> ProductEditDialog(p, quoteVm) { editing = null } }
}

/** Тиражи МойСклад, для которых задаются цены (типы цен «1 штук» … «от 500 штук»). */
private val MS_TIER_QTY = listOf(1, 10, 20, 50, 100, 500)

/** Правка карточки товара МойСклад (директор). */
@Composable
private fun ProductEditDialog(p: Product, quoteVm: QuoteViewModel, onDismiss: () -> Unit) {
    val colors = LocalKnitColors.current
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    fun fmt(v: BigDecimal) = v.setScale(2, java.math.RoundingMode.HALF_UP).stripTrailingZeros().toPlainString().replace('.', ',')
    val current = remember(p) { tierPrices(p).associate { (q, v) -> q.toInt() to fmt(v) } }
    var name by remember { mutableStateOf(p.baseName.ifBlank { p.name }) }
    var article by remember { mutableStateOf(p.code) }
    var description by remember { mutableStateOf(p.description) }
    var weight by remember { mutableStateOf(p.weightGrams?.let(::fmt).orEmpty()) }
    var minPrice by remember { mutableStateOf(p.minPrice?.let(::fmt).orEmpty()) }
    var prices by remember { mutableStateOf(MS_TIER_QTY.associateWith { current[it].orEmpty() }) }
    var saving by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    AlertDialog(
        onDismissRequest = { if (!saving) onDismiss() },
        title = { Text(stringResource(R.string.products_edit_title), fontSize = 18.sp) },
        text = {
            Column(Modifier.heightIn(max = 520.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                if (p.externalType == "variant") Text(stringResource(R.string.products_edit_variant), color = colors.textSecondary, fontSize = 12.sp)
                KnitField(name, { name = it }, R.string.products_edit_name, text = true, maxLength = 255)
                KnitField(article, { article = it }, R.string.products_edit_article, text = true, maxLength = 100)
                KnitField(description, { description = it }, R.string.products_edit_description, text = true, singleLine = false, maxLength = 2000)
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    KnitField(weight, { weight = it }, R.string.products_edit_weight, suffix = "г", modifier = Modifier.weight(1f))
                    KnitField(minPrice, { minPrice = it }, R.string.products_edit_min, suffix = "₽", modifier = Modifier.weight(1f))
                }
                Text(stringResource(R.string.products_prices), color = colors.textSecondary, fontSize = 13.sp)
                MS_TIER_QTY.chunked(2).forEach { pair ->
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        pair.forEach { q ->
                            androidx.compose.material3.OutlinedTextField(
                                value = prices[q].orEmpty(),
                                onValueChange = { v -> prices = prices + (q to v.filter { it.isDigit() || it == ',' || it == '.' }.take(12)) },
                                label = { Text(stringResource(R.string.products_from_qty, q.toString(), p.unit)) },
                                suffix = { Text("₽") }, singleLine = true,
                                keyboardOptions = androidx.compose.foundation.text.KeyboardOptions(keyboardType = androidx.compose.ui.text.input.KeyboardType.Decimal),
                                modifier = Modifier.weight(1f),
                            )
                        }
                    }
                }
                error?.let { Text(it, color = androidx.compose.ui.graphics.Color(0xFFD32F2F), fontSize = 13.sp) }
                Text(stringResource(R.string.products_edit_hint), color = colors.textSecondary, fontSize = 12.sp)
            }
        },
        confirmButton = {
            TextButton(enabled = !saving, onClick = {
                fun num(t: String) = com.knit.calculator.core.YarnCalculator.parseDecimal(t)
                val changes = org.json.JSONObject()
                if (name.trim() != p.baseName.ifBlank { p.name }) changes.put("name", name.trim())
                if (article.trim() != p.code) changes.put("article", article.trim())
                if (description != p.description) changes.put("description", description)
                num(weight)?.takeIf { it != p.weightGrams }?.let { changes.put("weight", it.toDouble()) }
                num(minPrice)?.takeIf { it != p.minPrice }?.let { changes.put("minPrice", it.toDouble()) }
                val changedPrices = org.json.JSONObject()
                MS_TIER_QTY.forEach { q -> val v = prices[q].orEmpty(); if (v != current[q].orEmpty()) num(v)?.let { changedPrices.put(q.toString(), it.toDouble()) } }
                if (changedPrices.length() > 0) changes.put("prices", changedPrices)
                if (changes.length() == 0) { onDismiss(); return@TextButton }
                saving = true
                error = null
                scope.launch {
                    val e = quoteVm.updateMsProduct(p, changes)
                    saving = false
                    if (e == null) onDismiss() else error = e
                }
            }) { Text(stringResource(if (saving) R.string.sync_loading else R.string.products_edit_save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
        },
        dismissButton = { TextButton(enabled = !saving, onClick = onDismiss) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
        containerColor = colors.panel,
    )
}

/** Цены по тиражам: «от 1 шт — 201,53 ₽», «от 500 шт — 158,34 ₽». */
fun tierPrices(p: Product): List<Pair<BigDecimal, BigDecimal>> {
    val first = (p.minOrder.takeIf { it.signum() > 0 } ?: BigDecimal.ONE) to p.basePrice
    return listOf(first) + p.tiers.map { t -> t.fromQuantity to QuoteCalculator.unitPrice(p, emptyMap(), t.fromQuantity).first }
}

@Composable
private fun ProductCard(p: Product, store: String, onDismiss: () -> Unit, onQuote: () -> Unit, onLabel: () -> Unit, onEdit: (() -> Unit)? = null) {
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
                if (onEdit != null) TextButton(onClick = onEdit) { Text(stringResource(R.string.products_edit), color = colors.textPrimary) }
                TextButton(onClick = onLabel) { Text(stringResource(R.string.products_label), color = colors.textPrimary) }
                TextButton(onClick = onQuote) { Text(stringResource(R.string.products_to_quote), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.products_close), color = colors.textSecondary) } },
        containerColor = colors.panel,
    )
}
