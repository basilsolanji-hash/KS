package com.knit.calculator.staff

import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.quote.FormScreen
import com.knit.calculator.report.ReportSharing
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import java.io.File

private fun rub(v: Double) = "%,.2f ₽".format(java.util.Locale("ru"), v).replace(' ', ' ')
private fun num(v: Double) = if (v == Math.floor(v)) v.toLong().toString() else "%.3f".format(java.util.Locale.US, v).trimEnd('0').trimEnd('.')
private fun date(m: String) = m.take(16).replace('T', ' ').let { if (it.length >= 10) "${it.substring(8, 10)}.${it.substring(5, 7)}.${it.substring(0, 4)}${it.drop(10)}" else it }

/**
 * Заказы МойСклад: список (номер, дата, контрагент, организация, сумма, оплачено, статус) с поиском;
 * карточка — правка позиций и шапки, связанные документы (открыть, изменить, PDF: печать и отправка).
 */
@Composable
fun MsOrdersScreen(vm: OrdersViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val orders by vm.orders.collectAsStateWithLifecycle()
    val card by vm.card.collectAsStateWithLifecycle()
    val doc by vm.doc.collectAsStateWithLifecycle()
    val busy by vm.busy.collectAsStateWithLifecycle()
    val message by vm.message.collectAsStateWithLifecycle()
    var search by rememberSaveable { mutableStateOf("") }
    LaunchedEffect(Unit) { if (orders == null) vm.load() }

    val d = doc
    val c = card
    if (d != null) { DocScreen(vm, d) { vm.closeDoc() }; return }
    if (c != null) { OrderCardScreen(vm, c) { vm.close(); vm.load(search) }; return }

    FormScreen(stringResource(R.string.orders_title), onBack) {
        if (!vm.connected) {
            Text(stringResource(R.string.staff_need_server), color = colors.textSecondary, fontSize = 14.sp)
            return@FormScreen
        }
        KnitField(search, { search = it; vm.load(it) }, R.string.orders_search, text = true, maxLength = 60)
        if (busy) Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        orders?.let { if (it.isEmpty()) Text(stringResource(R.string.orders_empty), color = colors.textSecondary, fontSize = 14.sp) }
        orders.orEmpty().forEach { o ->
            Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth().clickable { vm.open(o.id) }) {
                Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("№ ${o.name}", color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                        Text(rub(o.sum), color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.Bold)
                    }
                    Text("${date(o.moment).take(10)} · ${o.agent}", color = colors.textPrimary, fontSize = 14.sp)
                    Text(o.organization, color = colors.textSecondary, fontSize = 13.sp)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        if (o.state.isNotEmpty()) {
                            Surface(shape = RoundedCornerShape(6.dp), color = stateColor(o.stateColor)) {
                                Text(o.state, color = Color.White, fontSize = 12.sp, modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp))
                            }
                        }
                        Text(
                            stringResource(R.string.orders_paid_shipped, rub(o.payed), rub(o.shipped)),
                            color = colors.textSecondary, fontSize = 12.sp, modifier = Modifier.padding(start = 8.dp),
                        )
                    }
                }
            }
        }
    }
}

/** Цвет статуса МойСклад (число RGB) — затемнённый, чтобы белый текст читался. */
private fun stateColor(c: Int): Color = if (c == 0) Color(0xFF607D8B) else Color(0xFF000000 or (c.toLong() and 0xFFFFFF)).let {
    Color(it.red * 0.8f, it.green * 0.8f, it.blue * 0.8f)
}

@Composable
private fun OrderCardScreen(vm: OrdersViewModel, card: MsOrderCard, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val h = card.header
    val busy by vm.busy.collectAsStateWithLifecycle()
    val message by vm.message.collectAsStateWithLifecycle()
    val positions = remember(card) { mutableStateListOf<MsPosition>().apply { addAll(card.positions) } }
    var description by remember(card) { mutableStateOf(h.optString("description")) }
    var address by remember(card) { mutableStateOf(h.optString("shipmentAddress")) }
    var stateId by remember(card) { mutableStateOf(h.optString("stateId")) }
    var editing by remember { mutableStateOf<Int?>(null) }
    var adding by remember { mutableStateOf(false) }
    var printing by remember { mutableStateOf(false) }
    val changed = positions.toList() != card.positions || description != h.optString("description") ||
        address != h.optString("shipmentAddress") || stateId != h.optString("stateId")
    val id = h.optString("id")
    val name = h.optString("name")

    FormScreen(stringResource(R.string.orders_card, name), onBack) {
        if (busy) Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(h.optString("agent"), color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                listOf(
                    h.optString("agentInn").takeIf { it.isNotEmpty() }?.let { "ИНН $it" },
                    listOf(h.optString("agentPhone"), h.optString("agentEmail")).filter { it.isNotEmpty() }.joinToString(" · ").takeIf { it.isNotEmpty() },
                    "${date(h.optString("moment"))} · ${h.optString("organization")}",
                    h.optString("store").takeIf { it.isNotEmpty() }?.let { "Склад: $it" },
                    stringResource(R.string.orders_totals, rub(h.optDouble("sum")), rub(h.optDouble("payed")), rub(h.optDouble("shipped"))),
                ).filterNotNull().forEach { Text(it, color = colors.textSecondary, fontSize = 13.sp) }
            }
        }
        if (card.states.isNotEmpty()) {
            SectionTitle(R.string.orders_state)
            Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                card.states.forEach { s ->
                    FilterChip(
                        selected = s.id == stateId, onClick = { stateId = s.id }, label = { Text(s.name) },
                        colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                    )
                }
            }
        }
        SectionTitle(R.string.orders_positions)
        positions.forEachIndexed { i, p ->
            Surface(shape = RoundedCornerShape(12.dp), color = colors.panel, modifier = Modifier.fillMaxWidth().clickable { editing = i }) {
                Column(Modifier.padding(horizontal = 12.dp, vertical = 8.dp)) {
                    Text(p.name, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                    Text(
                        "${num(p.quantity)} × ${rub(p.price)}" + (if (p.discount > 0) " − ${num(p.discount)} %" else "") + " = ${rub(p.total)}" +
                            (if (p.shipped > 0) " · отгружено ${num(p.shipped)}" else ""),
                        color = colors.textSecondary, fontSize = 13.sp,
                    )
                }
            }
        }
        ActionButton(R.string.orders_add_position, R.drawable.ic_add, primary = false, Modifier.fillMaxWidth()) { adding = true }
        KnitField(description, { description = it }, R.string.orders_comment, text = true, maxLength = 2000, singleLine = false)
        KnitField(address, { address = it }, R.string.orders_address, text = true, maxLength = 255)
        ActionButton(R.string.orders_save, R.drawable.ic_cloud, primary = changed, Modifier.fillMaxWidth()) {
            if (changed && !busy) vm.save(id, description, address, stateId, positions.toList()) {}
        }
        ActionButton(R.string.orders_pdf, R.drawable.ic_doc, primary = false, Modifier.fillMaxWidth()) { printing = true }
        if (card.related.isNotEmpty()) {
            SectionTitle(R.string.orders_related)
            card.related.forEach { r ->
                Surface(shape = RoundedCornerShape(12.dp), color = colors.panel, modifier = Modifier.fillMaxWidth().clickable { vm.openDoc(r.type, r.id) }) {
                    Row(Modifier.padding(horizontal = 12.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text("${MS_DOC_TITLES[r.type] ?: r.type} № ${r.name}", color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                            Text(date(r.moment) + (if (!r.applicable) " · не проведён" else ""), color = colors.textSecondary, fontSize = 12.sp)
                        }
                        Text(rub(r.sum), color = colors.textPrimary, fontSize = 14.sp)
                    }
                }
            }
        }
    }

    editing?.let { i ->
        val p = positions.getOrNull(i) ?: run { editing = null; return }
        var qty by remember(i) { mutableStateOf(num(p.quantity)) }
        var price by remember(i) { mutableStateOf(num(p.price)) }
        var disc by remember(i) { mutableStateOf(num(p.discount)) }
        AlertDialog(
            onDismissRequest = { editing = null },
            title = { Text(p.name, fontSize = 16.sp) },
            text = {
                Column {
                    KnitField(qty, { qty = it }, R.string.orders_qty)
                    KnitField(price, { price = it }, R.string.orders_price, suffix = "₽")
                    KnitField(disc, { disc = it }, R.string.orders_discount, suffix = "%")
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    val q = qty.replace(',', '.').toDoubleOrNull()
                    val pr = price.replace(',', '.').toDoubleOrNull()
                    val ds = disc.replace(',', '.').toDoubleOrNull() ?: 0.0
                    if (q != null && q > 0 && pr != null && pr >= 0 && ds in 0.0..100.0) {
                        positions[i] = p.copy(quantity = q, price = pr, discount = ds)
                        editing = null
                    }
                }) { Text(stringResource(R.string.shortcuts_save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
            },
            dismissButton = {
                Row {
                    TextButton(onClick = { if (positions.size > 1) positions.removeAt(i); editing = null }) { Text(stringResource(R.string.orders_remove), color = Color(0xFFD32F2F)) }
                    TextButton(onClick = { editing = null }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) }
                }
            },
            containerColor = colors.panel,
        )
    }

    if (adding) {
        val found by vm.found.collectAsStateWithLifecycle()
        var text by remember { mutableStateOf("") }
        AlertDialog(
            onDismissRequest = { adding = false },
            title = { Text(stringResource(R.string.orders_add_position)) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    KnitField(text, { text = it; vm.search(it) }, R.string.orders_search_product, text = true, maxLength = 60)
                    found.take(12).forEach { a ->
                        Text(
                            "${a.name}" + (if (a.code.isNotEmpty()) " · ${a.code}" else "") + " · ${rub(a.price)}" + (a.stock?.let { " · ост. ${num(it)}" } ?: ""),
                            color = colors.textPrimary, fontSize = 14.sp,
                            modifier = Modifier.fillMaxWidth().clickable {
                                positions.add(MsPosition("", a.name, a.id, a.type, a.code, 1.0, a.price, 0.0, 0, 0.0))
                                adding = false
                                editing = positions.lastIndex
                            }.padding(vertical = 6.dp),
                        )
                    }
                }
            },
            confirmButton = { TextButton(onClick = { adding = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }

    if (printing) PrintDialog(vm, "customerorder", id, "Заказ $name") { printing = false }
}

/** Печатные формы МойСклад → PDF: «Поделиться» (почта, Telegram, MAX…) или печать. */
@Composable
private fun PrintDialog(vm: OrdersViewModel, type: String, id: String, fileName: String, onClose: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    var list by remember { mutableStateOf<List<MsTemplate>?>(null) }
    var file by remember { mutableStateOf<File?>(null) }
    LaunchedEffect(type, id) { vm.templates(type) { list = it } }
    AlertDialog(
        onDismissRequest = onClose,
        title = { Text(stringResource(R.string.orders_pdf)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                val f = file
                if (f != null) {
                    Text(f.name, color = colors.textPrimary, fontSize = 14.sp)
                    ActionButton(R.string.orders_share, R.drawable.ic_share, primary = true, Modifier.fillMaxWidth()) {
                        ReportSharing.share(context, f, fileName, fileName)
                    }
                    ActionButton(R.string.orders_print, R.drawable.ic_doc, primary = false, Modifier.fillMaxWidth()) {
                        ReportSharing.print(context, f, fileName)
                    }
                } else if (list == null) {
                    Text(stringResource(R.string.sync_loading), color = colors.textSecondary)
                } else {
                    if (list!!.isEmpty()) Text(stringResource(R.string.orders_no_templates), color = colors.textSecondary)
                    list!!.forEach { t ->
                        Text(t.name, color = colors.textPrimary, fontSize = 15.sp,
                            modifier = Modifier.fillMaxWidth().clickable { vm.pdf(type, id, t, fileName) { file = it } }.padding(vertical = 8.dp))
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = onClose) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
        containerColor = colors.panel,
    )
}

/** Связанный документ: шапка, позиции, комментарий (и назначение платежа), проведение (руководство), PDF. */
@Composable
private fun DocScreen(vm: OrdersViewModel, card: MsDocCard, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val h = card.header
    val type = h.optString("type")
    val id = h.optString("id")
    val message by vm.message.collectAsStateWithLifecycle()
    var description by remember(card) { mutableStateOf(h.optString("description")) }
    var purpose by remember(card) { mutableStateOf(h.optString("paymentPurpose")) }
    var applicable by remember(card) { mutableStateOf(h.optBoolean("applicable", true)) }
    var printing by remember { mutableStateOf(false) }
    val payment = type == "paymentin" || type == "cashin"
    val title = "${MS_DOC_TITLES[type] ?: type} № ${h.optString("name")}"
    FormScreen(title, onBack) {
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        Text("${date(h.optString("moment"))} · ${h.optString("agent")}", color = colors.textPrimary, fontSize = 14.sp)
        Text("${h.optString("organization")} · ${rub(h.optDouble("sum"))}", color = colors.textSecondary, fontSize = 13.sp)
        card.positions.forEach { p ->
            Text("${p.name}: ${num(p.quantity)} × ${rub(p.price)} = ${rub(p.total)}", color = colors.textPrimary, fontSize = 13.sp)
        }
        if (payment) KnitField(purpose, { purpose = it }, R.string.orders_purpose, text = true, maxLength = 1000, singleLine = false)
        KnitField(description, { description = it }, R.string.orders_comment, text = true, maxLength = 2000, singleLine = false)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.orders_applicable), color = colors.textPrimary, modifier = Modifier.weight(1f))
            Switch(checked = applicable, onCheckedChange = { applicable = it })
        }
        ActionButton(R.string.orders_save, R.drawable.ic_cloud, primary = true, Modifier.fillMaxWidth()) {
            vm.saveDoc(type, id, description, if (payment) purpose else null,
                applicable.takeIf { it != h.optBoolean("applicable", true) })
        }
        ActionButton(R.string.orders_pdf, R.drawable.ic_doc, primary = false, Modifier.fillMaxWidth()) { printing = true }
    }
    if (printing) PrintDialog(vm, type, id, title) { printing = false }
}
