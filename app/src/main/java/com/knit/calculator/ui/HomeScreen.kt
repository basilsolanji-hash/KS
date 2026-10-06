package com.knit.calculator.ui

import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.heightIn
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.semantics.contentDescription
import kotlinx.coroutines.launch
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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
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
    /** Отгрузки МойСклад по месяцам: сумма и штуки; `null` — нет МойСклад. */
    val shipSum: List<java.math.BigDecimal>? = null,
    val shipQty: List<java.math.BigDecimal>? = null,
)

/** Разделы, доступные с главного экрана. */
enum class HomeAction { QUOTE, YARN, CALCULATOR, HISTORY, REPORT, SHOP, SETTINGS, PAYMENTS, PRODUCTION, STOCK, LABELS, ABOUT, PRODUCTS, FINANCE, COST, COMMS, WAREHOUSE, WORKTIME, AI }

/** Пункт меню: раздел, подпись, значок. */
data class MenuItem(val action: HomeAction, val label: Int, val icon: Int)

/** Все разделы для меню ☰ по группам (с учётом роли и МойСклад). */
fun menuGroups(director: Boolean, msEnabled: Boolean): List<Pair<Int, List<MenuItem>>> = listOf(
    R.string.home_group_sales to listOf(
        MenuItem(HomeAction.QUOTE, R.string.menu_quote, R.drawable.ic_add),
        MenuItem(HomeAction.HISTORY, R.string.home_history, R.drawable.ic_history),
        MenuItem(HomeAction.PRODUCTS, R.string.home_products, R.drawable.ic_inventory),
        MenuItem(HomeAction.REPORT, R.string.home_report, R.drawable.ic_report),
        MenuItem(HomeAction.COMMS, R.string.home_comms, R.drawable.ic_chat),
        MenuItem(HomeAction.AI, R.string.home_ai, R.drawable.ic_ai),
        MenuItem(HomeAction.SHOP, R.string.home_shop, R.drawable.ic_web),
    ),
    R.string.home_group_money to listOfNotNull(
        MenuItem(HomeAction.PAYMENTS, R.string.home_payments, R.drawable.ic_payments),
        if (director) MenuItem(HomeAction.FINANCE, R.string.home_finance, R.drawable.ic_report) else null,
        if (director) MenuItem(HomeAction.COST, R.string.home_cost, R.drawable.ic_calculator) else null,
    ),
    R.string.home_group_production to listOfNotNull(
        MenuItem(HomeAction.PRODUCTION, R.string.home_production, R.drawable.ic_factory),
        MenuItem(HomeAction.YARN, R.string.home_yarn, R.drawable.ic_yarn),
        // С МойСклад склад пряжи ведётся там.
        if (!msEnabled) MenuItem(HomeAction.STOCK, R.string.home_stock, R.drawable.ic_inventory) else null,
        if (msEnabled) MenuItem(HomeAction.WAREHOUSE, R.string.home_warehouse, R.drawable.ic_scan) else null,
        MenuItem(HomeAction.LABELS, R.string.home_labels, R.drawable.ic_label),
        MenuItem(HomeAction.CALCULATOR, R.string.home_calculator, R.drawable.ic_calculator),
    ),
    R.string.menu_group_service to listOf(
        MenuItem(HomeAction.WORKTIME, R.string.home_worktime, R.drawable.ic_history),
        MenuItem(HomeAction.SETTINGS, R.string.home_settings, R.drawable.ic_settings),
        MenuItem(HomeAction.ABOUT, R.string.menu_about, R.drawable.ic_doc),
    ),
)

/** Кнопки наверху по умолчанию. */
val DEFAULT_SHORTCUTS = listOf(HomeAction.CALCULATOR, HomeAction.LABELS, HomeAction.SHOP, HomeAction.SETTINGS)
const val MAX_SHORTCUTS = 4

/** Нижняя панель: сотрудник, фото, смена. */
data class WorkBar(
    val person: String,
    val photoPath: String?,
    val photoVersion: Int = 0,
    /** Начало открытой смены сегодня; `null` — смена не идёт. */
    val shiftStart: Long?,
    val workedMinutes: Long,
)

/**
 * Главный экран: ☰ меню со всеми разделами, логотип и «ФАБРИКА», свои кнопки наверху; «Новое КП»,
 * «Сегодня», динамика (директору); внизу — фото сотрудника, дата и часы, рабочее время смены.
 * Потяните вниз — данные обновятся.
 */
@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
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
    refreshing: Boolean = false,
    onRefresh: () -> Unit = {},
    shortcuts: List<HomeAction> = DEFAULT_SHORTCUTS,
    onShortcuts: (List<HomeAction>) -> Unit = {},
    work: WorkBar? = null,
    onShift: (start: Boolean) -> Unit = {},
    onPhoto: (android.net.Uri) -> Unit = {},
    onAction: (HomeAction) -> Unit,
) {
    val colors = LocalKnitColors.current
    val drawer = androidx.compose.material3.rememberDrawerState(androidx.compose.material3.DrawerValue.Closed)
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    var editShortcuts by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(false) }
    val groups = menuGroups(director, sync.msEnabled)
    val all = groups.flatMap { it.second }
    fun go(a: HomeAction) {
        scope.launch { drawer.close() }
        onAction(a)
    }

    androidx.compose.material3.ModalNavigationDrawer(
        drawerState = drawer,
        drawerContent = {
            androidx.compose.material3.ModalDrawerSheet(drawerContainerColor = colors.background) {
                Column(Modifier.fillMaxHeight().verticalScroll(rememberScrollState()).padding(vertical = 12.dp)) {
                    Row(Modifier.padding(horizontal = 20.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
                        Image(painterResource(R.drawable.ic_ks_logo), null, colorFilter = ColorFilter.tint(colors.textPrimary), modifier = Modifier.height(26.dp))
                        Text(stringResource(R.string.app_name), color = colors.textPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold, modifier = Modifier.padding(start = 8.dp))
                    }
                    groups.forEach { (title, items) ->
                        if (items.isEmpty()) return@forEach
                        Text(stringResource(title), color = colors.textSecondary, fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                            modifier = Modifier.padding(start = 28.dp, top = 14.dp, bottom = 4.dp))
                        items.forEach { item ->
                            androidx.compose.material3.NavigationDrawerItem(
                                label = { Text(stringResource(item.label), fontSize = 16.sp) },
                                icon = { Icon(painterResource(item.icon), null, Modifier.size(22.dp)) },
                                selected = false,
                                onClick = { go(item.action) },
                                colors = androidx.compose.material3.NavigationDrawerItemDefaults.colors(
                                    unselectedContainerColor = androidx.compose.ui.graphics.Color.Transparent,
                                    unselectedTextColor = colors.textPrimary,
                                    unselectedIconColor = if (colors.isDark) colors.accent else colors.textPrimary,
                                ),
                                modifier = Modifier.padding(horizontal = 12.dp),
                            )
                        }
                    }
                    Text(stringResource(R.string.menu_group_view), color = colors.textSecondary, fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.padding(start = 28.dp, top = 14.dp, bottom = 4.dp))
                    androidx.compose.material3.NavigationDrawerItem(
                        label = { Text(stringResource(R.string.menu_shortcuts), fontSize = 16.sp) },
                        icon = { Icon(painterResource(R.drawable.ic_edit), null, Modifier.size(22.dp)) },
                        selected = false,
                        onClick = { scope.launch { drawer.close() }; editShortcuts = true },
                        colors = androidx.compose.material3.NavigationDrawerItemDefaults.colors(
                            unselectedContainerColor = androidx.compose.ui.graphics.Color.Transparent,
                            unselectedTextColor = colors.textPrimary, unselectedIconColor = colors.textSecondary,
                        ),
                        modifier = Modifier.padding(horizontal = 12.dp),
                    )
                    androidx.compose.material3.NavigationDrawerItem(
                        label = { Text(stringResource(R.string.menu_theme), fontSize = 16.sp) },
                        icon = {
                            Icon(painterResource(when (themeMode) {
                                ThemeMode.SYSTEM -> R.drawable.ic_theme_auto
                                ThemeMode.LIGHT -> R.drawable.ic_theme_light
                                ThemeMode.DARK -> R.drawable.ic_theme_dark
                            }), null, Modifier.size(22.dp))
                        },
                        selected = false,
                        onClick = onTheme,
                        colors = androidx.compose.material3.NavigationDrawerItemDefaults.colors(
                            unselectedContainerColor = androidx.compose.ui.graphics.Color.Transparent,
                            unselectedTextColor = colors.textPrimary, unselectedIconColor = colors.textSecondary,
                        ),
                        modifier = Modifier.padding(horizontal = 12.dp),
                    )
                    Text(
                        stringResource(if (director) R.string.role_director else R.string.role_manager) + " · " + stringResource(R.string.about_link, version),
                        color = colors.textSecondary, fontSize = 12.sp, modifier = Modifier.padding(start = 28.dp, top = 16.dp),
                    )
                }
            }
        },
    ) {
    Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
    androidx.compose.material3.pulltorefresh.PullToRefreshBox(
        isRefreshing = refreshing,
        onRefresh = onRefresh,
        modifier = Modifier.fillMaxWidth().weight(1f),
    ) {
    Column(
        Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        // Шапка: ☰, логотип и «ФАБРИКА», свои кнопки (настраиваются в меню).
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            HeaderButton(R.drawable.ic_list, stringResource(R.string.menu_open)) { scope.launch { drawer.open() } }
            Image(
                painter = painterResource(R.drawable.ic_ks_logo),
                contentDescription = null,
                colorFilter = ColorFilter.tint(colors.textPrimary),
                modifier = Modifier.padding(start = 4.dp).height(24.dp),
            )
            Text(
                stringResource(R.string.app_name),
                color = colors.textPrimary, fontSize = 17.sp, fontWeight = FontWeight.Bold,
                modifier = Modifier.padding(start = 6.dp).weight(1f).semantics { heading() },
                maxLines = 1, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
            )
            shortcuts.mapNotNull { a -> all.firstOrNull { it.action == a } }.take(MAX_SHORTCUTS).forEach { item ->
                HeaderButton(item.icon, stringResource(item.label)) { onAction(item.action) }
            }
        }
        SyncLine(sync)

        // Главное действие — компактно, на всю ширину.
        Surface(
            onClick = { onAction(HomeAction.QUOTE) },
            shape = RoundedCornerShape(20.dp),
            color = colors.equalsKey,
            contentColor = colors.equalsKeyText,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Row(Modifier.padding(horizontal = 18.dp, vertical = 14.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(painterResource(R.drawable.ic_add), null, Modifier.size(30.dp))
                Column(Modifier.padding(start = 14.dp).weight(1f)) {
                    Text(stringResource(R.string.home_quote), fontSize = 19.sp, fontWeight = FontWeight.Bold)
                    Text(stringResource(R.string.home_quote_hint), fontSize = 13.sp)
                }
                Icon(painterResource(R.drawable.ic_chevron_right), null, Modifier.size(24.dp))
            }
        }
        if (day != null && !day.isEmpty) DayPanel(day, onAction)
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
        // Динамика (директор): продажи, отгрузки, приход и расход.
        if (charts != null) {
            GroupTitle(R.string.home_group_dynamics)
            val dark = colors.isDark
            TrendChart(
                stringResource(R.string.chart_sales), charts.months,
                listOf(stringResource(R.string.chart_sales_series) to charts.sales), listOf(ChartColors.inflow(dark)),
                plan = charts.plan, planLabel = stringResource(R.string.chart_plan),
            )
            if (charts.shipSum != null) {
                var pieces by androidx.compose.runtime.saveable.rememberSaveable { androidx.compose.runtime.mutableStateOf(false) }
                TrendChart(
                    stringResource(R.string.chart_ship), charts.months,
                    listOf(stringResource(R.string.chart_ship_series) to (if (pieces) charts.shipQty.orEmpty() else charts.shipSum)),
                    listOf(ChartColors.outflow(dark)),
                    unit = if (pieces) "шт" else "₽",
                    toggle = listOf("₽", "шт") to (if (pieces) 1 else 0),
                    onToggle = { pieces = it == 1 },
                )
            }
            TrendChart(
                stringResource(R.string.chart_flows), charts.months,
                listOfNotNull(
                    stringResource(R.string.chart_in) to charts.inflow,
                    charts.outflow?.let { stringResource(R.string.chart_out) to it },
                ),
                listOf(ChartColors.inflow(dark), ChartColors.outflow(dark)),
            )
            if (charts.outflow == null) Text(stringResource(R.string.chart_no_out), color = colors.textSecondary, fontSize = 12.sp)
        }

    }
    }
    if (work != null) WorkBarView(work, onShift, onPhoto)
    }
    }
    if (editShortcuts) {
        ShortcutsDialog(all, shortcuts, onDismiss = { editShortcuts = false }) { onShortcuts(it); editShortcuts = false }
    }
}

/** Компактная кнопка шапки (40 dp — помещается больше кнопок). */
@Composable
private fun HeaderButton(icon: Int, description: String, onClick: () -> Unit) {
    val colors = LocalKnitColors.current
    androidx.compose.material3.IconButton(onClick = onClick, modifier = Modifier.size(40.dp)) {
        Icon(painterResource(icon), description, Modifier.size(22.dp), tint = colors.textSecondary)
    }
}

/** Нижняя панель: фото (нажмите, чтобы сменить), имя, смена; справа — время и дата. */
@Composable
private fun WorkBarView(work: WorkBar, onShift: (Boolean) -> Unit, onPhoto: (android.net.Uri) -> Unit) {
    val colors = LocalKnitColors.current
    var now by androidx.compose.runtime.remember { androidx.compose.runtime.mutableLongStateOf(System.currentTimeMillis()) }
    androidx.compose.runtime.LaunchedEffect(Unit) {
        while (true) {
            now = System.currentTimeMillis()
            kotlinx.coroutines.delay(60_000 - now % 60_000)
        }
    }
    val pick = androidx.activity.compose.rememberLauncherForActivityResult(
        androidx.activity.result.contract.ActivityResultContracts.PickVisualMedia(),
    ) { uri -> uri?.let(onPhoto) }
    val photo = androidx.compose.runtime.remember(work.photoPath, work.photoVersion) { com.knit.calculator.quote.PhotoStore.load(work.photoPath)?.asImageBitmap() }
    var confirm by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(false) }
    Surface(color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Row(Modifier.padding(horizontal = 12.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(
                Modifier.size(44.dp).clip(androidx.compose.foundation.shape.CircleShape).background(colors.equalsKey)
                    .clickable { pick.launch(androidx.activity.result.PickVisualMediaRequest(androidx.activity.result.contract.ActivityResultContracts.PickVisualMedia.ImageOnly)) }
                    .semantics { contentDescription = "Фото профиля" },
                contentAlignment = Alignment.Center,
            ) {
                if (photo != null) {
                    Image(photo, null, contentScale = androidx.compose.ui.layout.ContentScale.Crop, modifier = Modifier.fillMaxSize())
                } else {
                    Text(work.person.take(1).uppercase(), color = colors.equalsKeyText, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                }
            }
            Column(Modifier.padding(start = 10.dp).weight(1f)) {
                Text(work.person, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
                val started = work.shiftStart
                Text(
                    if (started != null) stringResource(R.string.shift_line, SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(started)), com.knit.calculator.core.WorkTime.format(work.workedMinutes))
                    else stringResource(R.string.shift_off, com.knit.calculator.core.WorkTime.format(work.workedMinutes)),
                    color = colors.textSecondary, fontSize = 12.sp, maxLines = 1,
                )
            }
            TextButton(onClick = { if (work.shiftStart != null) confirm = true else onShift(true) }) {
                Text(
                    stringResource(if (work.shiftStart != null) R.string.shift_close else R.string.shift_start),
                    color = if (work.shiftStart != null) androidx.compose.ui.graphics.Color(0xFFD9534F) else colors.textPrimary,
                    fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                )
            }
            Column(horizontalAlignment = Alignment.End) {
                Text(SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(now)), color = colors.textPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                Text(SimpleDateFormat("EE, d MMM", Locale("ru")).format(Date(now)), color = colors.textSecondary, fontSize = 12.sp)
            }
        }
    }
    if (confirm) {
        androidx.compose.material3.AlertDialog(
            onDismissRequest = { confirm = false },
            title = { Text(stringResource(R.string.shift_close)) },
            text = { Text(stringResource(R.string.shift_close_confirm, com.knit.calculator.core.WorkTime.format(work.workedMinutes)), color = colors.textPrimary) },
            confirmButton = { TextButton(onClick = { confirm = false; onShift(false) }) { Text(stringResource(R.string.shift_close), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) } },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
}

/** Выбор кнопок наверху: до 4, порядок — стрелкой «выше». */
@Composable
private fun ShortcutsDialog(all: List<MenuItem>, current: List<HomeAction>, onDismiss: () -> Unit, onSave: (List<HomeAction>) -> Unit) {
    val colors = LocalKnitColors.current
    var chosen by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(current.filter { a -> all.any { it.action == a } }) }
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.menu_shortcuts)) },
        text = {
            Column(Modifier.heightIn(max = 480.dp).verticalScroll(rememberScrollState())) {
                Text(stringResource(R.string.shortcuts_hint, MAX_SHORTCUTS), color = colors.textSecondary, fontSize = 13.sp)
                // Сначала выбранные (в их порядке), затем остальные.
                (chosen.mapNotNull { a -> all.firstOrNull { it.action == a } } + all.filter { it.action !in chosen }).forEach { item ->
                    val on = item.action in chosen
                    val index = chosen.indexOf(item.action)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        androidx.compose.material3.Checkbox(
                            checked = on,
                            enabled = on || chosen.size < MAX_SHORTCUTS,
                            onCheckedChange = { v -> chosen = if (v) chosen + item.action else chosen - item.action },
                        )
                        Icon(painterResource(item.icon), null, Modifier.size(20.dp), tint = colors.textSecondary)
                        Text(stringResource(item.label), color = colors.textPrimary, fontSize = 15.sp, modifier = Modifier.padding(start = 8.dp).weight(1f))
                        if (index > 0) {
                            KnitIconButton(R.drawable.ic_arrow_up, stringResource(R.string.comms_up), {
                                chosen = chosen.toMutableList().apply { add(index - 1, removeAt(index)) }
                            })
                        }
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = { onSave(chosen) }) { Text(stringResource(R.string.shortcuts_save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) } },
        dismissButton = { TextButton(onClick = { onSave(DEFAULT_SHORTCUTS) }) { Text(stringResource(R.string.shortcuts_default), color = colors.textSecondary) } },
        containerColor = colors.panel,
    )
}

@Composable
private fun GroupTitle(title: Int) {
    Text(
        stringResource(title), color = LocalKnitColors.current.textSecondary, fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
        modifier = Modifier.padding(top = 4.dp),
    )
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
