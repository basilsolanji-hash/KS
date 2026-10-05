package com.knit.calculator

import android.graphics.Color
import android.os.Bundle
import androidx.activity.ComponentActivity
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
import com.knit.calculator.ui.CalculatorScreen
import com.knit.calculator.ui.theme.KnitTheme
import com.knit.calculator.yarn.YarnScreen
import com.knit.calculator.yarn.YarnViewModel

/** Экраны приложения; переход «назад» описан у каждого экрана. */
private enum class Screen { HOME, CALCULATOR, YARN, QUOTE, CATALOG, PRODUCT, COMPANY, QUOTE_HISTORY, REPORT, ORDER_YARN, SHOP, PAYMENTS, PRODUCTION, STOCK, LABELS, ABOUT }

class MainActivity : ComponentActivity() {

    private val viewModel: CalculatorViewModel by viewModels()
    private val yarnViewModel: YarnViewModel by viewModels()
    private val quoteViewModel: QuoteViewModel by viewModels()
    private val opsViewModel: OpsViewModel by viewModels()
    private val labelViewModel: com.knit.calculator.label.LabelViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
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
                }
            }
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
                when (screen) {
                    Screen.HOME -> HomeScreen(sync, themeMode, director, onTheme = viewModel::cycleTheme, day = day, version = version) { action ->
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
