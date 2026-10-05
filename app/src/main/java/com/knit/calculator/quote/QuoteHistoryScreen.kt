package com.knit.calculator.quote

import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.QuoteStatus
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.theme.LocalKnitColors
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

/**
 * История КП: из Google Таблицы (все телефоны) или из архива этого телефона.
 * Статус меняется прямо в списке; «Открыть» — изменить и отправить снова, «Повторить» — новое КП по образцу.
 */
@Composable
fun QuoteHistoryScreen(
    viewModel: QuoteViewModel,
    opsViewModel: OpsViewModel,
    onBack: () -> Unit,
    onOpenProduction: () -> Unit,
    onOpened: () -> Unit,
    onOpenLabels: (HistoryItem) -> Unit = {},
) {
    val history by viewModel.history.collectAsStateWithLifecycle()
    val error by viewModel.historyError.collectAsStateWithLifecycle()
    val sync by viewModel.sync.collectAsStateWithLifecycle()
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val openError = stringResource(R.string.history_open_error)
    val repeated = stringResource(R.string.history_repeated)

    var request by remember { mutableStateOf<Pair<HistoryItem, DealAction>?>(null) }
    // «Отказ» и «Оплачено» — только после подтверждения (уходят и в МойСклад).
    var pendingStatus by remember { mutableStateOf<Pair<HistoryItem, QuoteStatus>?>(null) }
    fun applyStatus(q: HistoryItem, status: QuoteStatus) {
        scope.launch(Dispatchers.Main) {
            viewModel.setStatus(q, status)?.let {
                Toast.makeText(context, context.getString(R.string.status_error, it), Toast.LENGTH_LONG).show()
            }
        }
    }

    BackHandler(onBack = onBack)
    LaunchedEffect(Unit) {
        viewModel.loadHistory()
        opsViewModel.load()
    }

    Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
        ScreenTopBar(stringResource(R.string.history_quotes), onBack) {
            KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.sync_refresh), viewModel::loadHistory)
        }
        if (!sync.connected) Message(stringResource(R.string.history_local_note))
        error?.let { Message(stringResource(R.string.history_error, it)) }
        val list = history
        when {
            list == null -> Message(stringResource(R.string.sync_loading))
            list.isEmpty() -> Message(stringResource(R.string.history_quotes_empty))
            else -> LazyColumn(
                Modifier.fillMaxSize().padding(horizontal = 16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                items(list, key = { it.id.ifBlank { it.number.toString() } }) { q ->
                    HistoryCard(
                        item = q,
                        onOpen = { if (viewModel.openQuote(q)) onOpened() else Toast.makeText(context, openError, Toast.LENGTH_LONG).show() },
                        onRepeat = {
                            if (viewModel.repeatQuote(q)) {
                                Toast.makeText(context, repeated, Toast.LENGTH_SHORT).show()
                                onOpened()
                            } else {
                                Toast.makeText(context, openError, Toast.LENGTH_LONG).show()
                            }
                        },
                        onDeal = { action -> if (action == DealAction.LABELS) onOpenLabels(q) else request = q to action },
                        onStatus = { status ->
                            if (status == QuoteStatus.REJECTED || status == QuoteStatus.PAID) pendingStatus = q to status
                            else applyStatus(q, status)
                        },
                    )
                }
            }
        }
    }
    DealActionHost(request, viewModel, opsViewModel, onDismiss = { request = null }, onOpenProduction = onOpenProduction)
    pendingStatus?.let { (q, status) ->
        ConfirmDialog(
            title = stringResource(R.string.status_confirm_title),
            text = stringResource(R.string.status_confirm_text, q.number, status.title),
            confirm = stringResource(R.string.status_confirm_ok),
            onConfirm = { pendingStatus = null; applyStatus(q, status) },
            onDismiss = { pendingStatus = null },
        )
    }
}

@Composable
private fun HistoryCard(
    item: HistoryItem,
    onOpen: () -> Unit,
    onRepeat: () -> Unit,
    onDeal: (DealAction) -> Unit,
    onStatus: (QuoteStatus) -> Unit,
) {
    val colors = LocalKnitColors.current
    var menu by remember { mutableStateOf(false) }
    var dealMenu by remember { mutableStateOf(false) }
    Surface(shape = RoundedCornerShape(18.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(stringResource(R.string.history_quote_title, item.number, item.date), color = colors.textPrimary, fontWeight = FontWeight.SemiBold, fontSize = 16.sp)
                    if (item.client.isNotBlank()) Text(item.client, color = colors.textSecondary, fontSize = 14.sp)
                    if (item.author.isNotBlank()) Text(item.author, color = colors.textSecondary, fontSize = 12.sp)
                }
                Text(
                    stringResource(R.string.quote_money, QuoteCalculator.formatMoney(item.total)),
                    color = if (colors.isDark) colors.accent else colors.textPrimary,
                    fontWeight = FontWeight.Bold,
                    fontSize = 16.sp,
                    modifier = Modifier.padding(end = 8.dp),
                )
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box {
                    Surface(
                        onClick = { menu = true },
                        shape = RoundedCornerShape(12.dp),
                        color = if (item.status.isWon) colors.equalsKey else colors.background,
                        contentColor = if (item.status.isWon) colors.equalsKeyText else colors.textPrimary,
                    ) {
                        Text("${stringResource(R.string.status_change)}: ${item.status.title} ▾", fontSize = 14.sp, modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp))
                    }
                    DropdownMenu(expanded = menu, onDismissRequest = { menu = false }, containerColor = colors.panel) {
                        QuoteStatus.entries.forEach { s ->
                            DropdownMenuItem(
                                text = { Text(s.title, color = colors.textPrimary, fontWeight = if (s == item.status) FontWeight.Bold else FontWeight.Normal) },
                                onClick = { menu = false; if (s != item.status) onStatus(s) },
                            )
                        }
                    }
                }
            }
            // Действия — отдельной строкой: на узком экране всё не помещается рядом со статусом.
            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(onClick = onOpen) { Text(stringResource(R.string.history_open_action), color = colors.textPrimary) }
                TextButton(onClick = onRepeat) { Text(stringResource(R.string.history_repeat_action), color = colors.textPrimary) }
                androidx.compose.foundation.layout.Spacer(Modifier.weight(1f))
                Box {
                    KnitIconButton(R.drawable.ic_more, stringResource(R.string.deal_menu), { dealMenu = true })
                    DropdownMenu(expanded = dealMenu, onDismissRequest = { dealMenu = false }, containerColor = colors.panel) {
                        listOf(
                            DealAction.INVOICE to R.string.deal_invoice,
                            DealAction.CONTRACT to R.string.deal_contract,
                            DealAction.ORDER to R.string.deal_order,
                            DealAction.PAYMENT to R.string.deal_payment,
                            DealAction.LABELS to R.string.deal_labels,
                        ).forEach { (action, label) ->
                            DropdownMenuItem(
                                text = { Text(stringResource(label), color = colors.textPrimary) },
                                onClick = { dealMenu = false; onDeal(action) },
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun Message(text: String) {
    Text(text, color = LocalKnitColors.current.textSecondary, fontSize = 14.sp, modifier = Modifier.padding(horizontal = 24.dp, vertical = 8.dp))
}
