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

/** Данные графиков главного экрана (директор): точки «время → сумма», периоды считаются на телефоне. */
data class HomeCharts(
    val sales: List<Pair<Long, java.math.BigDecimal>>,
    /** План продаж в месяц (линия — на графике «По месяцам»). */
    val plan: java.math.BigDecimal?,
    val inflow: List<Pair<Long, java.math.BigDecimal>>,
    /** Расход — только с МойСклад (исходящие платежи); `null` — данных нет. */
    val outflow: List<Pair<Long, java.math.BigDecimal>>?,
    /** Отгрузки МойСклад (время, ₽, шт) по точности; `null` — нет МойСклад. */
    val ship: Map<com.knit.calculator.core.Grain, List<Triple<Long, java.math.BigDecimal, java.math.BigDecimal>>>? = null,
    /** КП для топов клиентов и товаров за период. */
    val deals: List<com.knit.calculator.core.Sale> = emptyList(),
    /** Расходы со статьями МойСклад. */
    val expenses: List<com.knit.calculator.core.Expense> = emptyList(),
)

/** Блоки главного экрана; [periodic] — зависят от периода «Сегодня … По годам». */
enum class HomeWidget(val title: Int, val directorOnly: Boolean = false, val periodic: Boolean = false) {
    DAY(R.string.day_title),
    MYPAY(R.string.salary_mine),
    SALES(R.string.chart_sales, directorOnly = true, periodic = true),
    SHIP(R.string.chart_ship, directorOnly = true, periodic = true),
    FLOWS(R.string.chart_flows, directorOnly = true, periodic = true),
    EXPENSES(R.string.widget_expenses, directorOnly = true, periodic = true),
    CLIENTS(R.string.widget_clients, directorOnly = true, periodic = true),
    PRODUCTS(R.string.widget_products, directorOnly = true, periodic = true),
}

val DEFAULT_WIDGETS = HomeWidget.entries.toList()

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
    widgets: List<HomeWidget> = DEFAULT_WIDGETS,
    onWidgets: (List<HomeWidget>) -> Unit = {},
    onAction: (HomeAction) -> Unit,
) {
    val colors = LocalKnitColors.current
    val drawer = androidx.compose.material3.rememberDrawerState(androidx.compose.material3.DrawerValue.Closed)
    val scope = androidx.compose.runtime.rememberCoroutineScope()
    var editShortcuts by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(false) }
    var editWidgets by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(false) }
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
                        label = { Text(stringResource(R.string.menu_widgets), fontSize = 16.sp) },
                        icon = { Icon(painterResource(R.drawable.ic_report), null, Modifier.size(22.dp)) },
                        selected = false,
                        onClick = { scope.launch { drawer.close() }; editWidgets = true },
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
    Box(Modifier.fillMaxWidth().weight(1f)) {
    androidx.compose.material3.pulltorefresh.PullToRefreshBox(
        isRefreshing = refreshing,
        onRefresh = onRefresh,
        modifier = Modifier.fillMaxSize(),
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

        // Блоки главного экрана — в выбранном порядке (меню → «Блоки главного экрана»).
        val visible = widgets.filter { w -> (!w.directorOnly || director) && !(w == HomeWidget.MYPAY && director) }
        val periodic = visible.any { it.periodic } && charts != null
        var periodName by androidx.compose.runtime.saveable.rememberSaveable { androidx.compose.runtime.mutableStateOf(com.knit.calculator.core.DynPeriod.MONTH.name) }
        val period = com.knit.calculator.core.DynPeriod.valueOf(periodName)
        val now = androidx.compose.runtime.remember(period, charts) { System.currentTimeMillis() }
        val dark = colors.isDark
        if (periodic) {
            GroupTitle(R.string.home_group_dynamics)
            androidx.compose.foundation.lazy.LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                items(com.knit.calculator.core.DynPeriod.entries.size) { k ->
                    val p = com.knit.calculator.core.DynPeriod.entries[k]
                    androidx.compose.material3.FilterChip(
                        selected = p == period, onClick = { periodName = p.name }, label = { Text(p.title) },
                        colors = androidx.compose.material3.FilterChipDefaults.filterChipColors(
                            selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary,
                        ),
                    )
                }
            }
        }
        visible.forEach { w ->
            when (w) {
                HomeWidget.DAY -> if (day != null && !day.isEmpty) DayPanel(day, onAction)
                HomeWidget.MYPAY -> if (myPay != null) {
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
                HomeWidget.SALES -> if (charts != null) {
                    val sales = androidx.compose.runtime.remember(period, charts) { com.knit.calculator.core.Dynamics.series(charts.sales, period, now) }
                    TrendChart(
                        stringResource(R.string.chart_sales), sales.labels,
                        listOf(stringResource(R.string.chart_sales_series) to sales.current), listOf(ChartColors.inflow(dark)),
                        previous = listOf(sales.previous),
                        plan = if (period == com.knit.calculator.core.DynPeriod.MONTHS) charts.plan else null, planLabel = stringResource(R.string.chart_plan),
                    )
                }
                HomeWidget.SHIP -> charts?.ship?.let { ship ->
                    var pieces by androidx.compose.runtime.saveable.rememberSaveable { androidx.compose.runtime.mutableStateOf(false) }
                    val points = ship[com.knit.calculator.core.Dynamics.grain(period)].orEmpty().ifEmpty { ship[com.knit.calculator.core.Grain.MONTH].orEmpty() }
                    val shipSeries = androidx.compose.runtime.remember(period, charts, pieces) {
                        com.knit.calculator.core.Dynamics.series(points.map { it.first to (if (pieces) it.third else it.second) }, period, now)
                    }
                    TrendChart(
                        stringResource(R.string.chart_ship), shipSeries.labels,
                        listOf(stringResource(R.string.chart_ship_series) to shipSeries.current),
                        listOf(ChartColors.outflow(dark)),
                        previous = listOf(shipSeries.previous),
                        unit = if (pieces) "шт" else "₽",
                        toggle = listOf("₽", "шт") to (if (pieces) 1 else 0),
                        onToggle = { pieces = it == 1 },
                    )
                }
                HomeWidget.FLOWS -> if (charts != null) {
                    val inflow = androidx.compose.runtime.remember(period, charts) { com.knit.calculator.core.Dynamics.series(charts.inflow, period, now) }
                    val outflow = androidx.compose.runtime.remember(period, charts) { charts.outflow?.let { com.knit.calculator.core.Dynamics.series(it, period, now) } }
                    TrendChart(
                        stringResource(R.string.chart_flows), inflow.labels,
                        listOfNotNull(
                            stringResource(R.string.chart_in) to inflow.current,
                            outflow?.let { stringResource(R.string.chart_out) to it.current },
                        ),
                        listOf(ChartColors.inflow(dark), ChartColors.outflow(dark)),
                        previous = listOf(inflow.previous, outflow?.previous),
                    )
                    if (charts.outflow == null) Text(stringResource(R.string.chart_no_out), color = colors.textSecondary, fontSize = 12.sp)
                }
                HomeWidget.EXPENSES -> if (charts != null && charts.expenses.isNotEmpty()) {
                    val (from, to) = com.knit.calculator.core.Dynamics.range(period, now)
                    val rows = androidx.compose.runtime.remember(period, charts) {
                        charts.expenses.filter { it.date in from until to }.groupBy { it.category.ifBlank { "Без статьи" } }
                            .map { (k, xs) -> k to xs.fold(java.math.BigDecimal.ZERO) { acc, x -> acc + x.amount } }.sortedByDescending { it.second }
                    }
                    TopList(stringResource(R.string.widget_expenses), rows, ChartColors.outflow(dark)) { onAction(HomeAction.FINANCE) }
                }
                HomeWidget.CLIENTS, HomeWidget.PRODUCTS -> if (charts != null && charts.deals.isNotEmpty()) {
                    val (from, to) = com.knit.calculator.core.Dynamics.range(period, now)
                    val rows = androidx.compose.runtime.remember(period, charts, w) {
                        val inPeriod = charts.deals.filter { it.date in from until to }
                        (if (w == HomeWidget.CLIENTS) com.knit.calculator.core.DirectorReport.clients(inPeriod) else com.knit.calculator.core.DirectorReport.products(inPeriod))
                            .map { it.name to it.value }
                    }
                    TopList(stringResource(if (w == HomeWidget.CLIENTS) R.string.widget_clients else R.string.widget_products), rows, ChartColors.inflow(dark)) {
                        onAction(HomeAction.FINANCE)
                    }
                }
            }
        }
        // Место под круглую кнопку «+».
        Spacer(Modifier.height(72.dp))

    }
    }
    // Главное действие — большая круглая кнопка «+»: новое КП.
    androidx.compose.material3.LargeFloatingActionButton(
        onClick = { onAction(HomeAction.QUOTE) },
        shape = androidx.compose.foundation.shape.CircleShape,
        containerColor = colors.equalsKey,
        contentColor = colors.equalsKeyText,
        modifier = Modifier.align(Alignment.BottomCenter).padding(bottom = 12.dp),
    ) {
        Icon(painterResource(R.drawable.ic_add), stringResource(R.string.menu_quote), Modifier.size(40.dp))
    }
    }
    if (work != null) WorkBarView(work, onShift, onPhoto)
    }
    }
    if (editWidgets) {
        WidgetsDialog(director, widgets, onDismiss = { editWidgets = false }) { onWidgets(it); editWidgets = false }
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

/** Топ-5 за период: название, сумма и полоса доли; нажатие — подробный отчёт. */
@Composable
private fun TopList(title: String, rows: List<Pair<String, java.math.BigDecimal>>, color: androidx.compose.ui.graphics.Color, onOpen: () -> Unit) {
    val colors = LocalKnitColors.current
    val total = rows.fold(java.math.BigDecimal.ZERO) { a, r -> a + r.second }
    Surface(onClick = onOpen, shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(title, color = colors.textSecondary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                Text(QuoteCalculator.formatMoney(total) + " ₽", color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Bold)
            }
            if (rows.isEmpty()) Text(stringResource(R.string.widget_empty), color = colors.textSecondary, fontSize = 13.sp)
            rows.take(5).forEach { (name, v) ->
                val share = if (total.signum() == 0) 0f else (v.toFloat() / total.toFloat())
                Column {
                    Row {
                        Text(name, color = colors.textPrimary, fontSize = 14.sp, maxLines = 1, overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                        Text(QuoteCalculator.formatMoney(v) + " ₽ · " + (share * 100).toInt() + "%", color = colors.textSecondary, fontSize = 13.sp)
                    }
                    androidx.compose.foundation.Canvas(Modifier.fillMaxWidth().padding(top = 3.dp).height(6.dp)) {
                        val r = androidx.compose.ui.geometry.CornerRadius(3.dp.toPx())
                        drawRoundRect(color.copy(alpha = 0.18f), cornerRadius = r)
                        drawRoundRect(color, size = size.copy(width = size.width * share.coerceIn(0.01f, 1f)), cornerRadius = r)
                    }
                }
            }
        }
    }
}

/** Блоки главного экрана: какие показывать и в каком порядке. */
@Composable
private fun WidgetsDialog(director: Boolean, current: List<HomeWidget>, onDismiss: () -> Unit, onSave: (List<HomeWidget>) -> Unit) {
    val colors = LocalKnitColors.current
    val available = HomeWidget.entries.filter { (!it.directorOnly || director) && !(it == HomeWidget.MYPAY && director) }
    var chosen by androidx.compose.runtime.remember { androidx.compose.runtime.mutableStateOf(current.filter { it in available }) }
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.menu_widgets)) },
        text = {
            Column(Modifier.heightIn(max = 480.dp).verticalScroll(rememberScrollState())) {
                Text(stringResource(R.string.widgets_hint), color = colors.textSecondary, fontSize = 13.sp)
                (chosen + available.filter { it !in chosen }).forEach { w ->
                    val index = chosen.indexOf(w)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        androidx.compose.material3.Checkbox(checked = w in chosen, onCheckedChange = { v -> chosen = if (v) chosen + w else chosen - w })
                        Text(stringResource(w.title), color = colors.textPrimary, fontSize = 15.sp, modifier = Modifier.weight(1f))
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
        dismissButton = { TextButton(onClick = { onSave(DEFAULT_WIDGETS) }) { Text(stringResource(R.string.shortcuts_default), color = colors.textSecondary) } },
        containerColor = colors.panel,
    )
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
