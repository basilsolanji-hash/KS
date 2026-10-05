package com.knit.calculator.ui

import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.R
import com.knit.calculator.core.DaySummary
import com.knit.calculator.core.QuoteCalculator
import androidx.compose.foundation.clickable
import androidx.compose.material3.TextButton
import com.knit.calculator.data.ThemeMode
import com.knit.calculator.quote.SyncStatus
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Графики главного экрана (директор): последние 6 месяцев. */
data class HomeCharts(
    val months: List<String>,
    val sales: List<java.math.BigDecimal>,
    val plan: java.math.BigDecimal?,
    val inflow: List<java.math.BigDecimal>,
    /** Расход — только с МойСклад (исходящие платежи); `null` — данных нет. */
    val outflow: List<java.math.BigDecimal>?,
)

/** Разделы, доступные с главного экрана. */
enum class HomeAction { QUOTE, YARN, CALCULATOR, HISTORY, REPORT, SHOP, SETTINGS, PAYMENTS, PRODUCTION, STOCK, LABELS, ABOUT, PRODUCTS, FINANCE }

/** Главный экран: логотип и крупные кнопки разделов. */
@Composable
fun HomeScreen(
    sync: SyncStatus,
    themeMode: ThemeMode,
    director: Boolean,
    onTheme: () -> Unit,
    day: DaySummary? = null,
    version: String = "",
    /** Заработок менеджера в этом месяце: (итого, оплаты, процент). */
    myPay: Triple<java.math.BigDecimal, java.math.BigDecimal, java.math.BigDecimal>? = null,
    charts: HomeCharts? = null,
    onAction: (HomeAction) -> Unit,
) {
    val colors = LocalKnitColors.current
    Column(
        Modifier
            .fillMaxSize()
            .background(colors.background)
            .safeDrawingPadding()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        // Редкие разделы — маленькими значками вверху.
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            KnitIconButton(R.drawable.ic_calculator, stringResource(R.string.home_calculator), { onAction(HomeAction.CALCULATOR) })
            KnitIconButton(R.drawable.ic_label, stringResource(R.string.home_labels), { onAction(HomeAction.LABELS) })
            KnitIconButton(R.drawable.ic_web, stringResource(R.string.home_shop), { onAction(HomeAction.SHOP) })
            KnitIconButton(R.drawable.ic_settings, stringResource(R.string.home_settings), { onAction(HomeAction.SETTINGS) })
            Spacer(Modifier.weight(1f))
            KnitIconButton(
                when (themeMode) {
                    ThemeMode.SYSTEM -> R.drawable.ic_theme_auto
                    ThemeMode.LIGHT -> R.drawable.ic_theme_light
                    ThemeMode.DARK -> R.drawable.ic_theme_dark
                },
                stringResource(R.string.theme_toggle, ""),
                onTheme,
            )
        }
        Image(
            painter = painterResource(R.drawable.ic_ks_logo),
            contentDescription = null,
            colorFilter = ColorFilter.tint(colors.textPrimary),
            modifier = Modifier.height(72.dp).align(Alignment.CenterHorizontally),
        )
        Text(
            stringResource(R.string.app_name),
            color = colors.textPrimary,
            fontSize = 26.sp,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center,
            modifier = Modifier.fillMaxWidth().semantics { heading() },
        )
        Spacer(Modifier.height(4.dp))

        Surface(
            onClick = { onAction(HomeAction.QUOTE) },
            shape = RoundedCornerShape(24.dp),
            color = colors.equalsKey,
            contentColor = colors.equalsKeyText,
            modifier = Modifier.fillMaxWidth().height(120.dp),
        ) {
            Row(Modifier.padding(20.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(painterResource(R.drawable.ic_quote), null, Modifier.size(44.dp))
                Column(Modifier.padding(start = 16.dp)) {
                    Text(stringResource(R.string.home_quote), fontSize = 22.sp, fontWeight = FontWeight.Bold)
                    Text(stringResource(R.string.home_quote_hint), fontSize = 14.sp)
                }
            }
        }
        if (day != null && !day.isEmpty) DayPanel(day, onAction)
        // Директору: динамика продаж и приход/расход по месяцам.
        if (charts != null) {
            val dark = colors.isDark
            MonthBars(
                stringResource(R.string.chart_sales), charts.months,
                listOf(stringResource(R.string.chart_sales_series) to charts.sales), listOf(ChartColors.inflow(dark)),
                plan = charts.plan, planLabel = stringResource(R.string.chart_plan),
            )
            MonthBars(
                stringResource(R.string.chart_flows), charts.months,
                listOfNotNull(
                    stringResource(R.string.chart_in) to charts.inflow,
                    charts.outflow?.let { stringResource(R.string.chart_out) to it },
                ),
                listOf(ChartColors.inflow(dark), ChartColors.outflow(dark)),
            )
            if (charts.outflow == null) Text(stringResource(R.string.chart_no_out), color = colors.textSecondary, fontSize = 12.sp)
        }
        if (myPay != null) {
            Surface(shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(horizontal = 16.dp, vertical = 10.dp)) {
                    Text(stringResource(R.string.salary_mine), color = colors.textSecondary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                    Text(QuoteCalculator.formatMoney(myPay.first) + " ₽", color = colors.textPrimary, fontSize = 22.sp, fontWeight = FontWeight.Bold)
                    Text(
                        stringResource(R.string.salary_mine_line, QuoteCalculator.formatMoney(myPay.second), QuoteCalculator.formatMoney(myPay.third)),
                        color = colors.textSecondary, fontSize = 13.sp,
                    )
                }
            }
        }
        val tiles = listOfNotNull(
            // Финансы — только директору.
            if (director) Triple(HomeAction.FINANCE, R.string.home_finance, R.drawable.ic_payments) else null,
            Triple(HomeAction.HISTORY, R.string.home_history, R.drawable.ic_history),
            Triple(HomeAction.PAYMENTS, R.string.home_payments, R.drawable.ic_payments),
            Triple(HomeAction.PRODUCTION, R.string.home_production, R.drawable.ic_factory),
            Triple(HomeAction.PRODUCTS, R.string.home_products, R.drawable.ic_inventory),
            Triple(HomeAction.STOCK, R.string.home_stock, R.drawable.ic_inventory),
            Triple(HomeAction.YARN, R.string.home_yarn, R.drawable.ic_yarn),
            Triple(HomeAction.REPORT, R.string.home_report, R.drawable.ic_report),
        ).filter { (action, _, _) -> !(action == HomeAction.STOCK && sync.msEnabled) } // с МойСклад склад — там
        tiles.chunked(2).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                row.forEach { (action, label, icon) ->
                    Tile(label, icon, Modifier.weight(1f)) { onAction(action) }
                }
                if (row.size == 1) Spacer(Modifier.weight(1f))
            }
        }
        SyncLine(sync)
        Text(
            stringResource(if (director) R.string.role_director else R.string.role_manager),
            color = colors.textSecondary, fontSize = 13.sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth(),
        )
        TextButton(onClick = { onAction(HomeAction.ABOUT) }, modifier = Modifier.align(Alignment.CenterHorizontally)) {
            Text(stringResource(R.string.about_link, version), color = colors.textSecondary, fontSize = 13.sp)
        }
    }
}

/** «Сегодня»: долги клиентов, просроченные заказы, КП без ответа — нажатие открывает раздел. */
@Composable
private fun DayPanel(day: DaySummary, onAction: (HomeAction) -> Unit) {
    val colors = LocalKnitColors.current
    val red = androidx.compose.ui.graphics.Color(0xFFD32F2F)
    Surface(shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 10.dp)) {
            Text(stringResource(R.string.day_title), color = colors.textSecondary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
            if (day.debt.signum() > 0) {
                DayRow(
                    stringResource(R.string.day_debt, QuoteCalculator.formatMoney(day.debt), day.debtors), red,
                ) { onAction(HomeAction.PAYMENTS) }
            }
            if (day.overdueOrders > 0) DayRow(stringResource(R.string.day_overdue, day.overdueOrders), red) { onAction(HomeAction.PRODUCTION) }
            if (day.waitingQuotes > 0) DayRow(stringResource(R.string.day_waiting, day.waitingQuotes), colors.textPrimary) { onAction(HomeAction.HISTORY) }
        }
    }
}

@Composable
private fun DayRow(text: String, color: androidx.compose.ui.graphics.Color, onClick: () -> Unit) {
    Text(
        text, color = color, fontSize = 16.sp, fontWeight = FontWeight.SemiBold,
        modifier = Modifier.fillMaxWidth().clickable(onClick = onClick).padding(vertical = 6.dp),
    )
}

@Composable
private fun Tile(@StringRes label: Int, @DrawableRes icon: Int, modifier: Modifier, onClick: () -> Unit) {
    val colors = LocalKnitColors.current
    Surface(onClick = onClick, shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = modifier.height(104.dp)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.SpaceBetween) {
            Icon(painterResource(icon), null, Modifier.size(30.dp), tint = if (colors.isDark) colors.accent else colors.textPrimary)
            Text(stringResource(label), color = colors.textPrimary, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
        }
    }
}

@Composable
private fun SyncLine(sync: SyncStatus) {
    val colors = LocalKnitColors.current
    val time = sync.lastSync?.let { SimpleDateFormat("dd.MM HH:mm", Locale.getDefault()).format(Date(it)) } ?: "—"
    val text = when {
        !sync.connected -> stringResource(R.string.home_sync_off)
        sync.error != null -> stringResource(R.string.home_sync_error, time)
        else -> stringResource(R.string.home_sync_on, time)
    }
    Text(text, color = colors.textSecondary, fontSize = 13.sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 4.dp))
}
