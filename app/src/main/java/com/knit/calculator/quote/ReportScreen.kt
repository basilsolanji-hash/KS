package com.knit.calculator.quote

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.foundation.layout.Spacer
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.ReportRow
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Locale

/** Отчёт за месяц: КП, согласования, конверсия, прибыль, по менеджерам и изделиям. */
@Composable
fun ReportScreen(viewModel: QuoteViewModel, onBack: () -> Unit) {
    val history by viewModel.history.collectAsStateWithLifecycle()
    val error by viewModel.historyError.collectAsStateWithLifecycle()
    val director by viewModel.director.collectAsStateWithLifecycle()
    val colors = LocalKnitColors.current
    var offset by rememberSaveable { mutableStateOf(0) }
    val month = Calendar.getInstance().apply { add(Calendar.MONTH, offset) }
    val key = SimpleDateFormat("yyyy-MM", Locale.US).format(month.time)
    val title = SimpleDateFormat("LLLL yyyy", Locale("ru")).format(month.time).replaceFirstChar { it.uppercase() }

    BackHandler(onBack = onBack)
    LaunchedEffect(Unit) { viewModel.loadHistory() }
    val report = remember(history, key) { viewModel.report(key) }

    Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
        ScreenTopBar(stringResource(R.string.report_title), onBack)
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            KnitIconButton(R.drawable.ic_chevron_left, stringResource(R.string.report_prev), { offset-- })
            Text(title, color = colors.textPrimary, fontSize = 18.sp, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center, modifier = Modifier.weight(1f))
            KnitIconButton(R.drawable.ic_chevron_right, stringResource(R.string.report_next), { offset++ }, enabled = offset < 0)
        }
        Column(
            Modifier.fillMaxWidth().weight(1f).verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            error?.let { Text(stringResource(R.string.history_error, it), color = colors.textPrimary, fontSize = 14.sp) }
            val r = report
            if (r == null) {
                Text(stringResource(R.string.sync_loading), color = colors.textSecondary)
            } else if (r.count == 0) {
                Text(stringResource(R.string.report_empty), color = colors.textSecondary, fontSize = 15.sp)
            } else {
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Stat(stringResource(R.string.report_quotes), "${r.count}", money(r.sum), Modifier.weight(1f))
                    Stat(stringResource(R.string.report_won), "${r.wonCount}", money(r.wonSum), Modifier.weight(1f))
                }
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    Stat(stringResource(R.string.report_conversion), "${YarnCalculator.formatCompact(r.conversionPercent, 1)} %", null, Modifier.weight(1f))
                    if (director) Stat(stringResource(R.string.report_profit), money(r.profit), null, Modifier.weight(1f))
                    else Spacer(Modifier.weight(1f))
                }
                Rows(stringResource(R.string.report_by_manager), r.byManager)
                Rows(stringResource(R.string.report_by_product), r.byProduct)
            }
            Text(stringResource(R.string.report_sheet_hint), color = colors.textSecondary, fontSize = 13.sp)
        }
    }
}

private fun money(v: java.math.BigDecimal) = QuoteCalculator.formatMoney(v) + " ₽"

@Composable
private fun Stat(label: String, value: String, sub: String?, modifier: Modifier) {
    val colors = LocalKnitColors.current
    Surface(shape = RoundedCornerShape(18.dp), color = colors.panel, modifier = modifier) {
        Column(Modifier.padding(14.dp)) {
            Text(label, color = colors.textSecondary, fontSize = 13.sp)
            Text(value, color = if (colors.isDark) colors.accent else colors.textPrimary, fontSize = 22.sp, fontWeight = FontWeight.Bold)
            sub?.let { Text(it, color = colors.textSecondary, fontSize = 13.sp) }
        }
    }
}

@Composable
private fun Rows(title: String, rows: List<ReportRow>) {
    val colors = LocalKnitColors.current
    if (rows.isEmpty()) return
    Surface(shape = RoundedCornerShape(18.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(title, color = colors.textSecondary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
            rows.forEach { row ->
                Column {
                    Text(row.name, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Medium)
                    Text(
                        stringResource(R.string.report_row, row.count, QuoteCalculator.formatMoney(row.sum), QuoteCalculator.formatMoney(row.wonSum)),
                        color = colors.textSecondary,
                        fontSize = 13.sp,
                    )
                }
            }
        }
    }
}
