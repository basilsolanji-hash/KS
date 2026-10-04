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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.components.toastIfNeeded
import com.knit.calculator.ui.theme.LocalKnitColors

/** Пряжа на весь заказ из текущего КП: по нитям, в кг, с браком и стоимостью. */
@Composable
fun OrderYarnScreen(viewModel: QuoteViewModel, onBack: () -> Unit) {
    val draft by viewModel.draft.collectAsStateWithLifecycle()
    val catalog by viewModel.catalog.collectAsStateWithLifecycle()
    val settings by viewModel.settings.collectAsStateWithLifecycle()
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val clipboard = LocalClipboardManager.current
    val copied = stringResource(R.string.order_yarn_copied)
    val result = remember(draft, catalog, settings) { viewModel.orderYarn(viewModel.lineViews(draft, catalog, settings)) }
    val waste = YarnCalculator.formatCompact(settings.yarnWaste, 2)

    BackHandler(onBack = onBack)
    Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
        ScreenTopBar(stringResource(R.string.order_yarn_title), onBack)
        Column(
            Modifier.fillMaxWidth().weight(1f).verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Text(stringResource(R.string.order_yarn_hint, waste), color = colors.textSecondary, fontSize = 14.sp)
            if (result.needs.isEmpty() && result.missingWeight.isEmpty()) {
                Text(stringResource(R.string.order_yarn_empty), color = colors.textSecondary)
            }
            result.needs.forEach { n ->
                Surface(shape = RoundedCornerShape(18.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                    Row(Modifier.padding(14.dp)) {
                        Column(Modifier.weight(1f)) {
                            Text(n.yarn, color = colors.textPrimary, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
                            Text(
                                stringResource(R.string.order_yarn_line, YarnCalculator.format(n.netKg, 3), YarnCalculator.format(n.totalKg, 3)),
                                color = colors.textSecondary,
                                fontSize = 14.sp,
                            )
                        }
                        n.cost?.let { Text(stringResource(R.string.order_yarn_cost, QuoteCalculator.formatMoney(it)), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
                    }
                }
            }
            if (result.needs.isNotEmpty()) {
                Text(
                    stringResource(R.string.order_yarn_total, YarnCalculator.format(result.totalKg, 3)),
                    color = if (colors.isDark) colors.accent else colors.textPrimary,
                    fontSize = 24.sp,
                    fontWeight = FontWeight.Bold,
                )
                result.totalCost?.let {
                    Text(stringResource(R.string.order_yarn_total_cost, QuoteCalculator.formatMoney(it)), color = colors.textPrimary, fontSize = 16.sp)
                }
            }
            if (result.missingWeight.isNotEmpty()) {
                Text(stringResource(R.string.order_yarn_missing, result.missingWeight.joinToString("; ")), color = colors.textPrimary, fontSize = 14.sp)
            }
            if (result.needs.isNotEmpty()) {
                ActionButton(R.string.yarn_copy, R.drawable.ic_copy, primary = true, Modifier.fillMaxWidth()) {
                    val text = buildString {
                        appendLine(context.getString(R.string.order_yarn_title))
                        result.needs.forEach { n ->
                            appendLine("${n.yarn}: ${YarnCalculator.format(n.totalKg, 3)} кг" + (n.cost?.let { " · ${QuoteCalculator.formatMoney(it)} ₽" } ?: ""))
                        }
                        appendLine(context.getString(R.string.order_yarn_total, YarnCalculator.format(result.totalKg, 3)))
                    }
                    clipboard.setText(AnnotatedString(text))
                    toastIfNeeded(context, copied)
                }
            }
        }
    }
}
