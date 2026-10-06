package com.knit.calculator.quote

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.Deal
import com.knit.calculator.core.Debts
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import java.math.BigDecimal

/**
 * Карточка клиента: контакты (звонок, WhatsApp, письмо), все его КП, сумма, оплачено и долг;
 * «Новое КП» — с реквизитами клиента.
 */
@Composable
fun ClientScreen(quoteVm: QuoteViewModel, opsVm: OpsViewModel, clientName: String, onBack: () -> Unit, onNewQuote: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val deals by quoteVm.deals.collectAsStateWithLifecycle()
    val history by quoteVm.history.collectAsStateWithLifecycle()
    val clients by quoteVm.clients.collectAsStateWithLifecycle()
    val ops by opsVm.data.collectAsStateWithLifecycle()
    LaunchedEffect(Unit) {
        quoteVm.loadHistory()
        quoteVm.loadDeals()
        opsVm.loadIfStale(60_000L)
    }
    val key = clientName.trim().lowercase()
    val client = clients.firstOrNull { it.company.trim().lowercase() == key }
    // История (последние КП, свежие) + все КП (долги): без повторов.
    val items = (history.orEmpty() + deals.orEmpty()).distinctBy { it.id.ifBlank { it.number.toString() } }
        .filter { it.client.trim().lowercase() == key }.sortedByDescending { it.number }
    val report = Debts.report(items.map { Deal(it.id, it.number, it.client, it.total, it.status) }, ops.paymentsForDebts)
    val total = items.fold(BigDecimal.ZERO) { a, q -> a + q.total }
    fun rub(v: BigDecimal) = QuoteCalculator.formatMoney(v) + " ₽"
    fun open(uri: String) = runCatching { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(uri)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }

    FormScreen(clientName.ifBlank { stringResource(R.string.client_title) }, onBack) {
        client?.let { c ->
            if (c.inn.isNotBlank()) Text(stringResource(R.string.client_inn, c.inn), color = colors.textSecondary, fontSize = 14.sp)
            if (c.contact.isNotBlank()) Text(c.contact, color = colors.textPrimary, fontSize = 16.sp)
            val phoneDigits = c.phone.filter { it.isDigit() }.let { if (it.length == 11 && it.startsWith("8")) "7" + it.drop(1) else it }
            if (phoneDigits.length >= 10 || c.email.isNotBlank()) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    if (phoneDigits.length >= 10) {
                        ActionButton(R.string.client_call, R.drawable.ic_share, primary = true, Modifier.weight(1f)) { open("tel:+$phoneDigits") }
                        ActionButton(R.string.client_whatsapp, R.drawable.ic_share, primary = false, Modifier.weight(1f)) { open("https://wa.me/$phoneDigits") }
                    }
                    if (c.email.isNotBlank()) {
                        ActionButton(R.string.client_email, R.drawable.ic_email, primary = false, Modifier.weight(1f)) { open("mailto:${c.email.trim()}") }
                    }
                }
            }
        }
        Surface(shape = RoundedCornerShape(18.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                Text(stringResource(R.string.client_quotes, items.size, rub(total)), color = colors.textPrimary, fontSize = 16.sp)
                Text(stringResource(R.string.client_paid, rub(report.totalPaid)), color = colors.textSecondary, fontSize = 14.sp)
                Text(
                    stringResource(R.string.client_debt, rub(report.totalDebt)),
                    color = if (report.totalDebt.signum() > 0) DEBT_RED else colors.textPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold,
                )
            }
        }
        val replaceGuard = rememberReplaceGuard()
        ActionButton(R.string.client_new_quote, R.drawable.ic_add, primary = true, Modifier.fillMaxWidth()) {
            replaceGuard.ask(quoteVm) {
                quoteVm.newQuote()
                quoteVm.applyClient(client ?: com.knit.calculator.core.Client(clientName, "", "", "", ""))
                onNewQuote()
            }
        }
        SectionTitle(R.string.client_history)
        if (items.isEmpty()) Text(stringResource(R.string.client_no_quotes), color = colors.textSecondary, fontSize = 14.sp)
        items.forEach { q ->
            val debt = report.rows.firstOrNull { it.deal.quoteId == q.id }?.remaining ?: BigDecimal.ZERO
            Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Row(Modifier.padding(horizontal = 14.dp, vertical = 10.dp)) {
                    Column(Modifier.weight(1f)) {
                        Text(stringResource(R.string.history_quote_title, q.number, q.date), color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                        Text(q.status.title + if (debt.signum() > 0) " · " + stringResource(R.string.client_row_debt, rub(debt)) else "", color = if (debt.signum() > 0) DEBT_RED else colors.textSecondary, fontSize = 13.sp)
                    }
                    Text(rub(q.total), color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}
