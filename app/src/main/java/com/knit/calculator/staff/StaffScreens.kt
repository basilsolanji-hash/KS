package com.knit.calculator.staff

import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.quote.FormScreen
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

/** Цвета этапов производства (одинаковые у всех сотрудников). */
val STAGE_COLORS = mapOf(
    "yarn" to Color(0xFF8D6E63), "spec" to Color(0xFF5C6BC0), "setup" to Color(0xFF26A69A), "knit" to Color(0xFF42A5F5),
    "coupons" to Color(0xFFAB47BC), "wto" to Color(0xFFFF7043), "qc" to Color(0xFFFFB300), "pack" to Color(0xFF66BB6A),
)

private val RED = Color(0xFFD32F2F)
private val hm = SimpleDateFormat("dd.MM HH:mm", Locale.getDefault())

@Composable
private fun Message(vm: StaffViewModel) {
    val colors = LocalKnitColors.current
    val message by vm.message.collectAsStateWithLifecycle()
    val busy by vm.busy.collectAsStateWithLifecycle()
    if (busy) Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
    message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
}

// ---------------------------------------------------------------- Подключение

/** Сервер фабрики: вход сотрудника по ключу (или QR), первый вход директора; директору — Wi-Fi фабрики. */
@Composable
fun StaffConnectScreen(vm: StaffViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val me by vm.me.collectAsStateWithLifecycle()
    val settings by vm.settings.collectAsStateWithLifecycle()
    var url by remember { mutableStateOf(vm.url) }
    var key by remember { mutableStateOf("") }
    var setup by remember { mutableStateOf(false) }
    var setupKey by remember { mutableStateOf("") }
    var name by remember { mutableStateOf("") }
    LaunchedEffect(me?.director) { if (me?.director == true) vm.loadSettings() }

    FormScreen(stringResource(R.string.staff_server), onBack) {
        Message(vm)
        val m = me
        if (m != null) {
            Surface(shape = RoundedCornerShape(16.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp)) {
                    Text(m.name, color = colors.textPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                    Text(m.roleTitle + " · " + vm.url, color = colors.textSecondary, fontSize = 13.sp)
                    if (m.tracked) Text(stringResource(R.string.activity_notice), color = colors.textSecondary, fontSize = 12.sp)
                }
            }
            if (m.director) {
                SectionTitle(R.string.staff_wifi)
                val s = settings
                var late by remember(s) { mutableStateOf(s?.lateMinutes?.toString() ?: "10") }
                var idle by remember(s) { mutableStateOf(s?.idleMinutes?.toString() ?: "30") }
                var ips by remember(s) { mutableStateOf(s?.allowedIps.orEmpty()) }
                var confirmOff by remember { mutableStateOf(false) }
                Text(stringResource(R.string.staff_wifi_hint, s?.myIp.orEmpty(), s?.allowedIps?.ifBlank { "—" } ?: "—"), color = colors.textSecondary, fontSize = 13.sp)
                ActionButton(R.string.staff_wifi_set, R.drawable.ic_web, primary = false, Modifier.fillMaxWidth()) {
                    // Добавить адрес этого телефона к уже разрешённым (несколько точек Wi-Fi фабрики).
                    val list = (s?.allowedIps.orEmpty().split(',') + s?.myIp.orEmpty()).map { it.trim() }.filter { it.isNotEmpty() }.distinct()
                    vm.saveSettings(list.joinToString(","), late.toIntOrNull() ?: 10, idle.toIntOrNull() ?: 30)
                }
                KnitField(ips, { ips = it.take(500) }, R.string.staff_ips, text = true, maxLength = 500)
                KnitField(late, { late = it.filter(Char::isDigit).take(3) }, R.string.staff_late, suffix = "мин")
                KnitField(idle, { idle = it.filter(Char::isDigit).take(3) }, R.string.staff_idle, suffix = "мин")
                ActionButton(R.string.shortcuts_save, R.drawable.ic_cloud, primary = false, Modifier.fillMaxWidth()) {
                    vm.saveSettings(ips, late.toIntOrNull() ?: 10, idle.toIntOrNull() ?: 30)
                }
                if (s?.allowedIps?.isNotBlank() == true) {
                    TextButton(onClick = { confirmOff = true }) { Text(stringResource(R.string.staff_wifi_off), color = colors.textSecondary) }
                }
                if (confirmOff) {
                    AlertDialog(
                        onDismissRequest = { confirmOff = false },
                        title = { Text(stringResource(R.string.staff_wifi_off)) },
                        text = { Text(stringResource(R.string.staff_wifi_off_confirm), color = colors.textPrimary) },
                        confirmButton = { TextButton(onClick = { confirmOff = false; vm.saveSettings("", late.toIntOrNull() ?: 10, idle.toIntOrNull() ?: 30) }) { Text(stringResource(R.string.staff_wifi_off), color = RED) } },
                        dismissButton = { TextButton(onClick = { confirmOff = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
                        containerColor = colors.panel,
                    )
                }
            }
            TextButton(onClick = { vm.disconnect() }) { Text(stringResource(R.string.staff_disconnect), color = RED) }
            return@FormScreen
        }
        Text(stringResource(R.string.staff_connect_hint), color = colors.textSecondary, fontSize = 14.sp)
        KnitField(url, { url = it.trim() }, R.string.staff_url, text = true, maxLength = 120)
        if (!setup) {
            KnitField(key, { key = it.trim() }, R.string.staff_key, text = true, maxLength = 40)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ActionButton(R.string.staff_login, R.drawable.ic_lock, primary = true, Modifier.weight(1f)) { vm.connect(url, key) }
                ActionButton(R.string.staff_scan, R.drawable.ic_qr, primary = false, Modifier.weight(1f)) {
                    com.knit.calculator.quote.BarcodeScan.scan(context, { code ->
                        // QR от директора: {"ks":2,"u":"https://…","k":"ключ"} или просто ключ.
                        val o = runCatching { JSONObject(code) }.getOrNull()
                        if (o != null && o.optInt("ks") == 2) {
                            url = o.optString("u", url)
                            key = o.optString("k")
                        } else key = code.trim()
                        vm.connect(url, key)
                    }) { e -> android.widget.Toast.makeText(context, e, android.widget.Toast.LENGTH_LONG).show() }
                }
            }
            TextButton(onClick = { setup = true }) { Text(stringResource(R.string.staff_first_director), color = colors.textSecondary) }
        } else {
            Text(stringResource(R.string.staff_setup_hint), color = colors.textSecondary, fontSize = 13.sp)
            KnitField(name, { name = it }, R.string.staff_name, text = true, maxLength = 80)
            KnitField(setupKey, { setupKey = it.trim() }, R.string.staff_setup_key, text = true, maxLength = 80)
            ActionButton(R.string.staff_login, R.drawable.ic_lock, primary = true, Modifier.fillMaxWidth()) { vm.connect(url, "", setupKey, name) }
            TextButton(onClick = { setup = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) }
        }
    }
}

// ---------------------------------------------------------------- Производство

/**
 * Этапы производства по цветам: «Начать» и «Завершить» — только свои этапы (директор и помощник — все).
 * Видно, кто начал и закончил и за сколько минут.
 */
@Composable
fun StaffProductionScreen(vm: StaffViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val me by vm.me.collectAsStateWithLifecycle()
    val jobs by vm.jobs.collectAsStateWithLifecycle()
    var adding by remember { mutableStateOf(false) }
    var closing by remember { mutableStateOf<Job?>(null) }
    var finishing by remember { mutableStateOf<StageRun?>(null) }
    var starting by remember { mutableStateOf<Pair<Job, Triple<String, String, Boolean>>?>(null) }
    LaunchedEffect(Unit) { vm.loadJobs() }
    val m = me
    FormScreen(stringResource(R.string.staff_production), onBack, actions = {
        KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.sync_refresh), { vm.loadJobs() })
        if (m != null && (m.full || m.role == "manager")) KnitIconButton(R.drawable.ic_add, stringResource(R.string.staff_job_add), { adding = true })
    }) {
        if (m == null) {
            Text(stringResource(R.string.staff_need_server), color = colors.textSecondary, fontSize = 14.sp)
            return@FormScreen
        }
        Message(vm)
        // Легенда цветов.
        Row(horizontalArrangement = Arrangement.spacedBy(6.dp), modifier = Modifier.fillMaxWidth()) {
            m.stages.forEach { (k, _, _) ->
                Surface(shape = RoundedCornerShape(4.dp), color = STAGE_COLORS[k] ?: colors.panel, modifier = Modifier.weight(1f).height(6.dp)) {}
            }
        }
        val list = jobs
        if (list != null && list.isEmpty()) Text(stringResource(R.string.staff_no_jobs), color = colors.textSecondary, fontSize = 14.sp)
        list.orEmpty().forEach { job ->
            Surface(shape = RoundedCornerShape(16.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text(job.title, color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                    if (job.client.isNotBlank() || job.quantity > 0) {
                        Text(listOf(job.client, if (job.quantity > 0) "${job.quantity} шт" else "").filter { it.isNotBlank() }.joinToString(" · "), color = colors.textSecondary, fontSize = 13.sp)
                    }
                    m.stages.forEach { stage ->
                        val (key, title, mine) = stage
                        val runs = job.stages.filter { it.stage == key }
                        val running = runs.firstOrNull { it.running }
                        val done = runs.lastOrNull { !it.running }
                        val color = STAGE_COLORS[key] ?: colors.accent
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Surface(
                                shape = RoundedCornerShape(8.dp),
                                color = when {
                                    running != null -> color.copy(alpha = 0.35f)
                                    done != null -> color
                                    else -> Color.Transparent
                                },
                                modifier = Modifier.weight(1f).border(1.dp, color, RoundedCornerShape(8.dp)),
                            ) {
                                Column(Modifier.padding(horizontal = 10.dp, vertical = 6.dp)) {
                                    Text((if (done != null && running == null) "✓ " else if (running != null) "▶ " else "") + title,
                                        color = if (done != null && running == null) Color.White else colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
                                    val line = when {
                                        running != null -> stringResource(R.string.staff_stage_running, running.startedBy.orEmpty(), running.startedAt?.let { hm.format(Date(it)) }.orEmpty())
                                        done != null -> stringResource(R.string.staff_stage_done, done.finishedBy.orEmpty(), done.minutes ?: 0, done.quantity)
                                        else -> ""
                                    }
                                    if (line.isNotEmpty()) Text(line, color = if (done != null && running == null) Color.White else colors.textSecondary, fontSize = 12.sp)
                                }
                            }
                            if (mine) {
                                if (running != null) TextButton(onClick = { finishing = running }) { Text(stringResource(R.string.staff_finish), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
                                else TextButton(onClick = { starting = job to stage }) { Text(stringResource(R.string.staff_start), color = colors.textPrimary) }
                            }
                        }
                    }
                    if (m.full && job.stages.any { it.stage == "pack" && !it.running }) {
                        TextButton(onClick = { closing = job }) { Text(stringResource(R.string.staff_job_close), color = colors.textSecondary) }
                    }
                }
            }
        }
    }

    starting?.let { (job, stage) ->
        AlertDialog(
            onDismissRequest = { starting = null },
            title = { Text(stage.second) },
            text = { Text(stringResource(R.string.staff_start_confirm, job.title), color = colors.textPrimary) },
            confirmButton = { TextButton(onClick = { vm.startStage(job, stage.first); starting = null }) { Text(stringResource(R.string.staff_start), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) } },
            dismissButton = { TextButton(onClick = { starting = null }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
    closing?.let { job ->
        AlertDialog(
            onDismissRequest = { closing = null },
            title = { Text(stringResource(R.string.staff_job_close)) },
            text = { Text(stringResource(R.string.staff_job_close_confirm, job.title), color = colors.textPrimary) },
            confirmButton = { TextButton(onClick = { vm.closeJob(job); closing = null }) { Text(stringResource(R.string.staff_job_close), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) } },
            dismissButton = { TextButton(onClick = { closing = null }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
    finishing?.let { run ->
        // Черновик количества и комментария живёт до успешного ответа сервера (ошибка связи не стирает ввод).
        var qty by rememberSaveable(run.id) { mutableStateOf("") }
        var comment by rememberSaveable(run.id) { mutableStateOf("") }
        val busy by vm.busy.collectAsStateWithLifecycle()
        val qtyValue = qty.toIntOrNull()
        val canFinish = !busy && qtyValue != null && (qtyValue > 0 || comment.isNotBlank())
        AlertDialog(
            onDismissRequest = { finishing = null },
            title = { Text(stringResource(R.string.staff_finish)) },
            text = {
                Column {
                    KnitField(qty, { qty = it.filter(Char::isDigit).take(7) }, R.string.staff_done_qty, suffix = "шт")
                    KnitField(comment, { comment = it }, R.string.staff_comment, text = true, maxLength = 300)
                }
            },
            confirmButton = {
                TextButton(enabled = canFinish, onClick = { vm.finishStage(run, qtyValue ?: 0, comment) { finishing = null } }) {
                    Text(stringResource(R.string.staff_finish), color = if (canFinish) colors.textPrimary else colors.textSecondary, fontWeight = FontWeight.SemiBold)
                }
            },
            dismissButton = { TextButton(onClick = { finishing = null }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
    if (adding) {
        var title by remember { mutableStateOf("") }
        var client by remember { mutableStateOf("") }
        var qty by remember { mutableStateOf("") }
        AlertDialog(
            onDismissRequest = { adding = false },
            title = { Text(stringResource(R.string.staff_job_add)) },
            text = {
                Column {
                    KnitField(title, { title = it }, R.string.staff_job_title, text = true, maxLength = 200)
                    KnitField(client, { client = it }, R.string.quote_client_company, text = true, maxLength = 200)
                    KnitField(qty, { qty = it.filter(Char::isDigit).take(7) }, R.string.staff_done_qty, suffix = "шт")
                }
            },
            confirmButton = {
                TextButton(enabled = title.isNotBlank(), onClick = { vm.saveJob(title.trim(), client.trim(), qty.toIntOrNull() ?: 0); adding = false }) {
                    Text(stringResource(R.string.comms_add), color = colors.textPrimary, fontWeight = FontWeight.SemiBold)
                }
            },
            dismissButton = { TextButton(onClick = { adding = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
}

// ---------------------------------------------------------------- Сотрудники

private val DAY_NAMES = listOf("Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс")

/** Сотрудники (директор): роль, должность, контакты, график; ключ входа и QR; импорт и карточка МойСклад. */
@Composable
fun EmployeesScreen(vm: StaffViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val me by vm.me.collectAsStateWithLifecycle()
    val employees by vm.employees.collectAsStateWithLifecycle()
    var editing by remember { mutableStateOf<Employee?>(null) }
    var shownKey by remember { mutableStateOf<String?>(null) }
    var msCard by remember { mutableStateOf<Employee?>(null) }
    LaunchedEffect(Unit) { vm.loadEmployees() }
    val m = me
    FormScreen(stringResource(R.string.staff_employees), onBack, actions = {
        if (m?.director == true) KnitIconButton(R.drawable.ic_add, stringResource(R.string.staff_employee_add), {
            editing = Employee(0, "", "manager", "", null, true, Schedule(listOf(1, 2, 3, 4, 5), "09:00", "18:00"), "", "", false)
        })
    }) {
        if (m == null) {
            Text(stringResource(R.string.staff_need_server), color = colors.textSecondary, fontSize = 14.sp)
            return@FormScreen
        }
        Message(vm)
        if (m.full) ActionButton(R.string.staff_ms_import, R.drawable.ic_arrow_down, primary = false, Modifier.fillMaxWidth()) { vm.importFromMs() }
        employees.orEmpty().forEach { e ->
            Surface(onClick = { if (m.director) editing = e }, shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(e.name, color = if (e.active) colors.textPrimary else colors.textSecondary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                        Text(m.roles[e.role] ?: e.role, color = colors.textSecondary, fontSize = 13.sp)
                    }
                    val days = if (e.schedule.cycle) "${e.schedule.cycleOn}/${e.schedule.cycleOff}" else e.schedule.days.sorted().joinToString(" ") { DAY_NAMES.getOrElse(it - 1) { "?" } }
                    Text(
                        listOf(e.position, "$days ${e.schedule.start}–${e.schedule.end}", if (!e.active) stringResource(R.string.staff_inactive) else "", if (e.msId != null) "МойСклад" else "")
                            .filter { it.isNotBlank() }.joinToString(" · "),
                        color = colors.textSecondary, fontSize = 13.sp,
                    )
                }
            }
        }
    }

    editing?.let { start ->
        var e by remember(start.id) { mutableStateOf(start) }
        AlertDialog(
            onDismissRequest = { editing = null },
            title = { Text(if (start.id == 0) stringResource(R.string.staff_employee_add) else start.name) },
            text = {
                Column(Modifier.heightIn(max = 520.dp).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    KnitField(e.name, { e = e.copy(name = it) }, R.string.staff_name, text = true, maxLength = 120)
                    Text(stringResource(R.string.staff_role), color = colors.textSecondary, fontSize = 13.sp)
                    m?.roles.orEmpty().entries.chunked(2).forEach { pair ->
                        Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            pair.forEach { (k, title) ->
                                FilterChip(
                                    selected = e.role == k, onClick = { e = e.copy(role = k) }, label = { Text(title, fontSize = 13.sp) },
                                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                                    modifier = Modifier.weight(1f),
                                )
                            }
                        }
                    }
                    KnitField(e.position, { e = e.copy(position = it) }, R.string.staff_position, text = true, maxLength = 120)
                    KnitField(e.phone, { e = e.copy(phone = it) }, R.string.quote_client_phone, text = true, maxLength = 40)
                    KnitField(e.email, { e = e.copy(email = it.trim()) }, R.string.quote_client_email, text = true, maxLength = 120)
                    Text(stringResource(R.string.staff_schedule), color = colors.textSecondary, fontSize = 13.sp)
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        FilterChip(
                            selected = !e.schedule.cycle, onClick = { e = e.copy(schedule = e.schedule.copy(cycleOn = 0)) },
                            label = { Text(stringResource(R.string.staff_schedule_week)) },
                            colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                        )
                        FilterChip(
                            selected = e.schedule.cycle,
                            onClick = {
                                val today = SimpleDateFormat("yyyy-MM-dd", Locale.US).format(Date())
                                e = e.copy(schedule = e.schedule.copy(cycleOn = 2, cycleOff = 2, anchor = e.schedule.anchor.ifBlank { today }))
                            },
                            label = { Text(stringResource(R.string.staff_schedule_cycle)) },
                            colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                        )
                    }
                    if (e.schedule.cycle) {
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            KnitField(e.schedule.cycleOn.toString(), { v -> e = e.copy(schedule = e.schedule.copy(cycleOn = v.filter(Char::isDigit).take(2).toIntOrNull()?.coerceIn(1, 14) ?: 1)) }, R.string.staff_cycle_on, modifier = Modifier.weight(1f))
                            KnitField(e.schedule.cycleOff.toString(), { v -> e = e.copy(schedule = e.schedule.copy(cycleOff = v.filter(Char::isDigit).take(2).toIntOrNull()?.coerceIn(0, 14) ?: 0)) }, R.string.staff_cycle_off, modifier = Modifier.weight(1f))
                        }
                        KnitField(e.schedule.anchor, { v -> e = e.copy(schedule = e.schedule.copy(anchor = v.take(10))) }, R.string.staff_cycle_anchor, text = true, maxLength = 10)
                    }
                    if (!e.schedule.cycle) Row(horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                        DAY_NAMES.forEachIndexed { i, d ->
                            val day = i + 1
                            FilterChip(
                                selected = day in e.schedule.days,
                                onClick = { e = e.copy(schedule = e.schedule.copy(days = if (day in e.schedule.days) e.schedule.days - day else e.schedule.days + day)) },
                                label = { Text(d, fontSize = 11.sp) },
                                colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                                modifier = Modifier.weight(1f),
                            )
                        }
                    }
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        KnitField(e.schedule.start, { e = e.copy(schedule = e.schedule.copy(start = it.take(5))) }, R.string.staff_start_time, text = true, maxLength = 5, modifier = Modifier.weight(1f))
                        KnitField(e.schedule.end, { e = e.copy(schedule = e.schedule.copy(end = it.take(5))) }, R.string.staff_end_time, text = true, maxLength = 5, modifier = Modifier.weight(1f))
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(stringResource(R.string.staff_active), color = colors.textPrimary, modifier = Modifier.weight(1f))
                        Switch(checked = e.active, onCheckedChange = { e = e.copy(active = it) }, colors = SwitchDefaults.colors(checkedTrackColor = colors.equalsKey, checkedThumbColor = colors.equalsKeyText))
                    }
                    if (start.id != 0) {
                        TextButton(onClick = { vm.newKey(start) { k -> shownKey = k }; editing = null }) { Text(stringResource(R.string.staff_new_key), color = colors.textPrimary) }
                        if (start.msId != null) TextButton(onClick = { msCard = start; editing = null }) { Text(stringResource(R.string.staff_ms_card), color = colors.textPrimary) }
                    }
                }
            },
            confirmButton = {
                TextButton(enabled = e.name.isNotBlank(), onClick = { vm.saveEmployee(e) { k -> shownKey = k }; editing = null }) {
                    Text(stringResource(R.string.shortcuts_save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold)
                }
            },
            dismissButton = { TextButton(onClick = { editing = null }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
    // Ключ показывается один раз: продиктовать или отсканировать QR на телефоне сотрудника.
    shownKey?.let { key ->
        val qr = JSONObject().put("ks", 2).put("u", vm.url).put("k", key).toString()
        AlertDialog(
            onDismissRequest = { shownKey = null },
            title = { Text(stringResource(R.string.staff_key_title)) },
            text = {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(key, color = colors.textPrimary, fontSize = 22.sp, fontWeight = FontWeight.Bold)
                    val bitmap = remember(qr) { runCatching { com.knit.calculator.quote.DocPdf.qrBitmap(qr, 600) }.getOrNull() }
                    bitmap?.let { androidx.compose.foundation.Image(it.asImageBitmap(), null, Modifier.padding(top = 8.dp).height(220.dp)) }
                    Text(stringResource(R.string.staff_key_hint), color = colors.textSecondary, fontSize = 13.sp)
                }
            },
            confirmButton = { TextButton(onClick = { shownKey = null }) { Text(stringResource(R.string.products_close), color = colors.textPrimary) } },
            containerColor = colors.panel,
        )
    }
    msCard?.let { emp ->
        val parts = emp.name.split(' ')
        var last by remember(emp.id) { mutableStateOf(parts.getOrElse(0) { "" }) }
        var first by remember(emp.id) { mutableStateOf(parts.getOrElse(1) { "" }) }
        var middle by remember(emp.id) { mutableStateOf(parts.drop(2).joinToString(" ")) }
        var position by remember(emp.id) { mutableStateOf(emp.position) }
        var phone by remember(emp.id) { mutableStateOf(emp.phone) }
        var email by remember(emp.id) { mutableStateOf(emp.email) }
        AlertDialog(
            onDismissRequest = { msCard = null },
            title = { Text(stringResource(R.string.staff_ms_card)) },
            text = {
                Column(Modifier.heightIn(max = 480.dp).verticalScroll(rememberScrollState())) {
                    KnitField(last, { last = it }, R.string.staff_last_name, text = true, maxLength = 80)
                    KnitField(first, { first = it }, R.string.staff_first_name, text = true, maxLength = 80)
                    KnitField(middle, { middle = it }, R.string.staff_middle_name, text = true, maxLength = 80)
                    KnitField(position, { position = it }, R.string.staff_position, text = true, maxLength = 120)
                    KnitField(phone, { phone = it }, R.string.quote_client_phone, text = true, maxLength = 40)
                    KnitField(email, { email = it.trim() }, R.string.quote_client_email, text = true, maxLength = 120)
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    vm.saveMsCard(emp.msId!!, mapOf("lastName" to last, "firstName" to first, "middleName" to middle, "position" to position, "phone" to phone, "email" to email))
                    msCard = null
                }) { Text(stringResource(R.string.products_edit_save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
            },
            dismissButton = { TextButton(onClick = { msCard = null }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
}

// ---------------------------------------------------------------- Рейтинг

/** Рейтинг за месяц: выработка, выход по графику и приход вовремя — основа премии. */
@Composable
fun RatingScreen(vm: StaffViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val me by vm.me.collectAsStateWithLifecycle()
    val rating by vm.rating.collectAsStateWithLifecycle()
    val months = remember {
        (0..2).map { back ->
            val c = Calendar.getInstance().apply { set(Calendar.DAY_OF_MONTH, 1); add(Calendar.MONTH, -back) }
            "%04d-%02d".format(c.get(Calendar.YEAR), c.get(Calendar.MONTH) + 1)
        }
    }
    var month by remember { mutableStateOf(months.first()) }
    LaunchedEffect(month) { vm.loadRating(month) }
    FormScreen(stringResource(R.string.staff_rating), onBack) {
        if (me == null) {
            Text(stringResource(R.string.staff_need_server), color = colors.textSecondary, fontSize = 14.sp)
            return@FormScreen
        }
        Message(vm)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            months.forEach { mm ->
                FilterChip(
                    selected = mm == month, onClick = { month = mm }, label = { Text(com.knit.calculator.ui.shortMonth(mm) + " " + mm.take(4)) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                )
            }
        }
        rating.orEmpty().forEachIndexed { i, r ->
            Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("${i + 1}. ${r.name}", color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                        Text("${r.score}", color = colors.textPrimary, fontSize = 20.sp, fontWeight = FontWeight.Bold)
                    }
                    androidx.compose.foundation.Canvas(Modifier.fillMaxWidth().height(6.dp)) {
                        val rr = androidx.compose.ui.geometry.CornerRadius(3.dp.toPx())
                        drawRoundRect(Color(0x3326A69A), cornerRadius = rr)
                        drawRoundRect(Color(0xFF26A69A), size = size.copy(width = size.width * (r.score / 100f).coerceIn(0.01f, 1f)), cornerRadius = rr)
                    }
                    if (r.attendance != null) {
                        Text(
                            stringResource(R.string.staff_rating_line, r.present ?: 0, r.planned ?: 0, r.punctuality ?: 0, r.output ?: 0),
                            color = colors.textSecondary, fontSize = 13.sp,
                        )
                    }
                    r.activity?.let { a ->
                        Text(stringResource(R.string.staff_rating_activity, a, hours(r.activeMinutes ?: 0)), color = colors.textSecondary, fontSize = 13.sp)
                    }
                }
            }
        }
        Text(stringResource(R.string.staff_rating_hint), color = colors.textSecondary, fontSize = 12.sp)
    }
}

/** «3 ч 25 мин». */
internal fun hours(minutes: Int): String = if (minutes >= 60) "${minutes / 60} ч ${minutes % 60} мин" else "$minutes мин"
