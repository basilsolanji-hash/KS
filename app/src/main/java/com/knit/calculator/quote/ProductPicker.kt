package com.knit.calculator.quote

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import com.knit.calculator.R
import com.knit.calculator.core.MoySklad
import com.knit.calculator.core.Product
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Остаток товара МойСклад для подписи: «На складе Электросталь: 120 шт» или «Под заказ». */
@Composable
fun stockText(product: Product, store: String): String? {
    val stock = product.stock ?: return null
    return if (stock.signum() > 0) stringResource(R.string.ms_stock, store.ifBlank { "—" }, QuoteCalculator.formatQuantity(stock), product.unit)
    else stringResource(R.string.ms_to_order)
}

/**
 * Выбор позиции КП: изделия «под заказ» (калькулятор из таблицы) и товары МойСклад
 * с поиском по названию / артикулу и фильтром по группе.
 */
@Composable
fun ProductPicker(
    calculator: List<Product>,
    msProducts: List<Product>,
    sync: SyncStatus,
    filterNames: List<String> = emptyList(),
    onRefresh: () -> Unit,
    onPick: (Product) -> Unit,
    onDismiss: () -> Unit,
    favorites: Set<String> = emptySet(),
    recent: List<String> = emptyList(),
    onToggleFavorite: ((Product) -> Unit)? = null,
) {
    val colors = LocalKnitColors.current
    var query by rememberSaveable { mutableStateOf("") }
    var group by rememberSaveable { mutableStateOf<String?>(null) }
    val groups = remember(msProducts) { MoySklad.topGroups(msProducts) }
    // Фильтры по характеристикам МойСклад: характеристика → выбранное значение.
    var selected by remember { mutableStateOf<Map<String, String>>(emptyMap()) }
    val found = remember(msProducts, query, group, selected) { MoySklad.search(msProducts, query, group, selected) }
    val byExternal = remember(msProducts) { msProducts.associateBy { it.externalId } }
    // Пока ничего не ищем — сверху избранные и недавние товары.
    val idle = query.isBlank() && group == null && selected.isEmpty()
    val favoriteList = if (idle) favorites.mapNotNull { byExternal[it] }.sortedBy { it.name } else emptyList()
    val recentList = if (idle) recent.filterNot { it in favorites }.mapNotNull { byExternal[it] } else emptyList()
    Dialog(onDismissRequest = onDismiss, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
            ScreenTopBar(stringResource(R.string.picker_title), onDismiss) {
                if (sync.msEnabled) KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.ms_refresh), onRefresh)
            }
            LazyColumn(
                Modifier.fillMaxWidth().weight(1f).padding(horizontal = 16.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                if (calculator.isNotEmpty()) {
                    item { Header(stringResource(R.string.picker_calculator)) }
                    items(calculator, key = { "c" + it.id }) { p ->
                        PickRow(p.name, stringResource(R.string.quote_price_line, QuoteCalculator.formatMoney(p.basePrice), p.unit), null) { onPick(p) }
                    }
                }
                if (sync.msEnabled || msProducts.isNotEmpty()) {
                    item {
                        Header(stringResource(R.string.picker_moysklad, msProducts.size))
                        val updated = sync.msLoadedAt?.let { SimpleDateFormat("dd.MM HH:mm", Locale.getDefault()).format(Date(it)) }
                        Text(
                            when {
                                sync.msLoading -> stringResource(R.string.sync_loading)
                                sync.msError != null -> stringResource(R.string.ms_error, sync.msError)
                                updated != null -> stringResource(R.string.ms_updated, updated)
                                else -> ""
                            },
                            color = colors.textSecondary, fontSize = 13.sp,
                        )
                    }
                    item {
                        KnitField(query, { query = it }, R.string.picker_search, text = true, maxLength = 60)
                    }
                    if (groups.size > 1) {
                        item {
                            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                items(listOf<String?>(null) + groups) { g ->
                                    FilterChip(
                                        selected = g == group,
                                        onClick = { group = g },
                                        label = { Text(g ?: stringResource(R.string.picker_all_groups)) },
                                        colors = FilterChipDefaults.filterChipColors(
                                            selectedContainerColor = colors.equalsKey,
                                            selectedLabelColor = colors.equalsKeyText,
                                            labelColor = colors.textPrimary,
                                        ),
                                    )
                                }
                            }
                        }
                    }
                    filterNames.forEach { name ->
                        // Значения — среди найденного без учёта этого же фильтра.
                        val values = MoySklad.filterValues(MoySklad.search(msProducts, query, group, selected - name), name)
                        if (values.isNotEmpty()) {
                            item(key = "f$name") {
                                Column {
                                    Text(name, color = colors.textSecondary, fontSize = 13.sp)
                                    LazyRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                                        items(listOf<String?>(null) + values) { value ->
                                            FilterChip(
                                                selected = selected[name] == value,
                                                onClick = { selected = if (value == null) selected - name else selected + (name to value) },
                                                label = { Text(value ?: stringResource(R.string.picker_all_groups)) },
                                                colors = FilterChipDefaults.filterChipColors(
                                                    selectedContainerColor = colors.equalsKey,
                                                    selectedLabelColor = colors.equalsKeyText,
                                                    labelColor = colors.textPrimary,
                                                ),
                                            )
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (selected.isNotEmpty()) {
                        item(key = "freset") {
                            androidx.compose.material3.TextButton(onClick = { selected = emptyMap() }) {
                                Text(stringResource(R.string.ms_filters_reset), color = colors.textSecondary)
                            }
                        }
                    }
                    if (favoriteList.isNotEmpty()) {
                        item(key = "hfav") { Header(stringResource(R.string.picker_favorites)) }
                        items(favoriteList, key = { "f" + it.id }) { p -> MsRow(p, sync.msStore, filterNames, favorites, onToggleFavorite, onPick) }
                    }
                    if (recentList.isNotEmpty()) {
                        item(key = "hrec") { Header(stringResource(R.string.picker_recent)) }
                        items(recentList, key = { "r" + it.id }) { p -> MsRow(p, sync.msStore, filterNames, favorites, onToggleFavorite, onPick) }
                    }
                    if (favoriteList.isNotEmpty() || recentList.isNotEmpty()) {
                        item(key = "hall") { Header(stringResource(R.string.picker_all)) }
                    }
                    items(found.take(MAX_RESULTS), key = { "m" + it.id }) { p -> MsRow(p, sync.msStore, filterNames, favorites, onToggleFavorite, onPick) }
                    if (found.size > MAX_RESULTS) {
                        item { Text(stringResource(R.string.picker_more, found.size - MAX_RESULTS), color = colors.textSecondary, fontSize = 13.sp) }
                    }
                    if (found.isEmpty() && msProducts.isNotEmpty()) {
                        item { Text(stringResource(R.string.picker_nothing), color = colors.textSecondary, fontSize = 14.sp) }
                    }
                }
            }
        }
    }
}

private const val MAX_RESULTS = 80

@Composable
private fun Header(text: String) {
    Text(text, color = LocalKnitColors.current.textSecondary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 8.dp))
}

/** Строка товара МойСклад: цена от тиража, артикул, характеристики-фильтры, остаток, значки, звёздочка «избранное». */
@Composable
private fun MsRow(
    p: Product,
    store: String,
    filterNames: List<String>,
    favorites: Set<String>,
    onToggleFavorite: ((Product) -> Unit)?,
    onPick: (Product) -> Unit,
) {
    val from = p.tiers.lastOrNull()?.let { t -> QuoteCalculator.unitPrice(p, emptyMap(), t.fromQuantity).first }
    val price = buildList {
        add(stringResource(R.string.quote_price_line, QuoteCalculator.formatMoney(p.basePrice), p.unit))
        if (from != null && from < p.basePrice) add(stringResource(R.string.picker_from_price, QuoteCalculator.formatMoney(from)))
        if (p.code.isNotBlank()) add(p.code)
    }.joinToString(" · ")
    // Характеристики-фильтры, которых нет в названии (артикул производителя, тип резинки…).
    val details = filterNames.mapNotNull { n -> p.attributes[n]?.takeIf { v -> v.isNotBlank() && v != "-" && !p.name.contains(v) }?.let { "$n: $it" } }
        .joinToString(" · ")
    PickRow(
        p.name, listOf(price, details).filter { it.isNotBlank() }.joinToString("\n"), stockText(p, store), p.badges,
        favorite = if (onToggleFavorite == null) null else p.externalId in favorites,
        onFavorite = { onToggleFavorite?.invoke(p) },
    ) { onPick(p) }
}

@Composable
private fun PickRow(
    title: String,
    subtitle: String,
    stock: String?,
    badges: List<String> = emptyList(),
    favorite: Boolean? = null,
    onFavorite: () -> Unit = {},
    onClick: () -> Unit,
) {
    val colors = LocalKnitColors.current
    Surface(shape = RoundedCornerShape(16.dp), color = colors.panel, modifier = Modifier.fillMaxWidth().clickable(onClick = onClick)) {
        Row(Modifier.padding(start = 14.dp, end = 4.dp, top = 10.dp, bottom = 10.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                if (badges.isNotEmpty()) Badges(badges)
                Text(title, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Medium)
                Text(subtitle, color = colors.textSecondary, fontSize = 13.sp)
                if (stock != null) Text(stock, color = if (colors.isDark) colors.accent else colors.textPrimary, fontSize = 13.sp)
            }
            if (favorite != null) {
                KnitIconButton(
                    if (favorite) R.drawable.ic_star else R.drawable.ic_star_border,
                    stringResource(if (favorite) R.string.picker_unfavorite else R.string.picker_favorite),
                    onFavorite,
                )
            }
        }
    }
}

/** Значки «Топ-продажа», «Популярный». */
@Composable
fun Badges(badges: List<String>) {
    val colors = LocalKnitColors.current
    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.padding(bottom = 2.dp)) {
        badges.forEach {
            Surface(shape = RoundedCornerShape(8.dp), color = colors.equalsKey, contentColor = colors.equalsKeyText) {
                Text("★ $it", fontSize = 11.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp))
            }
        }
    }
}
