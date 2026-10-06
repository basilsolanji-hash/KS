package com.knit.calculator.quote

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
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
import com.knit.calculator.core.WorkShift
import com.knit.calculator.core.WorkTime
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Date
import java.util.Locale

/**
 * Рабочее время: итоги за месяц по сотрудникам (директор — все, сотрудник — свои) и смены по дням.
 * Незакрытые смены — красным; директор закрывает их, указав время конца.
 */
@Composable
fun WorkTimeScreen(vm: WorkViewModel, director: Boolean, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val team by vm.team.collectAsStateWithLifecycle()
    val error by vm.error.collectAsStateWithLifecycle()
    val now = remember { System.currentTimeMillis() }
    val months = remember {
        (0..2).map { back -> Calendar.getInstance().apply { timeInMillis = now; set(Calendar.DAY_OF_MONTH, 1); add(Calendar.MONTH, -back) }.timeInMillis }
            .map { WorkTime.monthKey(it) }
    }
    var month by remember { mutableStateOf(months.first()) }
    var person by remember { mutableStateOf<String?>(null) }
    var fixing by remember { mutableStateOf<WorkShift?>(null) }
    LaunchedEffect(month) { vm.loadTeam(month) }
    val shifts = team.orEmpty()
    val totals = remember(shifts, month) { WorkTime.totals(shifts, month, System.currentTimeMillis()) }
    val day = SimpleDateFormat("EE d MMM", Locale("ru"))
    val hm = SimpleDateFormat("HH:mm", Locale.getDefault())

    FormScreen(stringResource(R.string.home_worktime), onBack) {
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            months.forEach { m ->
                FilterChip(
                    selected = m == month, onClick = { month = m; person = null },
                    label = { Text(com.knit.calculator.ui.shortMonth(m) + " " + m.take(4)) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                )
            }
        }
        error?.let { Text(it, color = colors.textSecondary, fontSize = 13.sp) }
        if (team != null && totals.isEmpty()) Text(stringResource(R.string.work_empty), color = colors.textSecondary, fontSize = 14.sp)
        totals.forEach { t ->
            Surface(onClick = { person = if (person == t.person) null else t.person }, shape = RoundedCornerShape(16.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(t.person, color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                        Text(WorkTime.format(t.minutes), color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.Bold)
                    }
                    Text(
                        stringResource(R.string.work_days, t.days) + if (t.unclosed > 0) " · " + stringResource(R.string.work_unclosed, t.unclosed) else "",
                        color = if (t.unclosed > 0) Color(0xFFD32F2F) else colors.textSecondary, fontSize = 13.sp,
                    )
                    if (person == t.person) {
                        shifts.filter { it.person == t.person && WorkTime.monthKey(it.start) == month }.sortedByDescending { it.start }.forEach { s ->
                            val unclosed = s.open && s.start < WorkTime.dayStart(System.currentTimeMillis())
                            Row(Modifier.padding(top = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                                Text(
                                    day.format(Date(s.start)) + ", " + hm.format(Date(s.start)) + "–" + (s.end?.let { hm.format(Date(it)) } ?: "…") +
                                        " · " + WorkTime.format(WorkTime.minutes(s, System.currentTimeMillis())) +
                                        (if (s.suspicious) " · ⚠ " + stringResource(R.string.work_suspicious) else ""),
                                    color = if (unclosed || s.suspicious) Color(0xFFD32F2F) else colors.textPrimary, fontSize = 14.sp, modifier = Modifier.weight(1f),
                                )
                                if (director && unclosed) TextButton(onClick = { fixing = s }) { Text(stringResource(R.string.work_fix), color = colors.textPrimary) }
                            }
                        }
                    }
                }
            }
        }
        Text(stringResource(R.string.work_hint), color = colors.textSecondary, fontSize = 12.sp)
    }

    fixing?.let { s ->
        var time by remember { mutableStateOf("18:00") }
        val valid = Regex("^([01]?\\d|2[0-3]):[0-5]\\d$").matches(time.trim())
        AlertDialog(
            onDismissRequest = { fixing = null },
            title = { Text(stringResource(R.string.work_fix_title, s.person, day.format(Date(s.start)))) },
            text = {
                Column {
                    Text(stringResource(R.string.work_fix_hint, hm.format(Date(s.start))), color = colors.textSecondary, fontSize = 13.sp)
                    KnitField(time, { time = it.take(5) }, R.string.work_fix_end, text = true, maxLength = 5)
                }
            },
            confirmButton = {
                TextButton(enabled = valid, onClick = {
                    val (h, m) = time.trim().split(':').map { it.toInt() }
                    val end = Calendar.getInstance().apply { timeInMillis = s.start; set(Calendar.HOUR_OF_DAY, h); set(Calendar.MINUTE, m) }.timeInMillis
                    if (end > s.start) vm.fixShift(s, end, month)
                    fixing = null
                }) { Text(stringResource(R.string.shortcuts_save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
            },
            dismissButton = { TextButton(onClick = { fixing = null }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
}
