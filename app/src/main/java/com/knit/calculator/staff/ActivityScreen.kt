package com.knit.calculator.staff

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
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.quote.FormScreen
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

private val IDLE_RED = Color(0xFFD32F2F)

/** Экраны приложения в статистике. */
private val SCREEN_TITLES = mapOf(
    "home" to "Главный", "quote" to "КП", "quote_history" to "Заказы и КП", "ms_orders" to "Заказы МойСклад", "products" to "Товары",
    "client" to "Клиенты", "payments" to "Оплаты", "finance" to "Финансы", "warehouse" to "Склад", "labels" to "Этикетки",
    "tasks" to "Задачи", "comms" to "Связь", "ai" to "ИИ", "shop" to "Магазин", "staff_production" to "Производство",
    "calculator" to "Калькулятор", "yarn" to "Пряжа", "report" to "Отчёт", "worktime" to "Рабочее время", "activity" to "Активность",
)

/** Названия действий в отчёте. */
private val KINDS = mapOf(
    "quote" to "КП", "quoteSend" to "отправлено КП", "client" to "клиенты", "product" to "товары", "order" to "заказы",
    "payment" to "оплаты", "ship" to "отгрузки", "receive" to "приёмки", "inventory" to "инвентаризации", "label" to "этикетки",
    "call" to "звонки", "other" to "другое",
)

/**
 * Активность офиса (директор, помощник): сколько каждый работает в приложении, когда заходил,
 * самый долгий перерыв, что сделал; красным — нет в приложении дольше порога в рабочее время.
 * Внизу — лента действий и журнал изменений МойСклад.
 */
@Composable
fun ActivityScreen(vm: StaffViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val me by vm.me.collectAsStateWithLifecycle()
    val report by vm.activity.collectAsStateWithLifecycle()
    val msAudit by vm.msAudit.collectAsStateWithLifecycle()
    var days by rememberSaveable { mutableIntStateOf(1) }
    LaunchedEffect(days, me?.full) {
        if (me?.full == true) {
            vm.loadActivity(days)
            vm.loadMsAudit(days)
        }
    }
    val hm = SimpleDateFormat("HH:mm", Locale("ru"))
    val dm = SimpleDateFormat("d MMM HH:mm", Locale("ru"))
    FormScreen(stringResource(R.string.activity_title), onBack) {
        if (me?.full != true) {
            Text(stringResource(R.string.staff_need_server), color = colors.textSecondary, fontSize = 14.sp)
            return@FormScreen
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOf(1 to R.string.period_today, 7 to R.string.period_7d, 30 to R.string.period_30d).forEach { (d, label) ->
                FilterChip(
                    selected = d == days, onClick = { days = d }, label = { Text(stringResource(label)) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                )
            }
        }
        val r = report
        if (r == null) {
            Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
            return@FormScreen
        }
        if (r.people.isEmpty()) Text(stringResource(R.string.activity_empty), color = colors.textSecondary, fontSize = 14.sp)
        r.people.forEach { p ->
            Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(p.name, color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                        Text(hours(p.minutes), color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.Bold)
                    }
                    if (p.idle) {
                        Text(
                            stringResource(R.string.activity_idle, p.last?.let { hm.format(Date(it)) } ?: "—", r.idleMinutes),
                            color = IDLE_RED, fontSize = 13.sp, fontWeight = FontWeight.SemiBold,
                        )
                    }
                    Text(
                        stringResource(
                            R.string.activity_line, p.sessions, p.first?.let { hm.format(Date(it)) } ?: "—",
                            p.last?.let { hm.format(Date(it)) } ?: "—", p.maxGapMinutes,
                        ),
                        color = colors.textSecondary, fontSize = 13.sp,
                    )
                    val acts = p.actions.entries.sortedByDescending { it.value }.joinToString(" · ") { "${KINDS[it.key] ?: it.key}: ${it.value}" }
                    Text(acts.ifEmpty { stringResource(R.string.activity_no_actions) }, color = colors.textSecondary, fontSize = 13.sp)
                }
            }
        }
        Text(stringResource(R.string.activity_hint, r.idleMinutes), color = colors.textSecondary, fontSize = 12.sp)
        if (r.screens.isNotEmpty()) {
            SectionTitle(R.string.activity_screens)
            val total = r.screens.sumOf { it.second }.coerceAtLeast(1)
            r.screens.forEach { (s, m) ->
                Text("${SCREEN_TITLES[s] ?: s} — ${hours(m)} (${m * 100 / total} %)", color = colors.textPrimary, fontSize = 13.sp)
            }
        }
        if (r.feed.isNotEmpty()) {
            SectionTitle(R.string.activity_feed)
            r.feed.take(60).forEach { f ->
                Text("${dm.format(Date(f.time))} · ${f.who} · ${KINDS[f.kind] ?: f.kind}" + (if (f.detail.isNotBlank()) " — ${f.detail}" else ""),
                    color = colors.textPrimary, fontSize = 13.sp)
            }
        }
        SectionTitle(R.string.activity_ms)
        val ms = msAudit
        when {
            ms == null -> Text(stringResource(R.string.activity_ms_none), color = colors.textSecondary, fontSize = 13.sp)
            ms.isEmpty() -> Text(stringResource(R.string.activity_ms_empty), color = colors.textSecondary, fontSize = 13.sp)
            else -> ms.take(60).forEach { a ->
                Text("${a.moment.take(16)} · ${a.who} · ${a.event} ${a.entity}" + (if (a.count > 1) " ×${a.count}" else "") + (if (a.info.isNotBlank()) " — ${a.info}" else ""),
                    color = colors.textPrimary, fontSize = 13.sp)
            }
        }
    }
}
