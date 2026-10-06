package com.knit.calculator.staff

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
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
import java.util.Calendar

/** Профиль сотрудника: фото, личные данные, сканы документов; директору — подтверждение и доступ к выплатам. */
@Composable
fun ProfileScreen(vm: ProfileViewModel, me: StaffMe?, targetId: Int?, onPayroll: (Int) -> Unit, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val profile by vm.profile.collectAsStateWithLifecycle()
    val photo by vm.photo.collectAsStateWithLifecycle()
    val busy by vm.busy.collectAsStateWithLifecycle()
    val message by vm.message.collectAsStateWithLifecycle()
    LaunchedEffect(targetId, me?.id) { if (me != null) vm.load(targetId ?: me.id) }
    val p = profile
    val own = p != null && p.id == me?.id
    val fields = remember(p) { mutableStateMapOf<String, String>().apply { p?.fields?.let { putAll(it) } } }
    var docKind by remember { mutableStateOf<String?>(null) }
    val pickPhoto = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri -> if (uri != null && p != null) vm.photo(p.id, uri, own) }
    val pickDoc = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        val k = docKind
        if (uri != null && p != null && k != null) vm.doc(p.id, k, uri)
        docKind = null
    }

    FormScreen(stringResource(R.string.profile_title), onBack) {
        if (me == null) {
            Text(stringResource(R.string.staff_need_server), color = colors.textSecondary, fontSize = 14.sp)
            return@FormScreen
        }
        if (busy) Text(stringResource(R.string.sync_loading), color = colors.textSecondary, fontSize = 14.sp)
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        if (p == null) return@FormScreen
        Row(verticalAlignment = Alignment.CenterVertically) {
            Surface(shape = CircleShape, color = colors.panel, modifier = Modifier.size(84.dp).clickable { pickPhoto.launch("image/*") }) {
                val b = photo
                if (b != null) Image(b.asImageBitmap(), null, contentScale = ContentScale.Crop, modifier = Modifier.size(84.dp).clip(CircleShape))
                else Text(p.name.take(1), color = colors.textPrimary, fontSize = 32.sp, modifier = Modifier.padding(24.dp))
            }
            Column(Modifier.padding(start = 14.dp).weight(1f)) {
                Text(p.name, color = colors.textPrimary, fontSize = 18.sp, fontWeight = FontWeight.Bold)
                Text((me.roles[p.role] ?: p.role) + (if (p.position.isNotEmpty()) " · ${p.position}" else ""), color = colors.textSecondary, fontSize = 13.sp)
                Text(stringResource(if (p.confirmed) R.string.profile_confirmed else R.string.profile_not_confirmed),
                    color = if (p.confirmed) colors.textSecondary else androidx.compose.ui.graphics.Color(0xFFF9A825), fontSize = 13.sp)
            }
        }
        Text(stringResource(R.string.profile_photo_hint), color = colors.textSecondary, fontSize = 12.sp)
        SectionTitle(R.string.profile_data)
        PROFILE_FIELDS.forEach { (k, label) ->
            ProfileField(fields[k].orEmpty(), { fields[k] = it }, label)
        }
        ActionButton(R.string.profile_save, R.drawable.ic_cloud, primary = true, Modifier.fillMaxWidth()) { vm.save(p.id, fields.toMap()) }
        if (me.director && !own && !p.confirmed) ActionButton(R.string.profile_confirm, R.drawable.ic_add, primary = false, Modifier.fillMaxWidth()) { vm.confirm(p.id) }
        SectionTitle(R.string.profile_docs)
        Text(stringResource(R.string.profile_docs_hint), color = colors.textSecondary, fontSize = 12.sp)
        p.docs.forEach { d ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("📄 ${DOC_KINDS[d.kind] ?: d.kind} · ${d.size / 1024} КБ", color = colors.textPrimary, fontSize = 14.sp,
                    modifier = Modifier.weight(1f).clickable { vm.openDoc(d) }.padding(vertical = 6.dp))
                TextButton(onClick = { vm.deleteDoc(d.id) }) { Text("✕", color = colors.textSecondary) }
            }
        }
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            DOC_KINDS.forEach { (k, l) ->
                FilterChip(selected = false, onClick = { docKind = k; pickDoc.launch("image/*") }, label = { Text("+ $l") },
                    colors = FilterChipDefaults.filterChipColors(labelColor = colors.textPrimary))
            }
        }
        if (me.director && !own) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.profile_pay_visible), color = colors.textPrimary, modifier = Modifier.weight(1f))
                Switch(checked = p.payVisible, onCheckedChange = { vm.payVisible(p.id, it) })
            }
        }
        if (own && (p.payVisible || me.director) || !own && (me.full || me.role == "accountant")) {
            ActionButton(R.string.payroll_title, R.drawable.ic_payments, primary = false, Modifier.fillMaxWidth()) { onPayroll(p.id) }
        }
    }
}

@Composable
private fun ProfileField(value: String, onChange: (String) -> Unit, label: String) {
    val colors = LocalKnitColors.current
    androidx.compose.material3.OutlinedTextField(
        value = value, onValueChange = { onChange(it.take(300)) }, label = { Text(label) }, singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        colors = androidx.compose.material3.OutlinedTextFieldDefaults.colors(
            focusedTextColor = colors.textPrimary, unfocusedTextColor = colors.textPrimary,
            focusedLabelColor = colors.textPrimary, unfocusedLabelColor = colors.textSecondary,
        ),
    )
}

/** Соглашения при первом входе: прочитать и принять — без этого работа с сервером фабрики не начинается. */
@Composable
fun LegalScreen(vm: ProfileViewModel, onAccepted: () -> Unit, onLater: () -> Unit) {
    val colors = LocalKnitColors.current
    val docs by vm.legal.collectAsStateWithLifecycle()
    val message by vm.message.collectAsStateWithLifecycle()
    var agree by rememberSaveable { mutableStateOf(false) }
    LaunchedEffect(Unit) { vm.loadLegal() }
    FormScreen(stringResource(R.string.legal_title), onLater) {
        Text(stringResource(R.string.legal_hint), color = colors.textSecondary, fontSize = 14.sp)
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        docs.orEmpty().forEach { d ->
            Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text(d.title, color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.Bold)
                    Text(d.text, color = colors.textPrimary, fontSize = 13.sp)
                }
            }
        }
        if (docs != null) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                androidx.compose.material3.Checkbox(checked = agree, onCheckedChange = { agree = it })
                Text(stringResource(R.string.legal_agree), color = colors.textPrimary, fontSize = 14.sp)
            }
            ActionButton(R.string.legal_accept, R.drawable.ic_add, primary = agree, Modifier.fillMaxWidth()) { if (agree) vm.acceptLegal(onAccepted) }
        }
    }
}

/** Выплаты за месяц: начислено, выплачено, остаток, работа за месяц; кадрам — добавить и удалить запись. */
@Composable
fun PayrollScreen(vm: ProfileViewModel, me: StaffMe?, targetId: Int?, people: List<Person>, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val pay by vm.payroll.collectAsStateWithLifecycle()
    val message by vm.message.collectAsStateWithLifecycle()
    val months = remember {
        (0..5).map { back ->
            val c = Calendar.getInstance().apply { set(Calendar.DAY_OF_MONTH, 1); add(Calendar.MONTH, -back) }
            "%04d-%02d".format(c.get(Calendar.YEAR), c.get(Calendar.MONTH) + 1)
        }
    }
    var month by rememberSaveable { mutableStateOf(months.first()) }
    var who by rememberSaveable { mutableStateOf(targetId ?: me?.id ?: 0) }
    var adding by remember { mutableStateOf(false) }
    val hr = me?.full == true || me?.role == "accountant"
    LaunchedEffect(month, who) { if (who > 0) vm.loadPayroll(who, month) }
    val rub = { v: Double -> "%,.2f ₽".format(java.util.Locale("ru"), v).replace(' ', ' ') }
    FormScreen(stringResource(R.string.payroll_title), onBack) {
        message?.let { Text(it, color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold) }
        if (hr && people.isNotEmpty()) {
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                people.forEach { p ->
                    FilterChip(selected = p.id == who, onClick = { who = p.id }, label = { Text(p.name) },
                        colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary))
                }
            }
        }
        Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            months.forEach { m ->
                FilterChip(selected = m == month, onClick = { month = m }, label = { Text(com.knit.calculator.ui.shortMonth(m) + " " + m.take(4)) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary))
            }
        }
        val p = pay ?: return@FormScreen
        Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(3.dp)) {
                Text(stringResource(R.string.payroll_accrued, rub(p.accrued)), color = colors.textPrimary, fontSize = 15.sp)
                Text(stringResource(R.string.payroll_paid, rub(p.paid)), color = colors.textPrimary, fontSize = 15.sp)
                Text(stringResource(R.string.payroll_balance, rub(p.balance)), color = colors.textPrimary, fontSize = 17.sp, fontWeight = FontWeight.Bold)
                Text(stringResource(R.string.payroll_work, p.days, p.hours, p.stages, p.tasks), color = colors.textSecondary, fontSize = 13.sp)
            }
        }
        p.items.forEach { i ->
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("${PAY_KINDS[i.kind] ?: i.kind}: ${rub(i.amount)}", color = colors.textPrimary, fontSize = 14.sp)
                    if (i.comment.isNotEmpty()) Text(i.comment, color = colors.textSecondary, fontSize = 12.sp)
                }
                if (hr) TextButton(onClick = { vm.deletePay(i.id) }) { Text("✕", color = colors.textSecondary) }
            }
        }
        if (hr) ActionButton(R.string.payroll_add, R.drawable.ic_add, primary = false, Modifier.fillMaxWidth()) { adding = true }
    }
    if (adding) {
        var kind by remember { mutableStateOf("salary") }
        var amount by remember { mutableStateOf("") }
        var comment by remember { mutableStateOf("") }
        AlertDialog(
            onDismissRequest = { adding = false },
            title = { Text(stringResource(R.string.payroll_add)) },
            text = {
                Column {
                    Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        PAY_KINDS.forEach { (k, l) ->
                            FilterChip(selected = k == kind, onClick = { kind = k }, label = { Text(l) },
                                colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary))
                        }
                    }
                    KnitField(amount, { amount = it }, R.string.payroll_amount, suffix = "₽")
                    KnitField(comment, { comment = it }, R.string.orders_comment, text = true, maxLength = 300)
                }
            },
            confirmButton = {
                TextButton(onClick = {
                    amount.replace(',', '.').toDoubleOrNull()?.takeIf { it > 0 }?.let { vm.addPay(who, month, kind, it, comment.trim()); adding = false }
                }) { Text(stringResource(R.string.shortcuts_save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
            },
            dismissButton = { TextButton(onClick = { adding = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
}
