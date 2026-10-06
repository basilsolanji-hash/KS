package com.knit.calculator.staff

import android.app.DatePickerDialog
import android.app.TimePickerDialog
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
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
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

private val TASK_RED = Color(0xFFD32F2F)
private val STATUS_COLOR = mapOf(
    "new" to Color(0xFF1E88E5), "work" to Color(0xFFF9A825), "done" to Color(0xFF43A047), "accepted" to Color(0xFF607D8B), "cancelled" to Color(0xFF9E9E9E),
)
private val dt = SimpleDateFormat("d MMM HH:mm", Locale("ru"))

/** Задачи: «Мне», «Я поставил», «Все» (руководство); новая задача; карточка с чек-листом, комментариями и файлами. */
@Composable
fun TasksScreen(vm: TasksViewModel, me: StaffMe?, openId: Int? = null, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val list by vm.list.collectAsStateWithLifecycle()
    val card by vm.card.collectAsStateWithLifecycle()
    val busy by vm.busy.collectAsStateWithLifecycle()
    val message by vm.message.collectAsStateWithLifecycle()
    var scope by rememberSaveable { mutableStateOf("mine") }
    var closed by rememberSaveable { mutableStateOf(false) }
    var editing by remember { mutableStateOf<TaskItem?>(null) }
    LaunchedEffect(scope, closed) { vm.load(scope, closed) }
    LaunchedEffect(openId) { openId?.let { vm.open(it) } }
    LaunchedEffect(Unit) { vm.loadPeople() }
    // Уведомления о задачах: на Android 13+ нужно разрешение — спрашиваем при первом открытии задач.
    val context = LocalContext.current
    val askNotify = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {}
    LaunchedEffect(Unit) {
        if (android.os.Build.VERSION.SDK_INT >= 33 && androidx.core.content.ContextCompat.checkSelfPermission(context, android.Manifest.permission.POST_NOTIFICATIONS) !=
            android.content.pm.PackageManager.PERMISSION_GRANTED
        ) askNotify.launch(android.Manifest.permission.POST_NOTIFICATIONS)
    }

    editing?.let { t -> TaskEditor(vm, me, t, onDone = { editing = null; vm.load(scope, closed) }); return }
    card?.let { c -> TaskDetail(vm, me, c, onEdit = { editing = c.task }, onBack = { vm.close(); vm.load(scope, closed) }); return }

    FormScreen(stringResource(R.string.tasks_title), onBack) {
        if (!vm.connected || me == null) {
            Text(stringResource(R.string.staff_need_server), color = colors.textSecondary, fontSize = 14.sp)
            return@FormScreen
        }
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            listOfNotNull("mine" to R.string.tasks_mine, "created" to R.string.tasks_created, if (me.full) "all" to R.string.tasks_all else null).forEach { (k, l) ->
                FilterChip(selected = scope == k, onClick = { scope = k }, label = { Text(stringResource(l)) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary))
            }
            FilterChip(selected = closed, onClick = { closed = !closed }, label = { Text(stringResource(R.string.tasks_closed)) },
                colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary))
        }
        ActionButton(R.string.tasks_new, R.drawable.ic_add, primary = true, Modifier.fillMaxWidth()) {
            editing = TaskItem(0, "", "", me.id, me.name, me.id, me.name, "new", false, null, null, "", "", "", "", emptyList(), 0, false)
        }
        if (busy) Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        list?.let { if (it.isEmpty()) Text(stringResource(R.string.tasks_empty), color = colors.textSecondary, fontSize = 14.sp) }
        list.orEmpty().forEach { t -> TaskRow(t) { vm.open(t.id) } }
    }
}

@Composable
private fun TaskRow(t: TaskItem, onClick: () -> Unit) {
    val colors = LocalKnitColors.current
    Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth().clickable(onClick = onClick)) {
        Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                if (t.priority) Text("! ", color = TASK_RED, fontSize = 16.sp, fontWeight = FontWeight.Bold)
                Text(t.title, color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                StatusBadge(t.status)
            }
            Text("${t.author} → ${t.assignee}", color = colors.textSecondary, fontSize = 13.sp)
            val done = t.checklist.count { it.done }
            val parts = listOfNotNull(
                t.due?.let { (if (t.late) "просрочено: " else "срок: ") + dt.format(Date(it)) },
                if (t.checklist.isNotEmpty()) "$done/${t.checklist.size}" else null,
                TASK_REPEAT[t.repeat]?.takeIf { t.repeat.isNotEmpty() },
            )
            if (parts.isNotEmpty()) Text(parts.joinToString(" · "), color = if (t.late) TASK_RED else colors.textSecondary, fontSize = 13.sp)
        }
    }
}

@Composable
private fun StatusBadge(status: String) {
    Surface(shape = RoundedCornerShape(6.dp), color = STATUS_COLOR[status] ?: Color.Gray) {
        Text(TASK_STATUS[status] ?: status, color = Color.White, fontSize = 12.sp, modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp))
    }
}

@Composable
private fun TaskDetail(vm: TasksViewModel, me: StaffMe?, card: TaskCard, onEdit: () -> Unit, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val t = card.task
    val message by vm.message.collectAsStateWithLifecycle()
    var comment by rememberSaveable(t.id) { mutableStateOf("") }
    val pick = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri -> uri?.let { vm.attach(t.id, it) } }
    val mine = me?.id == t.assigneeId
    val author = me?.id == t.authorId || me?.full == true
    FormScreen(t.title, onBack) {
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        Row(verticalAlignment = Alignment.CenterVertically) {
            StatusBadge(t.status)
            if (t.priority) Text("  срочно", color = TASK_RED, fontSize = 14.sp, fontWeight = FontWeight.Bold)
        }
        Text("${t.author} → ${t.assignee}", color = colors.textPrimary, fontSize = 15.sp)
        t.due?.let { Text((if (t.late) "Просрочено, срок был " else "Срок: ") + dt.format(Date(it)), color = if (t.late) TASK_RED else colors.textSecondary, fontSize = 14.sp) }
        if (t.repeat.isNotEmpty()) Text(TASK_REPEAT[t.repeat].orEmpty(), color = colors.textSecondary, fontSize = 13.sp)
        if (t.linkTitle.isNotEmpty()) Text(stringResource(R.string.tasks_link_value, t.linkTitle), color = colors.textSecondary, fontSize = 13.sp)
        if (t.body.isNotEmpty()) Text(t.body, color = colors.textPrimary, fontSize = 15.sp)
        if (t.checklist.isNotEmpty()) {
            SectionTitle(R.string.tasks_checklist)
            t.checklist.forEachIndexed { i, item ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(checked = item.done, onCheckedChange = { vm.check(t.id, i, it) }, enabled = mine || author)
                    Text(item.text, color = colors.textPrimary, fontSize = 14.sp)
                }
            }
        }
        // Шаги по статусу: исполнитель — «в работу», «сделано»; автор — «принять», «вернуть», «отменить».
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            if (mine && t.status == "new") ActionButton(R.string.tasks_to_work, R.drawable.ic_arrow_down, primary = true, Modifier.weight(1f)) { vm.status(t.id, "work") }
            if (mine && t.status in listOf("new", "work")) ActionButton(R.string.tasks_done, R.drawable.ic_add, primary = true, Modifier.weight(1f)) { vm.status(t.id, "done") }
            if (author && t.status == "done") {
                ActionButton(R.string.tasks_accept, R.drawable.ic_add, primary = true, Modifier.weight(1f)) { vm.status(t.id, "accepted") }
                ActionButton(R.string.tasks_return, R.drawable.ic_arrow_down, primary = false, Modifier.weight(1f)) { vm.status(t.id, "work") }
            }
        }
        if (author && t.status in listOf("new", "work", "done")) {
            Row {
                TextButton(onClick = onEdit) { Text(stringResource(R.string.tasks_edit), color = colors.textPrimary) }
                TextButton(onClick = { vm.status(t.id, "cancelled") }) { Text(stringResource(R.string.tasks_cancel), color = TASK_RED) }
            }
        }
        SectionTitle(R.string.tasks_files)
        card.files.forEach { f ->
            Text("📎 ${f.name} · ${f.size / 1024} КБ · ${f.who}", color = colors.textPrimary, fontSize = 14.sp,
                modifier = Modifier.fillMaxWidth().clickable { vm.openFile(f) }.padding(vertical = 4.dp))
        }
        ActionButton(R.string.tasks_attach, R.drawable.ic_add, primary = false, Modifier.fillMaxWidth()) { pick.launch("*/*") }
        SectionTitle(R.string.tasks_comments)
        card.comments.forEach { c ->
            Column(Modifier.padding(vertical = 2.dp)) {
                Text("${c.who} · ${dt.format(Date(c.time))}", color = colors.textSecondary, fontSize = 12.sp)
                Text(c.text, color = colors.textPrimary, fontSize = 14.sp)
            }
        }
        KnitField(comment, { comment = it }, R.string.tasks_comment_hint, text = true, maxLength = 2000, singleLine = false)
        ActionButton(R.string.tasks_send, R.drawable.ic_share, primary = false, Modifier.fillMaxWidth()) {
            if (comment.isNotBlank()) vm.comment(t.id, comment.trim()) { comment = "" }
        }
    }
}

@Composable
private fun TaskEditor(vm: TasksViewModel, me: StaffMe?, start: TaskItem, onDone: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val people by vm.people.collectAsStateWithLifecycle()
    val busy by vm.busy.collectAsStateWithLifecycle()
    val message by vm.message.collectAsStateWithLifecycle()
    var title by remember { mutableStateOf(start.title) }
    var body by remember { mutableStateOf(start.body) }
    var assignee by remember { mutableStateOf(start.assigneeId) }
    var priority by remember { mutableStateOf(start.priority) }
    var due by remember { mutableStateOf(start.due) }
    var remindHours by remember { mutableStateOf(if (start.due != null && start.remind != null) ((start.due - start.remind) / 3_600_000).toString() else "1") }
    var repeat by remember { mutableStateOf(start.repeat) }
    var link by remember { mutableStateOf(start.linkTitle) }
    val checklist = remember { mutableStateListOf<CheckItem>().apply { addAll(start.checklist) } }
    var newItem by remember { mutableStateOf("") }
    var choosing by remember { mutableStateOf(false) }

    fun pickDue() {
        val c = Calendar.getInstance().apply { due?.let { timeInMillis = it } ?: add(Calendar.DAY_OF_YEAR, 1) }
        DatePickerDialog(context, { _, y, m, d ->
            TimePickerDialog(context, { _, h, min ->
                due = Calendar.getInstance().apply { set(y, m, d, h, min, 0); set(Calendar.MILLISECOND, 0) }.timeInMillis
            }, c.get(Calendar.HOUR_OF_DAY).takeIf { due != null } ?: 18, c.get(Calendar.MINUTE).takeIf { due != null } ?: 0, true).show()
        }, c.get(Calendar.YEAR), c.get(Calendar.MONTH), c.get(Calendar.DAY_OF_MONTH)).show()
    }

    FormScreen(stringResource(if (start.id > 0) R.string.tasks_edit else R.string.tasks_new), onDone) {
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        KnitField(title, { title = it }, R.string.tasks_name, text = true, maxLength = 200)
        KnitField(body, { body = it }, R.string.tasks_body, text = true, maxLength = 5000, singleLine = false)
        val who = people.firstOrNull { it.id == assignee }?.name ?: if (assignee == me?.id) me.name else "—"
        Surface(shape = RoundedCornerShape(12.dp), color = colors.panel, modifier = Modifier.fillMaxWidth().clickable { choosing = !choosing }) {
            Text(stringResource(R.string.tasks_assignee, who), color = colors.textPrimary, fontSize = 15.sp, modifier = Modifier.padding(14.dp))
        }
        if (choosing) {
            people.forEach { p ->
                Text(p.name + (if (p.id == me?.id) " (я)" else ""), color = if (p.id == assignee) colors.accent else colors.textPrimary, fontSize = 15.sp,
                    modifier = Modifier.fillMaxWidth().clickable { assignee = p.id; choosing = false }.padding(horizontal = 14.dp, vertical = 8.dp))
            }
        }
        Surface(shape = RoundedCornerShape(12.dp), color = colors.panel, modifier = Modifier.fillMaxWidth().clickable { pickDue() }) {
            Text(due?.let { stringResource(R.string.tasks_due_value, dt.format(Date(it))) } ?: stringResource(R.string.tasks_due_pick),
                color = colors.textPrimary, fontSize = 15.sp, modifier = Modifier.padding(14.dp))
        }
        if (due != null) {
            KnitField(remindHours, { remindHours = it.filter(Char::isDigit).take(3) }, R.string.tasks_remind, suffix = "ч")
            TextButton(onClick = { due = null }) { Text(stringResource(R.string.tasks_no_due), color = colors.textSecondary) }
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.tasks_priority), color = colors.textPrimary, modifier = Modifier.weight(1f))
            Switch(checked = priority, onCheckedChange = { priority = it })
        }
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            TASK_REPEAT.forEach { (k, l) ->
                FilterChip(selected = repeat == k, onClick = { repeat = k }, label = { Text(l) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary))
            }
        }
        KnitField(link, { link = it }, R.string.tasks_link, text = true, maxLength = 200)
        SectionTitle(R.string.tasks_checklist)
        checklist.forEachIndexed { i, item ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("• ${item.text}", color = colors.textPrimary, fontSize = 14.sp, modifier = Modifier.weight(1f))
                TextButton(onClick = { checklist.removeAt(i) }) { Text("✕", color = colors.textSecondary) }
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            KnitField(newItem, { newItem = it }, R.string.tasks_check_item, text = true, maxLength = 200, modifier = Modifier.weight(1f))
            TextButton(onClick = { if (newItem.isNotBlank()) { checklist.add(CheckItem(newItem.trim(), false)); newItem = "" } }) { Text("+", fontSize = 22.sp, color = colors.textPrimary) }
        }
        ActionButton(R.string.tasks_save, R.drawable.ic_cloud, primary = true, Modifier.fillMaxWidth()) {
            if (title.isBlank() || busy) return@ActionButton
            val d = due
            val remind = d?.let { it - (remindHours.toLongOrNull() ?: 1) * 3_600_000 }
            val name = people.firstOrNull { it.id == assignee }?.name ?: who
            vm.save(start.copy(title = title.trim(), body = body.trim(), assigneeId = assignee, assignee = name, priority = priority, due = d,
                remind = remind, repeat = repeat, linkTitle = link.trim(), checklist = checklist.toList()), onDone)
        }
    }
}
