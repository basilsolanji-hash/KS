package com.knit.calculator.quote

import android.widget.Toast
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.RadioButton
import androidx.compose.material3.RadioButtonDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.Deal
import com.knit.calculator.core.Debts
import com.knit.calculator.core.OrderStage
import com.knit.calculator.core.ProductionOrder
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.QuoteStatus
import com.knit.calculator.core.WorkingDays
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.core.YarnMove
import com.knit.calculator.core.YarnStock
import com.knit.calculator.report.ReportSharing
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.math.BigDecimal
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone

private fun date(ms: Long) = if (ms <= 0) "—" else SimpleDateFormat("dd.MM.yyyy", Locale.getDefault()).format(Date(ms))
private fun rub(v: BigDecimal) = QuoteCalculator.formatMoney(v) + " ₽"
internal val DEBT_RED = Color(0xFFD93B3B)

/** Действия со сделкой из истории КП. */
enum class DealAction { INVOICE, CONTRACT, ORDER, PAYMENT, LABELS }

/** Готовый документ: отправить клиенту письмом, поделиться или напечатать. */
private data class ReadyDoc(
    val file: File,
    val subject: String,
    val text: String,
    val email: String,
    val kind: String,
    val quoteId: String,
    val invoiceNumber: Int? = null,
)

@Composable
private fun ReadyDocDialog(doc: ReadyDoc, quoteVm: QuoteViewModel, onDismiss: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val sync by quoteVm.sync.collectAsStateWithLifecycle()
    var sending by remember { mutableStateOf(false) }
    AlertDialog(
        onDismissRequest = { if (!sending) onDismiss() },
        title = { Text(doc.file.name) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                if (sync.connected && doc.email.isNotBlank()) {
                    ActionButton(R.string.doc_send_email, R.drawable.ic_email, primary = true, Modifier.fillMaxWidth()) {
                        if (sending) return@ActionButton
                        sending = true
                        scope.launch(Dispatchers.Main) {
                            val error = quoteVm.sendDocument(doc.file, doc.email, doc.subject, doc.text, doc.kind, doc.quoteId, doc.invoiceNumber)
                            sending = false
                            if (error == null) {
                                Toast.makeText(context, context.getString(R.string.doc_sent, doc.email), Toast.LENGTH_LONG).show()
                                onDismiss()
                            } else {
                                Toast.makeText(context, context.getString(R.string.email_send_failed, error), Toast.LENGTH_LONG).show()
                                ReportSharing.email(context, doc.file, doc.subject, doc.text, arrayOf(doc.email))
                            }
                        }
                    }
                    Text(doc.email, color = colors.textSecondary, fontSize = 13.sp)
                }
                ActionButton(R.string.yarn_pdf, R.drawable.ic_share, primary = !(sync.connected && doc.email.isNotBlank()), Modifier.fillMaxWidth()) {
                    quoteVm.uploadDocument(doc.file, doc.invoiceNumber)
                    ReportSharing.share(context, doc.file, doc.subject, doc.text)
                    onDismiss()
                }
                ActionButton(R.string.yarn_print, R.drawable.ic_print, primary = false, Modifier.fillMaxWidth()) {
                    quoteVm.uploadDocument(doc.file, doc.invoiceNumber)
                    ReportSharing.print(context, doc.file, doc.subject)
                    onDismiss()
                }
                if (sending) Text(stringResource(R.string.doc_sending), color = colors.textSecondary, fontSize = 14.sp)
            }
        },
        confirmButton = { TextButton(onClick = onDismiss, enabled = !sending) { Text(stringResource(R.string.close), color = colors.textPrimary) } },
        containerColor = colors.panel,
        titleContentColor = colors.textPrimary,
        textContentColor = colors.textSecondary,
    )
}

/**
 * Диалоги действий со сделкой: счёт, договор, заказ на производство, оплата.
 * [request] — что делать и с каким КП; `null` — ничего не показано.
 */
@Composable
fun DealActionHost(
    request: Pair<HistoryItem, DealAction>?,
    quoteVm: QuoteViewModel,
    opsVm: OpsViewModel,
    onDismiss: () -> Unit,
    onOpenProduction: () -> Unit,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val ops by opsVm.data.collectAsStateWithLifecycle()
    val settings by quoteVm.settings.collectAsStateWithLifecycle()
    var ready by remember { mutableStateOf<ReadyDoc?>(null) }
    var busy by remember { mutableStateOf(false) }

    ready?.let { doc -> ReadyDocDialog(doc, quoteVm) { ready = null } }
    val (item, action) = request ?: return
    val deal = remember(item) { quoteVm.deal(item) }
    if (deal == null) {
        LaunchedEffect(item) {
            withContext(Dispatchers.Main) {
                Toast.makeText(context, R.string.history_open_error, Toast.LENGTH_LONG).show()
                onDismiss()
            }
        }
        return
    }
    val paid = ops.paymentsForDebts.filter { it.quoteId == item.id }.fold(BigDecimal.ZERO) { a, p -> a + p.amount }
    val remaining = (deal.total - paid).max(BigDecimal.ZERO)
    fun toast(text: String) = Toast.makeText(context, text, Toast.LENGTH_LONG).show()

    when (action) {
        DealAction.INVOICE -> {
            val prepay = Debts.prepayment(deal.total, settings.prepay)
            val options = buildList {
                if (paid.signum() == 0 && settings.prepay > BigDecimal.ZERO && settings.prepay < BigDecimal(100)) {
                    add(context.getString(R.string.invoice_prepay, YarnCalculator.formatCompact(settings.prepay, 2), deal.quoteNumber) to prepay)
                }
                if (paid.signum() > 0 && remaining.signum() > 0) add(context.getString(R.string.invoice_rest, deal.quoteNumber) to remaining)
                add(context.getString(R.string.invoice_full, deal.quoteNumber) to deal.total)
            }
            var choice by remember(item) { mutableStateOf(0) }
            ChoiceDialog(
                title = stringResource(R.string.invoice_title_for, deal.quoteNumber, deal.client),
                options = options.map { "${it.first} — ${rub(it.second)}" },
                selected = choice,
                onSelect = { choice = it },
                confirm = stringResource(R.string.invoice_create),
                busy = busy,
                onDismiss = onDismiss,
            ) {
                val (purpose, amount) = options[choice]
                busy = true
                scope.launch(Dispatchers.Main) {
                    val result = opsVm.createInvoice(item.id, deal.quoteNumber, deal.client, amount, purpose)
                    busy = false
                    result.onFailure { toast(context.getString(R.string.ops_error, it.message ?: "")) }
                    result.onSuccess { inv ->
                        val file = withContext(Dispatchers.IO) { runCatching { DocPdf.invoice(context, settings, inv, deal) }.getOrNull() }
                        if (file == null) {
                            toast(context.getString(R.string.yarn_pdf_error))
                        } else {
                            val subject = context.getString(R.string.invoice_subject, inv.number, settings.brand)
                            val body = context.getString(R.string.invoice_email, inv.number, deal.quoteNumber, rub(inv.amount), settings.signature, settings.phone, settings.email) +
                                "\n\n" + settings.emailDisclaimer
                            ready = ReadyDoc(file, subject, body, deal.clientEmail, "invoice", item.id, inv.number)
                        }
                        onDismiss()
                    }
                }
            }
        }
        DealAction.CONTRACT -> {
            LaunchedEffect(item) { withContext(Dispatchers.Main) {
                val file = withContext(Dispatchers.IO) {
                    runCatching { DocPdf.contract(context, settings, deal, quoteVm.contractParagraphs(), YarnCalculator.formatCompact(settings.prepay, 2)) }.getOrNull()
                }
                if (file == null) {
                    toast(context.getString(R.string.yarn_pdf_error))
                } else {
                    val subject = context.getString(R.string.contract_subject, deal.quoteNumber, settings.brand)
                    val body = context.getString(R.string.contract_email, deal.quoteNumber, settings.signature, settings.phone, settings.email) +
                        "\n\n" + settings.emailDisclaimer
                    ready = ReadyDoc(file, subject, body, deal.clientEmail, "contract", item.id)
                }
                onDismiss()
            } }
        }
        DealAction.ORDER -> {
            LaunchedEffect(item) { withContext(Dispatchers.Main) {
                if (ops.orders.any { it.quoteId == item.id }) {
                    toast(context.getString(R.string.order_exists, deal.quoteNumber))
                    onDismiss()
                    onOpenProduction()
                    return@withContext
                }
                val now = System.currentTimeMillis()
                val days = WorkingDays.maxDays(settings.leadTime) ?: 15
                val (items, yarn) = quoteVm.orderContent(item)
                val order = ProductionOrder(
                    quoteId = item.id, quoteNumber = deal.quoteNumber, client = deal.client, created = now,
                    due = WorkingDays.add(now, days, TimeZone.getDefault().getOffset(now).toLong()) + 86_399_000L,
                    items = items, yarn = yarn,
                )
                val error = opsVm.saveOrder(order)
                if (error != null) {
                    toast(context.getString(R.string.ops_error, error))
                } else {
                    toast(context.getString(R.string.order_created, deal.quoteNumber, date(order.due)))
                    if (item.status == QuoteStatus.SENT || item.status == QuoteStatus.APPROVED) quoteVm.setStatus(item, QuoteStatus.IN_WORK)
                }
                onDismiss()
                if (error == null) onOpenProduction()
            } }
        }
        // Этикетки открываются отдельным экраном (история КП → «Этикетки»).
        DealAction.LABELS -> LaunchedEffect(item) { onDismiss() }
        DealAction.PAYMENT -> {
            var amount by remember(item) { mutableStateOf(QuoteCalculator.money(remaining).stripTrailingZeros().toPlainString().replace('.', ',')) }
            var note by remember(item) { mutableStateOf("") }
            // Сумма больше долга (опечатка?) — сохраняем только после второго подтверждения.
            var overpayConfirmed by remember(item) { mutableStateOf(false) }
            val entered = YarnCalculator.parseDecimal(amount)
            val overpay = entered != null && entered > remaining
            val colors = LocalKnitColors.current
            AlertDialog(
                onDismissRequest = { if (!busy) onDismiss() },
                title = { Text(stringResource(R.string.payment_title, deal.quoteNumber)) },
                text = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.payment_state, rub(deal.total), rub(paid), rub(remaining)), fontSize = 14.sp)
                        KnitField(amount, { amount = it }, R.string.payment_amount, suffix = "₽")
                        KnitField(note, { note = it }, R.string.payment_note, text = true, maxLength = 100)
                        if (overpay && entered != null) {
                            Text(
                                stringResource(R.string.payment_overpay, rub(entered - remaining)),
                                color = DEBT_RED, fontSize = 14.sp, fontWeight = FontWeight.SemiBold,
                            )
                        }
                    }
                },
                confirmButton = {
                    TextButton(
                        enabled = !busy,
                        onClick = {
                            val value = YarnCalculator.parseDecimal(amount)?.takeIf { it.signum() > 0 }
                            if (value == null) toast(context.getString(R.string.payment_invalid))
                            else if (overpay && !overpayConfirmed) overpayConfirmed = true
                            else scope.launch(Dispatchers.Main) {
                                busy = true
                                val error = opsVm.addPayment(item.id, deal.quoteNumber, deal.client, value, System.currentTimeMillis(), note)
                                busy = false
                                if (error != null) {
                                    toast(context.getString(R.string.ops_error, error))
                                    return@launch
                                }
                                // Оплачено полностью — статус КП «Оплачено».
                                if (paid + value >= deal.total && item.status != QuoteStatus.PAID) quoteVm.setStatus(item, QuoteStatus.PAID)
                                toast(context.getString(R.string.payment_saved, rub(value)))
                                onDismiss()
                            }
                        },
                    ) {
                        Text(
                            stringResource(if (overpay && overpayConfirmed) R.string.payment_overpay_confirm else R.string.save),
                            color = if (overpay && overpayConfirmed) DEBT_RED else colors.textPrimary, fontWeight = FontWeight.SemiBold,
                        )
                    }
                },
                dismissButton = { TextButton(onClick = onDismiss, enabled = !busy) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
                containerColor = colors.panel,
                titleContentColor = colors.textPrimary,
                textContentColor = colors.textSecondary,
            )
        }
    }
}

@Composable
private fun ChoiceDialog(
    title: String,
    options: List<String>,
    selected: Int,
    onSelect: (Int) -> Unit,
    confirm: String,
    busy: Boolean,
    onDismiss: () -> Unit,
    onConfirm: () -> Unit,
) {
    val colors = LocalKnitColors.current
    AlertDialog(
        onDismissRequest = { if (!busy) onDismiss() },
        title = { Text(title) },
        text = {
            Column {
                options.forEachIndexed { i, text ->
                    Row(Modifier.fillMaxWidth().clickable { onSelect(i) }.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                        RadioButton(
                            selected = i == selected, onClick = { onSelect(i) },
                            colors = RadioButtonDefaults.colors(selectedColor = colors.accent, unselectedColor = colors.textSecondary),
                        )
                        Text(text, color = colors.textPrimary, fontSize = 15.sp)
                    }
                }
                if (busy) Text(stringResource(R.string.sync_saving), color = colors.textSecondary, fontSize = 14.sp)
            }
        },
        confirmButton = { TextButton(onClick = onConfirm, enabled = !busy) { Text(confirm, color = colors.textPrimary, fontWeight = FontWeight.SemiBold) } },
        dismissButton = { TextButton(onClick = onDismiss, enabled = !busy) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
        containerColor = colors.panel,
        titleContentColor = colors.textPrimary,
        textContentColor = colors.textSecondary,
    )
}

@Composable
private fun OpsCard(content: @Composable () -> Unit) {
    Surface(shape = RoundedCornerShape(18.dp), color = LocalKnitColors.current.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) { content() }
    }
}

@Composable
private fun OpsStatus(opsVm: OpsViewModel, quoteVm: QuoteViewModel) {
    val colors = LocalKnitColors.current
    val loading by opsVm.loading.collectAsStateWithLifecycle()
    val error by opsVm.error.collectAsStateWithLifecycle()
    val sync by quoteVm.sync.collectAsStateWithLifecycle()
    if (!sync.connected) Text(stringResource(R.string.ops_local_note), color = colors.textSecondary, fontSize = 14.sp)
    if (loading) Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
    error?.let { Text(stringResource(R.string.history_error, it), color = colors.textSecondary, fontSize = 14.sp) }
}

// ---------------------------------------------------------------- Оплаты и долги

@Composable
fun PaymentsScreen(quoteVm: QuoteViewModel, opsVm: OpsViewModel, onBack: () -> Unit, onOpenProduction: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val history by quoteVm.history.collectAsStateWithLifecycle()
    val deals by quoteVm.deals.collectAsStateWithLifecycle()
    val ops by opsVm.data.collectAsStateWithLifecycle()
    var request by remember { mutableStateOf<Pair<HistoryItem, DealAction>?>(null) }
    LaunchedEffect(Unit) {
        quoteVm.loadHistory()
        quoteVm.loadDeals()
        opsVm.load()
    }
    // Долги — по всем КП (не только последним 200 из истории).
    val items = deals ?: history.orEmpty()
    val report = Debts.report(items.map { Deal(it.id, it.number, it.client, it.total, it.status) }, ops.paymentsForDebts)

    FormScreen(stringResource(R.string.payments_title), onBack, actions = {
        KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.sync_refresh), { quoteVm.loadHistory(); quoteVm.loadDeals(); opsVm.load() })
    }) {
        OpsStatus(opsVm, quoteVm)
        if (ops.msPaid != null) Text(stringResource(R.string.payments_ms_note), color = colors.textSecondary, fontSize = 14.sp)
        ops.msError?.let { Text(stringResource(R.string.ms_error, it), color = colors.textPrimary, fontSize = 14.sp) }
        OpsCard {
            Text(stringResource(R.string.payments_total_debt), color = colors.textSecondary, fontSize = 14.sp)
            Text(rub(report.totalDebt), color = if (report.totalDebt.signum() > 0) DEBT_RED else colors.textPrimary, fontSize = 26.sp, fontWeight = FontWeight.Bold)
            Text(stringResource(R.string.payments_total_paid, rub(report.totalPaid)), color = colors.textSecondary, fontSize = 14.sp)
        }
        if (report.rows.isEmpty()) Text(stringResource(R.string.payments_empty), color = colors.textSecondary, fontSize = 14.sp)
        report.rows.forEach { row ->
            val item = items.firstOrNull { it.id == row.deal.quoteId } ?: return@forEach
            var expanded by remember(row.deal.quoteId) { mutableStateOf(false) }
            OpsCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)) {
                        Text(stringResource(R.string.payments_deal, row.deal.quoteNumber, row.deal.client), color = colors.textPrimary, fontWeight = FontWeight.SemiBold, fontSize = 16.sp)
                        Text(stringResource(R.string.payments_row, rub(row.deal.total), rub(row.paid)), color = colors.textSecondary, fontSize = 14.sp)
                    }
                    Text(
                        if (row.isClosed) stringResource(R.string.payments_closed) else rub(row.remaining),
                        color = if (row.isClosed) colors.textSecondary else DEBT_RED,
                        fontWeight = FontWeight.Bold,
                        fontSize = 16.sp,
                    )
                }
                Row {
                    if (!row.isClosed) TextButton(onClick = { request = item to DealAction.PAYMENT }) { Text(stringResource(R.string.payment_add), color = colors.textPrimary) }
                    TextButton(onClick = { request = item to DealAction.INVOICE }) { Text(stringResource(R.string.invoice_action), color = colors.textPrimary) }
                    Spacer(Modifier.weight(1f))
                    TextButton(onClick = { expanded = !expanded }) { Text(stringResource(if (expanded) R.string.payments_hide else R.string.payments_show), color = colors.textSecondary) }
                }
                if (expanded) {
                    ops.invoices.filter { it.quoteId == row.deal.quoteId }.forEach { inv ->
                        Text(stringResource(R.string.payments_invoice_line, inv.number, date(inv.date), rub(inv.amount), inv.purpose), color = colors.textSecondary, fontSize = 13.sp)
                    }
                    ops.payments.filter { it.quoteId == row.deal.quoteId }.sortedBy { it.date }.forEach { p ->
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(
                                stringResource(R.string.payments_payment_line, date(p.date), rub(p.amount), p.note),
                                color = colors.textPrimary, fontSize = 14.sp, modifier = Modifier.weight(1f),
                            )
                            KnitIconButton(R.drawable.ic_delete, stringResource(R.string.payment_delete), {
                                scope.launch(Dispatchers.Main) {
                                    opsVm.deletePayment(p)?.let { Toast.makeText(context, context.getString(R.string.ops_error, it), Toast.LENGTH_LONG).show() }
                                }
                            })
                        }
                    }
                }
            }
        }
    }
    DealActionHost(request, quoteVm, opsVm, onDismiss = { request = null }, onOpenProduction = onOpenProduction)
}

// ---------------------------------------------------------------- Производство

@Composable
fun ProductionScreen(quoteVm: QuoteViewModel, opsVm: OpsViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val sync by quoteVm.sync.collectAsStateWithLifecycle()
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val ops by opsVm.data.collectAsStateWithLifecycle()
    var showShipped by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { opsVm.load() }
    val now = System.currentTimeMillis()
    val orders = ops.orders.filter { showShipped || it.stage != OrderStage.SHIPPED }.sortedBy { it.due }
    val stock = YarnStock.balances(ops.moves)

    FormScreen(stringResource(R.string.production_title), onBack, actions = {
        KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.sync_refresh), { opsVm.load() })
    }) {
        OpsStatus(opsVm, quoteVm)
        Text(stringResource(R.string.production_hint), color = colors.textSecondary, fontSize = 14.sp)
        val overdue = ops.orders.count { it.isOverdue(now) }
        if (overdue > 0) Text(stringResource(R.string.production_overdue_count, overdue), color = DEBT_RED, fontWeight = FontWeight.SemiBold)
        if (orders.isEmpty()) Text(stringResource(R.string.production_empty), color = colors.textSecondary, fontSize = 14.sp)
        orders.forEach { order ->
            var menu by remember(order.quoteId) { mutableStateOf(false) }
            OpsCard {
                Text(stringResource(R.string.production_order, order.quoteNumber, order.client), color = colors.textPrimary, fontWeight = FontWeight.SemiBold, fontSize = 16.sp)
                Text(
                    stringResource(R.string.production_due, date(order.due)),
                    color = if (order.isOverdue(now)) DEBT_RED else colors.textSecondary,
                    fontWeight = if (order.isOverdue(now)) FontWeight.SemiBold else FontWeight.Normal,
                    fontSize = 14.sp,
                )
                if (order.items.isNotBlank()) Text(order.items, color = colors.textPrimary, fontSize = 14.sp)
                if (order.yarn.isNotEmpty()) {
                    val short = YarnStock.shortages(order.yarn, stock).filter { !order.yarnWrittenOff && it.missing.signum() > 0 }
                    Text(
                        stringResource(
                            if (order.yarnWrittenOff) R.string.production_yarn_written else R.string.production_yarn,
                            order.yarn.joinToString(", ") { "${it.yarn} ${YarnCalculator.formatCompact(it.kg, 3)} кг" },
                        ),
                        color = colors.textSecondary, fontSize = 13.sp,
                    )
                    if (short.isNotEmpty()) {
                        Text(
                            stringResource(R.string.production_yarn_short, short.joinToString(", ") { "${it.yarn} ${YarnCalculator.formatCompact(it.missing, 3)} кг" }),
                            color = DEBT_RED, fontSize = 13.sp,
                        )
                    }
                }
                Box {
                    Surface(
                        onClick = { menu = true },
                        shape = RoundedCornerShape(12.dp),
                        color = if (order.stage == OrderStage.SHIPPED) colors.equalsKey else colors.background,
                        contentColor = if (order.stage == OrderStage.SHIPPED) colors.equalsKeyText else colors.textPrimary,
                    ) {
                        Text(stringResource(R.string.production_stage, order.stage.title, date(order.stageDate)), fontSize = 14.sp, modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp))
                    }
                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }, containerColor = colors.panel) {
                        OrderStage.entries.forEach { st ->
                            DropdownMenuItem(
                                text = { Text(st.title, color = colors.textPrimary, fontWeight = if (st == order.stage) FontWeight.Bold else FontWeight.Normal) },
                                onClick = {
                                    menu = false
                                    if (st != order.stage) scope.launch(Dispatchers.Main) {
                                        opsVm.setStage(order, st, ownYarnStock = !sync.msEnabled)?.let { Toast.makeText(context, context.getString(R.string.ops_error, it), Toast.LENGTH_LONG).show() }
                                    }
                                },
                            )
                        }
                    }
                }
            }
        }
        TextButton(onClick = { showShipped = !showShipped }) {
            Text(stringResource(if (showShipped) R.string.production_hide_shipped else R.string.production_show_shipped), color = colors.textSecondary)
        }
    }
}

// ---------------------------------------------------------------- Склад пряжи

@Composable
fun StockScreen(quoteVm: QuoteViewModel, opsVm: OpsViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val ops by opsVm.data.collectAsStateWithLifecycle()
    LaunchedEffect(Unit) { opsVm.load() }
    val stock = YarnStock.balances(ops.moves)
    var dialog by remember { mutableStateOf<Boolean?>(null) } // true — приход, false — расход

    FormScreen(stringResource(R.string.stock_title), onBack, actions = {
        KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.sync_refresh), { opsVm.load() })
    }) {
        OpsStatus(opsVm, quoteVm)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            ActionButton(R.string.stock_in, R.drawable.ic_add, primary = true, Modifier.weight(1f)) { dialog = true }
            ActionButton(R.string.stock_out, R.drawable.ic_delete, primary = false, Modifier.weight(1f)) { dialog = false }
        }
        SectionTitle(R.string.stock_balances)
        if (stock.isEmpty()) Text(stringResource(R.string.stock_empty), color = colors.textSecondary, fontSize = 14.sp)
        stock.forEach { row ->
            OpsCard {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(row.yarn, color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                    Text(
                        "${YarnCalculator.formatCompact(row.kg, 3)} кг",
                        color = if (row.kg.signum() < 0) DEBT_RED else colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.Bold,
                    )
                }
            }
        }
        // Пряжа, которую ещё нужно списать на заказы (не ушедшие в вязку).
        val reserved = ops.orders.filter { !it.yarnWrittenOff && it.stage == OrderStage.NEW }.flatMap { it.yarn }
        if (reserved.isNotEmpty()) {
            SectionTitle(R.string.stock_reserved)
            val short = YarnStock.shortages(
                reserved.groupBy { it.yarn.trim().lowercase() }.map { (_, xs) -> xs.first().copy(kg = xs.fold(BigDecimal.ZERO) { a, y -> a + y.kg }) },
                stock,
            )
            short.forEach {
                Text(
                    stringResource(R.string.stock_reserved_line, it.yarn, YarnCalculator.formatCompact(it.need, 3), YarnCalculator.formatCompact(it.have, 3)) +
                        if (it.missing.signum() > 0) " · " + context.getString(R.string.stock_missing, YarnCalculator.formatCompact(it.missing, 3)) else "",
                    color = if (it.missing.signum() > 0) DEBT_RED else colors.textSecondary, fontSize = 14.sp,
                )
            }
        }
        SectionTitle(R.string.stock_moves)
        ops.moves.sortedByDescending { it.date }.take(30).forEach { m ->
            Text(
                "${date(m.date)} · ${m.yarn} · ${if (m.kg.signum() > 0) "+" else ""}${YarnCalculator.formatCompact(m.kg, 3)} кг" + if (m.reason.isNotBlank()) " · ${m.reason}" else "",
                color = colors.textSecondary, fontSize = 13.sp,
            )
        }
    }

    dialog?.let { income ->
        var yarn by remember { mutableStateOf("") }
        var kg by remember { mutableStateOf("") }
        var reason by remember { mutableStateOf("") }
        var busy by remember { mutableStateOf(false) }
        val names = (quoteVm.yarnNames() + stock.map { it.yarn }).distinctBy { it.lowercase() }
        AlertDialog(
            onDismissRequest = { if (!busy) dialog = null },
            title = { Text(stringResource(if (income) R.string.stock_in else R.string.stock_out)) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    KnitField(yarn, { yarn = it }, R.string.stock_yarn, text = true, maxLength = 60)
                    val hints = names.filter { yarn.isBlank() || (it.contains(yarn.trim(), ignoreCase = true) && !it.equals(yarn.trim(), ignoreCase = true)) }.take(5)
                    hints.forEach { h -> Text(h, color = colors.textPrimary, fontSize = 15.sp, modifier = Modifier.fillMaxWidth().clickable { yarn = h }.padding(vertical = 4.dp)) }
                    KnitField(kg, { kg = it }, R.string.stock_kg, suffix = "кг", keyboardType = KeyboardType.Decimal)
                    KnitField(reason, { reason = it }, R.string.stock_reason, text = true, maxLength = 100)
                }
            },
            confirmButton = {
                TextButton(enabled = !busy, onClick = {
                    val value = YarnCalculator.parseDecimal(kg)?.takeIf { it.signum() > 0 }
                    if (yarn.isBlank() || value == null) Toast.makeText(context, R.string.stock_invalid, Toast.LENGTH_LONG).show()
                    else scope.launch(Dispatchers.Main) {
                        busy = true
                        val move = YarnMove(opsVm.newMoveId(), System.currentTimeMillis(), yarn.trim(), if (income) value else value.negate(), reason.trim())
                        val error = opsVm.addMoves(listOf(move))
                        busy = false
                        if (error != null) Toast.makeText(context, context.getString(R.string.ops_error, error), Toast.LENGTH_LONG).show()
                        else dialog = null
                    }
                }) { Text(stringResource(R.string.save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
            },
            dismissButton = { TextButton(onClick = { dialog = null }, enabled = !busy) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
            titleContentColor = colors.textPrimary,
            textContentColor = colors.textSecondary,
        )
    }
}

// ---------------------------------------------------------------- QR-код ссылки

/** QR-код ссылки — показать клиенту с экрана телефона. */
@Composable
fun QrDialog(url: String, onDismiss: () -> Unit) {
    val colors = LocalKnitColors.current
    val bitmap = remember(url) { runCatching { DocPdf.qrBitmap(url, 720).asImageBitmap() }.getOrNull() }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.qr_title)) },
        text = {
            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                bitmap?.let {
                    androidx.compose.foundation.Image(it, stringResource(R.string.qr_title), Modifier.fillMaxWidth().aspectRatio(1f))
                }
                Text(url, color = colors.textSecondary, fontSize = 13.sp)
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.close), color = colors.textPrimary) } },
        containerColor = colors.panel,
        titleContentColor = colors.textPrimary,
        textContentColor = colors.textSecondary,
    )
}
