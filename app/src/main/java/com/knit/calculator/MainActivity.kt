package com.knit.calculator

import android.graphics.Color
import android.os.Bundle
import androidx.fragment.app.FragmentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.data.ThemeMode
import com.knit.calculator.quote.CatalogScreen
import com.knit.calculator.quote.CompanyScreen
import com.knit.calculator.quote.ProductEditorScreen
import com.knit.calculator.quote.OpsViewModel
import com.knit.calculator.quote.OrderYarnScreen
import com.knit.calculator.quote.PaymentsScreen
import com.knit.calculator.quote.ProductionScreen
import com.knit.calculator.quote.StockScreen
import com.knit.calculator.quote.QuoteHistoryScreen
import com.knit.calculator.quote.ReportScreen
import com.knit.calculator.ui.HomeAction
import com.knit.calculator.ui.HomeScreen
import com.knit.calculator.ui.WebCatalogScreen
import com.knit.calculator.quote.QuoteScreen
import com.knit.calculator.quote.QuoteViewModel
import com.knit.calculator.quote.toSale
import com.knit.calculator.ui.CalculatorScreen
import com.knit.calculator.ui.theme.KnitTheme
import com.knit.calculator.yarn.YarnScreen
import com.knit.calculator.yarn.YarnViewModel

/** Экраны приложения; переход «назад» описан у каждого экрана. */
private enum class Screen { HOME, CALCULATOR, YARN, QUOTE, CATALOG, PRODUCT, COMPANY, QUOTE_HISTORY, REPORT, ORDER_YARN, SHOP, PAYMENTS, PRODUCTION, STOCK, LABELS, ABOUT, PRODUCTS, CLIENT, FINANCE, COST, COMMS, WAREHOUSE, WORKTIME, AI }

class MainActivity : FragmentActivity() {

    // Блокировка: при запуске и после 5 минут в фоне (если включена в настройках).
    private var locked by mutableStateOf(false)
    private var stoppedAt = 0L

    override fun onStart() {
        super.onStart()
        val away = stoppedAt > 0 && System.currentTimeMillis() - stoppedAt > LOCK_AFTER_MS
        if (away && lockOn()) locked = true
    }

    /** Защита включена: свой PIN или вход по отпечатку / PIN телефона. */
    private fun lockOn(): Boolean {
        val store = com.knit.calculator.quote.QuoteStore(this)
        return store.pinSet || (store.appLock && com.knit.calculator.ui.AppLock.available(this))
    }

    private fun biometricOn(): Boolean =
        com.knit.calculator.quote.QuoteStore(this).appLock && com.knit.calculator.ui.AppLock.available(this)

    override fun onStop() {
        super.onStop()
        stoppedAt = System.currentTimeMillis()
    }

    private companion object {
        const val LOCK_AFTER_MS = 5 * 60_000L
    }


    private val viewModel: CalculatorViewModel by viewModels()
    private val yarnViewModel: YarnViewModel by viewModels()
    private val quoteViewModel: QuoteViewModel by viewModels()
    private val opsViewModel: OpsViewModel by viewModels()
    private val labelViewModel: com.knit.calculator.label.LabelViewModel by viewModels()
    private val financeViewModel: com.knit.calculator.quote.FinanceViewModel by viewModels()
    private val warehouseViewModel: com.knit.calculator.quote.WarehouseViewModel by viewModels()
    private val workViewModel: com.knit.calculator.quote.WorkViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        applySecureScreen(this)
        if (savedInstanceState == null && lockOn()) locked = true
        // Ежедневная сводка (9:00): долги, отгрузки, просрочки, КП без ответа.
        com.knit.calculator.quote.DailyDigest.schedule(this, com.knit.calculator.quote.QuoteStore(this).digestEnabled)
        enableEdgeToEdge()
        setContent {
            val themeMode by viewModel.themeMode.collectAsStateWithLifecycle()
            val darkTheme = when (themeMode) {
                ThemeMode.SYSTEM -> isSystemInDarkTheme()
                ThemeMode.LIGHT -> false
                ThemeMode.DARK -> true
            }
            DisposableEffect(darkTheme) {
                val style = SystemBarStyle.auto(Color.TRANSPARENT, Color.TRANSPARENT) { darkTheme }
                enableEdgeToEdge(statusBarStyle = style, navigationBarStyle = style)
                onDispose {}
            }
            // Простой стек экранов: «назад» возвращает на предыдущий экран, в конце — главный.
            var stack by rememberSaveable { mutableStateOf(listOf(Screen.HOME.name)) }
            val screen = Screen.valueOf(stack.last())
            fun open(s: Screen) { stack = stack + s.name }
            var clientName by rememberSaveable { mutableStateOf("") }
            fun back() { stack = if (stack.size > 1) stack.dropLast(1) else stack }
            val sync by quoteViewModel.sync.collectAsStateWithLifecycle()
            val settings by quoteViewModel.settings.collectAsStateWithLifecycle()
            val director by quoteViewModel.director.collectAsStateWithLifecycle()
            val version = androidx.compose.runtime.remember {
                runCatching {
                    val info = packageManager.getPackageInfo(packageName, 0)
                    "${info.versionName} (${androidx.core.content.pm.PackageInfoCompat.getLongVersionCode(info)})"
                }.getOrDefault("")
            }
            // Панель «Сегодня» на главном: долги, просроченные заказы, КП без ответа.
            val deals by quoteViewModel.deals.collectAsStateWithLifecycle()
            val ops by opsViewModel.data.collectAsStateWithLifecycle()
            androidx.compose.runtime.LaunchedEffect(screen == Screen.HOME) {
                if (screen == Screen.HOME) {
                    quoteViewModel.loadDealsIfStale()
                    opsViewModel.loadIfStale()
                    if (!director) quoteViewModel.loadMySalaryIfStale() else financeViewModel.loadIfStale()
                }
            }
            // Графики директора: продажи (согласованные КП), приход и расход по месяцам.
            val finance by financeViewModel.data.collectAsStateWithLifecycle()
            val charts = if (!director) null else androidx.compose.runtime.remember(deals, ops, finance) {
                val won = setOf(com.knit.calculator.core.QuoteStatus.APPROVED, com.knit.calculator.core.QuoteStatus.IN_WORK, com.knit.calculator.core.QuoteStatus.PAID)
                val sales = deals.orEmpty().filter { it.status in won }.map { it.toSale().let { s -> s.date to s.total } }
                val f = finance
                val inflow = if (f != null && f.actualIn.isNotEmpty()) f.actualIn else ops.payments.map { it.date to it.amount }
                val outflow = if (f != null && f.actualOut.isNotEmpty()) f.actualOut else null
                val ship = if (f != null && f.hasShipments) mapOf(
                    com.knit.calculator.core.Grain.MONTH to f.shipments,
                    com.knit.calculator.core.Grain.DAY to f.shipDay,
                    com.knit.calculator.core.Grain.HOUR to f.shipHour,
                ) else null
                com.knit.calculator.ui.HomeCharts(
                    sales, f?.plan?.takeIf { it.signum() > 0 }, inflow, outflow, ship,
                    deals = deals.orEmpty().map { it.toSale() },
                    expenses = f?.expenses.orEmpty(),
                )
                    .takeIf { sales.isNotEmpty() || inflow.isNotEmpty() || outflow != null }
            }
            val financeLoading by financeViewModel.loading.collectAsStateWithLifecycle()
            val refreshing = sync.loading || financeLoading
            val mySalary by quoteViewModel.mySalary.collectAsStateWithLifecycle()
            val myPay = if (director) null else mySalary?.rows?.firstOrNull()?.let { Triple(it.total, it.paid, it.bonus) }
            val day = androidx.compose.runtime.remember(deals, ops) {
                deals?.let { list ->
                    val parse = java.text.SimpleDateFormat("dd.MM.yyyy", java.util.Locale.US)
                    com.knit.calculator.core.Dashboard.summary(
                        deals = list.map { com.knit.calculator.core.Deal(it.id, it.number, it.client, it.total, it.status) },
                        sentAt = list.associate { it.id to (runCatching { parse.parse(it.date.substringBefore(' '))?.time }.getOrNull() ?: System.currentTimeMillis()) },
                        payments = ops.paymentsForDebts,
                        orders = ops.orders,
                        now = System.currentTimeMillis(),
                    )
                }
            }
            // Предупреждения (например, «не записано в МойСклад») — окном, чтобы не пропустить.
            var notice by androidx.compose.runtime.remember { mutableStateOf<String?>(null) }
            androidx.compose.runtime.LaunchedEffect(Unit) {
                quoteViewModel.notices.collect { notice = it }
            }
            KnitTheme(darkTheme = darkTheme) {
                if (locked) {
                    com.knit.calculator.ui.LockScreen(
                        onBiometric = if (biometricOn()) ({ com.knit.calculator.ui.AppLock.prompt(this@MainActivity) { locked = false } }) else null,
                        onUnlocked = { locked = false },
                    )
                    return@KnitTheme
                }
                // Рабочее время: смена начинается сама при первом входе за день.
                androidx.compose.runtime.LaunchedEffect(Unit) { workViewModel.autoStart() }
                val shifts by workViewModel.shifts.collectAsStateWithLifecycle()
                var tick by androidx.compose.runtime.remember { androidx.compose.runtime.mutableLongStateOf(System.currentTimeMillis()) }
                androidx.compose.runtime.LaunchedEffect(Unit) {
                    while (true) { kotlinx.coroutines.delay(60_000); tick = System.currentTimeMillis() }
                }
                val store = androidx.compose.runtime.remember { com.knit.calculator.quote.QuoteStore(this@MainActivity) }
                var shortcuts by androidx.compose.runtime.remember {
                    androidx.compose.runtime.mutableStateOf(
                        store.homeShortcuts?.mapNotNull { n -> HomeAction.entries.firstOrNull { it.name == n } } ?: com.knit.calculator.ui.DEFAULT_SHORTCUTS,
                    )
                }
                var widgets by androidx.compose.runtime.remember {
                    androidx.compose.runtime.mutableStateOf(
                        store.homeWidgets?.mapNotNull { n -> com.knit.calculator.ui.HomeWidget.entries.firstOrNull { it.name == n } } ?: com.knit.calculator.ui.DEFAULT_WIDGETS,
                    )
                }
                val avatar = java.io.File(filesDir, "avatar.jpg")
                var avatarVersion by androidx.compose.runtime.remember { androidx.compose.runtime.mutableIntStateOf(0) }
                val work = androidx.compose.runtime.remember(shifts, tick, avatarVersion, sync) {
                    val cur = com.knit.calculator.core.WorkTime.current(shifts, tick)
                    com.knit.calculator.ui.WorkBar(
                        person = workViewModel.person(),
                        photoPath = avatar.takeIf { it.exists() }?.absolutePath,
                        photoVersion = avatarVersion,
                        shiftStart = cur?.start,
                        workedMinutes = com.knit.calculator.core.WorkTime.workedToday(shifts, tick),
                    )
                }
                when (screen) {
                    Screen.HOME -> HomeScreen(sync, themeMode, director, onTheme = viewModel::cycleTheme, day = day, version = version, myPay = myPay, charts = charts,
                        shortcuts = shortcuts,
                        onShortcuts = { list -> shortcuts = list; store.homeShortcuts = list.map { it.name } },
                        work = work,
                        widgets = widgets,
                        onWidgets = { list -> widgets = list; store.homeWidgets = list.map { it.name } },
                        onShift = { start -> if (start) workViewModel.start() else workViewModel.close(); tick = System.currentTimeMillis() },
                        onPhoto = { uri ->
                            // Фото профиля — уменьшенной копией (до 320 px), только на этом телефоне.
                            runCatching {
                                val bounds = android.graphics.BitmapFactory.Options().apply { inJustDecodeBounds = true }
                                contentResolver.openInputStream(uri)?.use { android.graphics.BitmapFactory.decodeStream(it, null, bounds) }
                                var sample = 1
                                while (maxOf(bounds.outWidth, bounds.outHeight) / (sample * 2) >= 320) sample *= 2
                                val bmp = contentResolver.openInputStream(uri)?.use {
                                    android.graphics.BitmapFactory.decodeStream(it, null, android.graphics.BitmapFactory.Options().apply { inSampleSize = sample })
                                }
                                bmp?.let { b -> avatar.outputStream().use { b.compress(android.graphics.Bitmap.CompressFormat.JPEG, 85, it) } }
                            }
                            avatarVersion++
                        },
                        refreshing = refreshing,
                        onRefresh = {
                            // Свайп вниз: заново всё — каталог, КП, учёт, финансы.
                            quoteViewModel.refresh()
                            quoteViewModel.loadDeals()
                            opsViewModel.load()
                            if (director) financeViewModel.load() else quoteViewModel.loadMySalary()
                        },
                    ) { action ->
                        open(
                            when (action) {
                                HomeAction.QUOTE -> Screen.QUOTE
                                HomeAction.YARN -> Screen.YARN
                                HomeAction.CALCULATOR -> Screen.CALCULATOR
                                HomeAction.HISTORY -> Screen.QUOTE_HISTORY
                                HomeAction.REPORT -> Screen.REPORT
                                HomeAction.SHOP -> Screen.SHOP
                                HomeAction.SETTINGS -> Screen.COMPANY
                                HomeAction.PAYMENTS -> Screen.PAYMENTS
                                HomeAction.PRODUCTION -> Screen.PRODUCTION
                                HomeAction.STOCK -> Screen.STOCK
                                HomeAction.LABELS -> Screen.LABELS
                                HomeAction.ABOUT -> Screen.ABOUT
                                HomeAction.PRODUCTS -> Screen.PRODUCTS
                                HomeAction.FINANCE -> Screen.FINANCE
                                HomeAction.COST -> Screen.COST
                                HomeAction.COMMS -> Screen.COMMS
                                HomeAction.WAREHOUSE -> Screen.WAREHOUSE
                                HomeAction.WORKTIME -> Screen.WORKTIME
                                HomeAction.AI -> Screen.AI
                            },
                        )
                    }
                    Screen.CALCULATOR -> CalculatorScreen(viewModel = viewModel, themeMode = themeMode, onBack = ::back)
                    Screen.YARN -> YarnScreen(viewModel = yarnViewModel, onBack = ::back)
                    Screen.QUOTE -> QuoteScreen(
                        viewModel = quoteViewModel,
                        onBack = ::back,
                        onOpenCatalog = { open(Screen.CATALOG) },
                        onOpenCompany = { open(Screen.COMPANY) },
                        onOpenHistory = { open(Screen.QUOTE_HISTORY) },
                        onOpenOrderYarn = { open(Screen.ORDER_YARN) },
                    )
                    Screen.QUOTE_HISTORY -> QuoteHistoryScreen(
                        viewModel = quoteViewModel,
                        opsViewModel = opsViewModel,
                        onBack = ::back,
                        onOpenProduction = { open(Screen.PRODUCTION) },
                        onOpenClient = { name -> clientName = name; open(Screen.CLIENT) },
                        onOpenLabels = { item ->
                            // Этикетки на все позиции МойСклад заказа: по одной копии, в упаковке — количество из КП.
                            val items = quoteViewModel.labelProducts(item)
                            if (items.isEmpty()) {
                                android.widget.Toast.makeText(this@MainActivity, R.string.label_from_order_empty, android.widget.Toast.LENGTH_LONG).show()
                            } else {
                                labelViewModel.setJobs(
                                    items.map { (p, qty) -> com.knit.calculator.label.LabelJob(p, 1, com.knit.calculator.core.QuoteCalculator.formatQuantity(qty).filter { it.isDigit() }) },
                                )
                                open(Screen.LABELS)
                            }
                        },
                        onOpened = {
                            // Из истории — в КП; повторное «назад» вернёт туда, откуда пришли.
                            stack = stack.dropLast(1).let { if (it.lastOrNull() == Screen.QUOTE.name) it else it + Screen.QUOTE.name }
                        },
                    )
                    Screen.CATALOG -> CatalogScreen(
                        viewModel = quoteViewModel,
                        onBack = ::back,
                        onEdit = { id ->
                            quoteViewModel.startEditing(id)
                            open(Screen.PRODUCT)
                        },
                    )
                    Screen.PRODUCT -> ProductEditorScreen(viewModel = quoteViewModel, onDone = ::back)
                    Screen.COMPANY -> CompanyScreen(viewModel = quoteViewModel, onBack = ::back)
                    Screen.REPORT -> ReportScreen(viewModel = quoteViewModel, onBack = ::back)
                    Screen.ORDER_YARN -> OrderYarnScreen(viewModel = quoteViewModel, opsViewModel = opsViewModel, onBack = ::back)
                    Screen.SHOP -> WebCatalogScreen(startUrl = settings.shopUrl, onBack = ::back)
                    Screen.PAYMENTS -> PaymentsScreen(quoteViewModel, opsViewModel, onBack = ::back, onOpenProduction = { open(Screen.PRODUCTION) })
                    Screen.PRODUCTION -> ProductionScreen(quoteViewModel, opsViewModel, onBack = ::back)
                    Screen.STOCK -> StockScreen(quoteViewModel, opsViewModel, onBack = ::back)
                    Screen.LABELS -> com.knit.calculator.label.LabelScreen(quoteViewModel, labelViewModel, onBack = ::back)
                    Screen.FINANCE -> com.knit.calculator.quote.FinanceScreen(quoteViewModel, opsViewModel, financeViewModel, onBack = ::back)
                    Screen.WORKTIME -> com.knit.calculator.quote.WorkTimeScreen(workViewModel, director, onBack = ::back)
                    Screen.AI -> com.knit.calculator.comms.AiScreen(quoteViewModel, director, onBack = ::back)
                    Screen.WAREHOUSE -> com.knit.calculator.quote.WarehouseScreen(quoteViewModel, warehouseViewModel, onBack = ::back)
                    Screen.COMMS -> com.knit.calculator.comms.CommsScreen(onBack = ::back)
                    Screen.COST -> com.knit.calculator.quote.CostScreen(quoteViewModel, onBack = ::back, onOpenQuote = { open(Screen.QUOTE) })
                    Screen.CLIENT -> com.knit.calculator.quote.ClientScreen(
                        quoteViewModel, opsViewModel, clientName, onBack = ::back, onNewQuote = { open(Screen.QUOTE) },
                    )
                    Screen.PRODUCTS -> com.knit.calculator.quote.ProductsScreen(
                        quoteViewModel,
                        onBack = ::back,
                        onAddToQuote = { p -> quoteViewModel.addLine(p); open(Screen.QUOTE) },
                        onLabel = { p -> labelViewModel.add(p); open(Screen.LABELS) },
                    )
                    Screen.ABOUT -> {
                        val msProducts by quoteViewModel.msProducts.collectAsStateWithLifecycle()
                        val printer by labelViewModel.printer.collectAsStateWithLifecycle()
                        val config by quoteViewModel.syncConfig.collectAsStateWithLifecycle()
                        com.knit.calculator.ui.AboutScreen(version, sync, msProducts.size, director, config.manager, printer.name, onBack = ::back)
                    }
                }
                notice?.let { text ->
                    androidx.compose.material3.AlertDialog(
                        onDismissRequest = { notice = null },
                        title = { androidx.compose.material3.Text(androidx.compose.ui.res.stringResource(R.string.notice_title)) },
                        text = { androidx.compose.material3.Text(text) },
                        confirmButton = {
                            androidx.compose.material3.TextButton(onClick = { notice = null }) {
                                androidx.compose.material3.Text(androidx.compose.ui.res.stringResource(R.string.ok))
                            }
                        },
                    )
                }
            }
        }
    }
}

/** Запрет снимков экрана по настройке «secureScreen» (сразу, без перезапуска). */
fun applySecureScreen(activity: android.app.Activity) {
    if (com.knit.calculator.quote.QuoteStore(activity).secureScreen) {
        activity.window.setFlags(android.view.WindowManager.LayoutParams.FLAG_SECURE, android.view.WindowManager.LayoutParams.FLAG_SECURE)
    } else {
        activity.window.clearFlags(android.view.WindowManager.LayoutParams.FLAG_SECURE)
    }
}
