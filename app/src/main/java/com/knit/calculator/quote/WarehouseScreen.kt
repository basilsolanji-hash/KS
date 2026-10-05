package com.knit.calculator.quote

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.ShipLine
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.theme.LocalKnitColors
import java.math.BigDecimal
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

private val GREEN = Color(0xFF2E9E5B)
private val RED = Color(0xFFD32F2F)
private fun q(v: BigDecimal) = v.stripTrailingZeros().toPlainString().replace('.', ',')

/**
 * Склад по сканеру: «Отгрузка» — заказ МойСклад, сканируем товары, приложение сверяет с заказом и создаёт отгрузку;
 * «Инвентаризация» — сканируем и считаем, документ «Инвентаризация» создаётся в МойСклад.
 */
@Composable
fun WarehouseScreen(quoteVm: QuoteViewModel, vm: WarehouseViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val catalog by quoteVm.msProducts.collectAsStateWithLifecycle()
    val orders by vm.orders.collectAsStateWithLifecycle()
    val order by vm.order.collectAsStateWithLifecycle()
    val lines by vm.lines.collectAsStateWithLifecycle()
    val count by vm.count.collectAsStateWithLifecycle()
    val loading by vm.loading.collectAsStateWithLifecycle()
    val message by vm.message.collectAsStateWithLifecycle()
    var tab by rememberSaveable { mutableStateOf(0) }
    var confirmShip by remember { mutableStateOf(false) }
    var confirmCount by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) {
        quoteVm.refreshMs(maxAgeMs = 3_600_000L)
        if (vm.orders.value == null) vm.loadOrders()
    }

    fun onCode(code: String) = if (tab == 0) vm.scanShip(code, catalog) else vm.scanCount(code, catalog)

    FormScreen(stringResource(R.string.wh_title), { if (order != null) vm.closeOrder() else onBack() }, actions = {
        if (tab == 0 && order == null) KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.sync_refresh), { vm.loadOrders() })
    }) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(R.string.wh_tab_ship, R.string.wh_tab_count).forEachIndexed { i, label ->
                FilterChip(
                    selected = tab == i, onClick = { tab = i }, label = { Text(stringResource(label)) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                )
            }
        }
        if (loading) Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
        message?.let {
            val bad = it.startsWith("Лишнее") || it.startsWith("Нет в заказе") || it.contains("не найден")
            Text(it, color = if (bad) RED else colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
        }

        val scanning = (tab == 0 && order != null) || tab == 1
        if (scanning) ScanInput(onCode = ::onCode, onCamera = {
            BarcodeScan.scan(context, ::onCode) { e -> android.widget.Toast.makeText(context, e, android.widget.Toast.LENGTH_LONG).show() }
        })

        if (tab == 0) {
            val o = order
            if (o == null) {
                Text(stringResource(R.string.wh_pick_order), color = colors.textSecondary, fontSize = 14.sp)
                orders?.let { list ->
                    if (list.isEmpty()) Text(stringResource(R.string.wh_no_orders), color = colors.textSecondary, fontSize = 14.sp)
                    list.forEach { x ->
                        Surface(onClick = { vm.clearMessage(); vm.openOrder(x) }, shape = RoundedCornerShape(16.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                            Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp)) {
                                Text("№ ${x.name} · ${x.client}", color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                                Text(
                                    stringResource(
                                        R.string.wh_order_line,
                                        SimpleDateFormat("dd.MM.yyyy", Locale.getDefault()).format(Date(x.moment)),
                                        QuoteCalculator.formatMoney(x.sum), QuoteCalculator.formatMoney(x.shipped),
                                    ),
                                    color = colors.textSecondary, fontSize = 13.sp,
                                )
                            }
                        }
                    }
                }
            } else {
                Text("№ ${o.name} · ${o.client}", color = colors.textPrimary, fontSize = 17.sp, fontWeight = FontWeight.Bold)
                lines.forEachIndexed { i, l -> ShipRow(l, onMinus = { vm.adjustShip(i, -1) }, onPlus = { vm.adjustShip(i, 1) }) }
                if (lines.any { it.scanned.signum() > 0 }) {
                    ActionButton(R.string.wh_ship, R.drawable.ic_factory, primary = true, Modifier.fillMaxWidth()) { confirmShip = true }
                }
            }
        } else {
            if (count.isEmpty()) Text(stringResource(R.string.wh_count_hint), color = colors.textSecondary, fontSize = 14.sp)
            count.forEach { item ->
                Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                    Row(Modifier.padding(start = 14.dp, end = 4.dp, top = 8.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(item.name, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Medium)
                            item.stock?.let { st ->
                                val diff = item.qty - st
                                Text(
                                    stringResource(R.string.wh_count_line, q(item.qty), q(st), (if (diff.signum() > 0) "+" else "") + q(diff)),
                                    color = if (diff.signum() == 0) GREEN else RED, fontSize = 13.sp,
                                )
                            } ?: Text(stringResource(R.string.wh_counted, q(item.qty)), color = colors.textSecondary, fontSize = 13.sp)
                        }
                        KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.wh_minus), { vm.setCountQty(item.id, item.qty - BigDecimal.ONE) })
                        KnitIconButton(R.drawable.ic_arrow_up, stringResource(R.string.wh_plus), { vm.setCountQty(item.id, item.qty + BigDecimal.ONE) })
                        KnitIconButton(R.drawable.ic_delete, stringResource(R.string.wh_remove), { vm.removeCount(item.id) })
                    }
                }
            }
            if (count.isNotEmpty()) {
                ActionButton(R.string.wh_save_count, R.drawable.ic_inventory, primary = true, Modifier.fillMaxWidth()) { confirmCount = true }
                TextButton(onClick = { vm.clearCount() }) { Text(stringResource(R.string.wh_clear), color = colors.textSecondary) }
            }
        }
    }

    if (confirmShip) {
        val s = com.knit.calculator.core.Warehouse.summary(lines)
        AlertDialog(
            onDismissRequest = { confirmShip = false },
            title = { Text(stringResource(R.string.wh_ship_confirm, order?.name.orEmpty())) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text(stringResource(R.string.wh_ship_total, q(s.scanned)), color = colors.textPrimary)
                    if (s.short.isNotEmpty()) Text(stringResource(R.string.wh_ship_short, s.short.joinToString { it.name + " — " + q(it.remaining - it.scanned) }), color = RED, fontSize = 13.sp)
                    if (s.over.isNotEmpty()) Text(stringResource(R.string.wh_ship_over, s.over.joinToString { it.name }), color = RED, fontSize = 13.sp)
                }
            },
            confirmButton = {
                TextButton(enabled = s.over.isEmpty(), onClick = { confirmShip = false; vm.ship {} }) {
                    Text(stringResource(R.string.wh_ship), color = colors.textPrimary, fontWeight = FontWeight.SemiBold)
                }
            },
            dismissButton = { TextButton(onClick = { confirmShip = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
    if (confirmCount) {
        AlertDialog(
            onDismissRequest = { confirmCount = false },
            title = { Text(stringResource(R.string.wh_save_count)) },
            text = { Text(stringResource(R.string.wh_count_confirm, count.size), color = colors.textPrimary) },
            confirmButton = {
                TextButton(onClick = { confirmCount = false; vm.saveInventory() }) {
                    Text(stringResource(R.string.wh_save_count), color = colors.textPrimary, fontWeight = FontWeight.SemiBold)
                }
            },
            dismissButton = { TextButton(onClick = { confirmCount = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
}

/** Поле для ручного сканера (вводит код и Enter) и кнопка камеры. */
@Composable
private fun ScanInput(onCode: (String) -> Unit, onCamera: () -> Unit) {
    val colors = LocalKnitColors.current
    var text by remember { mutableStateOf("") }
    fun submit() {
        val c = text.trim()
        text = ""
        if (c.isNotEmpty()) onCode(c)
    }
    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        OutlinedTextField(
            value = text,
            onValueChange = { v ->
                // Сканер-«клавиатура» завершает код переводом строки.
                if (v.contains('\n')) { text = v.replace("\n", ""); submit() } else text = v.take(60)
            },
            label = { Text(stringResource(R.string.wh_code)) },
            singleLine = true,
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Done),
            keyboardActions = KeyboardActions(onDone = { submit() }),
            modifier = Modifier.weight(1f),
        )
        Surface(onClick = onCamera, shape = RoundedCornerShape(16.dp), color = colors.equalsKey, contentColor = colors.equalsKeyText) {
            androidx.compose.material3.Icon(
                androidx.compose.ui.res.painterResource(R.drawable.ic_scan), stringResource(R.string.scan_barcode),
                Modifier.padding(16.dp),
            )
        }
    }
}

@Composable
private fun ShipRow(l: ShipLine, onMinus: () -> Unit, onPlus: () -> Unit) {
    val colors = LocalKnitColors.current
    val color = when {
        l.over -> RED
        l.done -> GREEN
        else -> colors.textSecondary
    }
    Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Row(Modifier.padding(start = 14.dp, end = 4.dp, top = 8.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(l.name, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Medium)
                Text(
                    stringResource(R.string.wh_ship_line, q(l.scanned), q(l.remaining)) + (if (l.shipped.signum() > 0) " · " + stringResource(R.string.wh_shipped_before, q(l.shipped)) else ""),
                    color = color, fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                )
            }
            KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.wh_minus), onMinus)
            KnitIconButton(R.drawable.ic_arrow_up, stringResource(R.string.wh_plus), onPlus)
        }
    }
}
