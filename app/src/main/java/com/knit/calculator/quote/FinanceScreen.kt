package com.knit.calculator.quote

import android.app.Application
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewModelScope
import com.knit.calculator.R
import com.knit.calculator.core.AbcRow
import com.knit.calculator.core.CashFlow
import com.knit.calculator.core.CashItem
import com.knit.calculator.core.Deal
import com.knit.calculator.core.DirectorReport
import com.knit.calculator.core.Payment
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.RegularPayment
import com.knit.calculator.core.Sale
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.math.BigDecimal
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Данные платёжного календаря из таблицы/МойСклад. */
data class FinanceData(
    val plan: BigDecimal = BigDecimal.ZERO,
    val balance: BigDecimal? = null,
    val balanceFromMs: Boolean = false,
    val regular: List<RegularPayment> = emptyList(),
    val supplier: List<CashItem> = emptyList(),
    val actualIn: List<Pair<Long, BigDecimal>> = emptyList(),
    val actualOut: List<Pair<Long, BigDecimal>> = emptyList(),
    val msError: String? = null,
) {
    companion object {
        private fun money(v: Any?): BigDecimal = v?.toString()?.toBigDecimalOrNull() ?: BigDecimal.ZERO
        private fun JSONArray?.objects(): List<JSONObject> = if (this == null) emptyList() else (0 until length()).mapNotNull { optJSONObject(it) }

        fun parse(o: JSONObject) = FinanceData(
            plan = money(o.opt("plan")),
            balance = if (o.isNull("balance") || !o.has("balance")) null else money(o.opt("balance")),
            balanceFromMs = o.optString("balanceSource") == "ms",
            regular = o.optJSONArray("regular").objects().map { RegularPayment(it.optString("name"), money(it.opt("amount")), it.optInt("day", 1), it.optString("category")) },
            supplier = o.optJSONArray("supplier").objects().map { CashItem(it.optLong("due"), money(it.opt("amount")), it.optString("name"), inflow = false, category = "Поставщики") },
            actualIn = o.optJSONArray("actualIn").objects().map { it.optLong("date") to money(it.opt("amount")) },
            actualOut = o.optJSONArray("actualOut").objects().map { it.optLong("date") to money(it.opt("amount")) },
            msError = o.optString("msError").ifBlank { null },
        )
    }
}

/** Зарплата менеджера за месяц: оклад + % от оплат по его КП. */
data class SalaryRow(val name: String, val paid: BigDecimal, val bonus: BigDecimal, val total: BigDecimal)

data class SalaryData(val month: String, val base: BigDecimal, val percent: BigDecimal, val rows: List<SalaryRow>) {
    companion object {
        private fun money(v: Any?): BigDecimal = v?.toString()?.toBigDecimalOrNull() ?: BigDecimal.ZERO

        fun parse(o: JSONObject): SalaryData {
            val a = o.optJSONArray("rows") ?: JSONArray()
            return SalaryData(
                o.optString("month"), money(o.opt("base")), money(o.opt("percent")),
                (0 until a.length()).mapNotNull { a.optJSONObject(it) }.map {
                    SalaryRow(it.optString("name"), money(it.opt("paid")), money(it.opt("bonus")), money(it.opt("total")))
                },
            )
        }
    }
}

class FinanceViewModel(application: Application) : AndroidViewModel(application) {
    private val _salary = MutableStateFlow<SalaryData?>(null)
    val salary: StateFlow<SalaryData?> = _salary.asStateFlow()

    private val store = QuoteStore(application)
    private val _data = MutableStateFlow<FinanceData?>(null)
    val data: StateFlow<FinanceData?> = _data.asStateFlow()
    private val _error = MutableStateFlow<String?>(null)
    val error: StateFlow<String?> = _error.asStateFlow()
    private val _loading = MutableStateFlow(false)
    val loading: StateFlow<Boolean> = _loading.asStateFlow()

    fun load() {
        val config = store.loadSyncConfig()
        if (!config.enabled) {
            _error.value = getApplication<Application>().getString(R.string.finance_need_sheet)
            return
        }
        if (_loading.value) return
        _loading.value = true
        _error.value = null
        viewModelScope.launch {
            try {
                val client = SheetClient(config)
                _data.value = FinanceData.parse(client.finance())
                _salary.value = runCatching { SalaryData.parse(client.salary()) }.getOrNull()
            } catch (e: Exception) {
                _error.value = e.message ?: "Нет связи с таблицей"
            } finally {
                _loading.value = false
            }
        }
    }
}

private val RED = Color(0xFFD32F2F)
private fun rub(v: BigDecimal) = QuoteCalculator.formatMoney(v) + " ₽"
private fun day(t: Long) = SimpleDateFormat("dd.MM", Locale.getDefault()).format(Date(t))

/** Сделки истории КП → продажи для отчёта (дата из «дд.мм.гггг чч:мм»). */
fun HistoryItem.toSale(): Sale {
    val time = runCatching { SimpleDateFormat("dd.MM.yyyy HH:mm", Locale.US).parse(date)?.time }.getOrNull()
        ?: runCatching { SimpleDateFormat("dd.MM.yyyy", Locale.US).parse(date.substringBefore(' '))?.time }.getOrNull() ?: 0L
    return Sale(id, number, client, total, status, time, author, profit, products)
}

/**
 * «Финансы» (только директор): платёжный календарь на 12 недель с кассовыми разрывами
 * и отчёт директора — план/факт, средний чек, менеджеры, ABC клиентов и товаров.
 */
@Composable
fun FinanceScreen(quoteVm: QuoteViewModel, opsVm: OpsViewModel, financeVm: FinanceViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val data by financeVm.data.collectAsStateWithLifecycle()
    val error by financeVm.error.collectAsStateWithLifecycle()
    val loading by financeVm.loading.collectAsStateWithLifecycle()
    val deals by quoteVm.deals.collectAsStateWithLifecycle()
    val ops by opsVm.data.collectAsStateWithLifecycle()
    val settings by quoteVm.settings.collectAsStateWithLifecycle()
    var tab by rememberSaveable { mutableStateOf(0) }
    LaunchedEffect(Unit) {
        financeVm.load()
        quoteVm.loadDeals()
        opsVm.load()
    }
    val now = remember { System.currentTimeMillis() }

    FormScreen(stringResource(R.string.finance_title), onBack, actions = {
        KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.sync_refresh), { financeVm.load(); quoteVm.loadDeals(); opsVm.load() })
    }) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(R.string.finance_tab_cash, R.string.finance_tab_report).forEachIndexed { i, label ->
                FilterChip(
                    selected = tab == i, onClick = { tab = i }, label = { Text(stringResource(label)) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                )
            }
        }
        if (loading) Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
        error?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp) }
        val d = data ?: return@FormScreen
        d.msError?.let { Text(stringResource(R.string.ms_error, it), color = colors.textSecondary, fontSize = 13.sp) }
        val list = deals.orEmpty()
        if (tab == 0) CashTab(d, list, ops, settings, now) else {
            ReportTab(d, list, ops, now)
            val salary by financeVm.salary.collectAsStateWithLifecycle()
            salary?.let { SalaryBlock(it) }
        }
    }
}

@Composable
private fun Card(content: @Composable () -> Unit) {
    Surface(shape = RoundedCornerShape(18.dp), color = LocalKnitColors.current.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) { content() }
    }
}

@Composable
private fun CashTab(d: FinanceData, deals: List<HistoryItem>, ops: OpsData, settings: CompanySettings, now: Long) {
    val colors = LocalKnitColors.current
    val weeks = remember(d, deals, ops, settings) {
        val horizon = now + 12 * 7 * 86_400_000L
        val inflows = CashFlow.expectedInflows(
            deals.map { Deal(it.id, it.number, it.client, it.total, it.status) }, ops.paymentsForDebts, ops.orders, settings.prepay, now,
        )
        val outflows = CashFlow.regular(d.regular, CashFlow.weekStart(now), horizon) + d.supplier
        CashFlow.weeks(d.balance ?: BigDecimal.ZERO, inflows + outflows, now)
    }
    var open by remember { mutableStateOf<Long?>(null) }

    Card {
        Text(stringResource(R.string.finance_balance), color = colors.textSecondary, fontSize = 14.sp)
        Text(d.balance?.let(::rub) ?: "—", color = colors.textPrimary, fontSize = 26.sp, fontWeight = FontWeight.Bold)
        Text(
            stringResource(if (d.balance == null) R.string.finance_balance_none else if (d.balanceFromMs) R.string.finance_balance_ms else R.string.finance_balance_sheet),
            color = colors.textSecondary, fontSize = 13.sp,
        )
    }
    val gap = weeks.firstOrNull { it.gap }
    if (gap != null) {
        Card {
            Text(stringResource(R.string.finance_gap_title), color = RED, fontSize = 16.sp, fontWeight = FontWeight.Bold)
            Text(stringResource(R.string.finance_gap_text, day(gap.start), rub(gap.balance.negate())), color = colors.textPrimary, fontSize = 14.sp)
        }
    }
    if (d.regular.isEmpty()) Text(stringResource(R.string.finance_regular_empty), color = colors.textSecondary, fontSize = 13.sp)
    SectionTitle(R.string.finance_weeks)
    weeks.forEach { w ->
        Surface(
            shape = RoundedCornerShape(14.dp), color = colors.panel,
            modifier = Modifier.fillMaxWidth().clickable { open = if (open == w.start) null else w.start },
        ) {
            Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp)) {
                Row {
                    Text(day(w.start) + "–" + day(w.start + 6 * 86_400_000L), color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                    Text(rub(w.balance), color = if (w.gap) RED else colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Bold)
                }
                Text(
                    stringResource(R.string.finance_week_line, rub(w.inflow), rub(w.outflow)),
                    color = colors.textSecondary, fontSize = 13.sp,
                )
                if (open == w.start) {
                    w.items.forEach { i ->
                        Text(
                            (if (i.inflow) "+ " else "− ") + rub(i.amount) + " · " + day(i.date) + " · " + i.label,
                            color = if (i.inflow) colors.textPrimary else colors.textSecondary, fontSize = 13.sp,
                        )
                    }
                    if (w.items.isEmpty()) Text(stringResource(R.string.finance_week_empty), color = colors.textSecondary, fontSize = 13.sp)
                }
            }
        }
    }
    if (d.actualIn.isNotEmpty() || d.actualOut.isNotEmpty()) {
        val inSum = d.actualIn.fold(BigDecimal.ZERO) { a, x -> a + x.second }
        val outSum = d.actualOut.fold(BigDecimal.ZERO) { a, x -> a + x.second }
        Text(stringResource(R.string.finance_actual, rub(inSum), rub(outSum)), color = colors.textSecondary, fontSize = 13.sp)
    }
    Text(stringResource(R.string.finance_hint), color = colors.textSecondary, fontSize = 13.sp)
}

@Composable
private fun ReportTab(d: FinanceData, deals: List<HistoryItem>, ops: OpsData, now: Long) {
    val colors = LocalKnitColors.current
    val sales = remember(deals) { deals.map { it.toSale() } }
    // Оплаты по датам: из МойСклад (входящие платежи), иначе — лист «Оплаты».
    val payments = remember(d, ops) {
        if (d.actualIn.isNotEmpty()) d.actualIn.mapIndexed { i, (t, v) -> Payment("ms$i", "", 0, "", t, v) } else ops.payments
    }
    val months = remember(sales, payments, d) { DirectorReport.months(sales, payments, d.plan, now) }
    val current = months.firstOrNull()
    Card {
        Text(stringResource(R.string.report_plan_title), color = colors.textSecondary, fontSize = 14.sp)
        if (current != null) {
            Text(
                stringResource(R.string.report_plan_line, rub(current.paid), rub(current.plan), current.planPercent),
                color = if (current.planPercent >= 100) colors.textPrimary else RED, fontSize = 18.sp, fontWeight = FontWeight.Bold,
            )
        }
        Text(stringResource(R.string.report_avg_check, rub(DirectorReport.averageCheck(sales))), color = colors.textSecondary, fontSize = 14.sp)
    }
    SectionTitle(R.string.report_months)
    months.forEach { m ->
        Text(
            stringResource(R.string.report_month_line, m.month, m.quotes, rub(m.wonSum), rub(m.paid), m.planPercent),
            color = colors.textPrimary, fontSize = 14.sp,
        )
    }
    SectionTitle(R.string.report_managers)
    DirectorReport.managers(sales).forEach { r ->
        Text(
            stringResource(R.string.report_manager_line, r.name, r.quotes, r.conversion, rub(r.wonSum)) +
                (r.profit?.let { " · " + stringResource(R.string.finance_profit, rub(it)) } ?: ""),
            color = colors.textPrimary, fontSize = 14.sp,
        )
    }
    AbcBlock(R.string.report_abc_clients, DirectorReport.clients(sales))
    AbcBlock(R.string.report_abc_products, DirectorReport.products(sales))
    Text(stringResource(R.string.report_abc_hint), color = colors.textSecondary, fontSize = 13.sp)
}

@Composable
private fun SalaryBlock(s: SalaryData) {
    val colors = LocalKnitColors.current
    SectionTitle(R.string.salary_title)
    Text(stringResource(R.string.salary_rule, s.month, rub(s.base), s.percent.stripTrailingZeros().toPlainString()), color = colors.textSecondary, fontSize = 13.sp)
    if (s.rows.isEmpty()) Text(stringResource(R.string.salary_empty), color = colors.textSecondary, fontSize = 13.sp)
    s.rows.forEach { r ->
        Row {
            Column(Modifier.weight(1f)) {
                Text(r.name, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                Text(stringResource(R.string.salary_line, rub(r.paid), rub(r.bonus)), color = colors.textSecondary, fontSize = 13.sp)
            }
            Text(rub(r.total), color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Bold)
        }
    }
    val total = s.rows.fold(BigDecimal.ZERO) { a, r -> a + r.total }
    if (s.rows.size > 1) Text(stringResource(R.string.salary_total, rub(total)), color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Bold)
}

@Composable
private fun AbcBlock(title: Int, rows: List<AbcRow>) {
    val colors = LocalKnitColors.current
    SectionTitle(title)
    if (rows.isEmpty()) Text(stringResource(R.string.finance_abc_empty), color = colors.textSecondary, fontSize = 13.sp)
    rows.take(15).forEach { r ->
        Row {
            Text(r.group.toString(), color = if (r.group == 'A') colors.textPrimary else colors.textSecondary, fontWeight = FontWeight.Bold, fontSize = 14.sp, modifier = Modifier.padding(end = 8.dp))
            Text(r.name, color = colors.textPrimary, fontSize = 14.sp, modifier = Modifier.weight(1f))
            Text(rub(r.value) + " · " + r.share.toPlainString() + " %", color = colors.textSecondary, fontSize = 13.sp)
        }
    }
}
