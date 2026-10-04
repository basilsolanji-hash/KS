package com.knit.calculator.quote

import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.OptionGroup
import com.knit.calculator.core.Product
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.core.QuoteTotals
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.report.ReportSharing
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.components.toastIfNeeded
import com.knit.calculator.ui.theme.LocalKnitColors
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.io.IOException

private enum class QuoteAction { SHARE, EMAIL, PRINT }

@Composable
fun QuoteScreen(
    viewModel: QuoteViewModel,
    onBack: () -> Unit,
    onOpenCatalog: () -> Unit,
    onOpenCompany: () -> Unit,
) {
    val draft by viewModel.draft.collectAsStateWithLifecycle()
    val catalog by viewModel.catalog.collectAsStateWithLifecycle()
    val settings by viewModel.settings.collectAsStateWithLifecycle()
    val views = remember(draft, catalog) { viewModel.lineViews(draft, catalog) }
    val totals = remember(views, settings) { viewModel.totals(views, settings) }
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val clipboard = LocalClipboardManager.current
    val scope = rememberCoroutineScope()
    val copiedMessage = stringResource(R.string.quote_copied)
    var confirmNew by rememberSaveable { mutableStateOf(false) }

    BackHandler(onBack = onBack)

    fun document() = QuoteDocument(settings, draft, totals)

    fun run(action: QuoteAction) {
        val doc = document()
        scope.launch {
            val file: File? = try {
                withContext(Dispatchers.IO) { QuotePdf.create(context, doc) }
            } catch (e: IOException) {
                null
            }
            if (file == null) {
                Toast.makeText(context, R.string.yarn_pdf_error, Toast.LENGTH_LONG).show()
                return@launch
            }
            val subject = QuotePdf.subject(context, doc)
            val text = QuotePdf.emailText(context, doc)
            when (action) {
                QuoteAction.SHARE -> ReportSharing.share(context, file, subject, text)
                QuoteAction.EMAIL -> ReportSharing.email(
                    context, file, subject, text,
                    listOf(doc.draft.clientEmail.trim()).filter { it.isNotEmpty() }.toTypedArray(),
                )
                QuoteAction.PRINT -> ReportSharing.print(context, file, subject)
            }
        }
    }

    Column(
        Modifier
            .fillMaxSize()
            .background(colors.background)
            .safeDrawingPadding()
            .imePadding(),
    ) {
        ScreenTopBar(stringResource(R.string.quote_title, draft.number), onBack) {
            KnitIconButton(R.drawable.ic_list, stringResource(R.string.quote_catalog), onOpenCatalog)
            KnitIconButton(R.drawable.ic_settings, stringResource(R.string.quote_company), onOpenCompany)
        }

        Column(
            Modifier
                .fillMaxWidth()
                .weight(1f)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            SectionTitle(R.string.quote_section_client)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                KnitField(
                    value = if (draft.number > 0) draft.number.toString() else "",
                    onChange = { v -> viewModel.updateDraft { it.copy(number = v.filter(Char::isDigit).take(7).toIntOrNull() ?: 0) } },
                    label = R.string.quote_number,
                    keyboardType = KeyboardType.Number,
                    modifier = Modifier.width(120.dp),
                )
                KnitField(
                    value = draft.clientCompany,
                    onChange = { v -> viewModel.updateDraft { it.copy(clientCompany = v) } },
                    label = R.string.quote_client_company,
                    text = true,
                    maxLength = 120,
                    modifier = Modifier.weight(1f),
                )
            }
            KnitField(draft.clientContact, { v -> viewModel.updateDraft { it.copy(clientContact = v) } }, R.string.quote_client_contact, text = true, maxLength = 120)
            KnitField(
                draft.clientEmail, { v -> viewModel.updateDraft { it.copy(clientEmail = v.trim()) } }, R.string.quote_client_email,
                text = true, keyboardType = KeyboardType.Email, maxLength = 120,
            )

            SectionTitle(R.string.quote_section_items)
            if (views.isEmpty()) {
                Text(
                    stringResource(if (catalog.isEmpty()) R.string.quote_empty_catalog else R.string.quote_no_items),
                    color = colors.textSecondary,
                    fontSize = 14.sp,
                )
            }
            views.forEachIndexed { index, view ->
                LineCard(
                    index = index,
                    view = view,
                    onQuantity = { q -> viewModel.updateLine(view.draft.id) { it.copy(quantity = q) } },
                    onSelect = { groupId, choiceId ->
                        viewModel.updateLine(view.draft.id) { it.copy(selected = it.selected + (groupId to choiceId)) }
                    },
                    onRemove = { viewModel.removeLine(view.draft.id) },
                )
            }
            AddItemButton(catalog, onAdd = viewModel::addLine, onOpenCatalog = onOpenCatalog)

            KnitField(
                draft.comment, { v -> viewModel.updateDraft { it.copy(comment = v) } }, R.string.quote_comment,
                text = true, singleLine = false, maxLength = 500,
            )

            SectionTitle(R.string.quote_section_total)
            TotalsCard(totals, settings)

            if (totals.lines.isNotEmpty()) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    ActionButton(R.string.yarn_pdf, R.drawable.ic_share, primary = true, Modifier.weight(1f)) { run(QuoteAction.SHARE) }
                    ActionButton(R.string.yarn_email, R.drawable.ic_email, primary = false, Modifier.weight(1f)) { run(QuoteAction.EMAIL) }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    ActionButton(R.string.yarn_print, R.drawable.ic_print, primary = false, Modifier.weight(1f)) { run(QuoteAction.PRINT) }
                    ActionButton(R.string.yarn_copy, R.drawable.ic_copy, primary = false, Modifier.weight(1f)) {
                        clipboard.setText(AnnotatedString(QuotePdf.emailText(context, document())))
                        toastIfNeeded(context, copiedMessage)
                    }
                }
            }
            TextButton(onClick = { confirmNew = true }, modifier = Modifier.align(Alignment.CenterHorizontally)) {
                Text(stringResource(R.string.quote_new), color = colors.textSecondary)
            }
            Spacer(Modifier.height(24.dp))
        }
    }

    if (confirmNew) {
        ConfirmDialog(
            title = stringResource(R.string.quote_new_title),
            text = stringResource(R.string.quote_new_text),
            confirm = stringResource(R.string.quote_new),
            onConfirm = { confirmNew = false; viewModel.newQuote() },
            onDismiss = { confirmNew = false },
        )
    }
}

@Composable
fun ConfirmDialog(title: String, text: String, confirm: String, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    val colors = LocalKnitColors.current
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(title) },
        text = { Text(text) },
        confirmButton = {
            TextButton(onClick = onConfirm) { Text(confirm, color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel), color = colors.textSecondary) }
        },
        containerColor = colors.panel,
        titleContentColor = colors.textPrimary,
        textContentColor = colors.textSecondary,
    )
}

@Composable
private fun LineCard(
    index: Int,
    view: DraftLineView,
    onQuantity: (String) -> Unit,
    onSelect: (Long, Long) -> Unit,
    onRemove: () -> Unit,
) {
    val colors = LocalKnitColors.current
    val product = view.product
    val line = view.line
    Surface(shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(start = 16.dp, end = 8.dp, top = 8.dp, bottom = 14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    "${index + 1}. ${product.name}",
                    color = colors.textPrimary,
                    fontSize = 17.sp,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
                )
                IconButton(onClick = onRemove) {
                    Icon(painterResource(R.drawable.ic_close), stringResource(R.string.quote_remove_item), tint = colors.textSecondary)
                }
            }
            product.options.forEach { group ->
                OptionSelector(group, view.draft.selected[group.id], onSelect = { onSelect(group.id, it) })
            }
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(end = 8.dp)) {
                KnitField(
                    value = view.draft.quantity,
                    onChange = onQuantity,
                    label = R.string.quote_quantity,
                    suffix = product.unit,
                    modifier = Modifier.width(170.dp),
                )
                Spacer(Modifier.weight(1f))
                Text(
                    if (line != null) stringResource(R.string.quote_line_total, QuoteCalculator.formatMoney(line.total)) else "",
                    color = if (colors.isDark) colors.accent else colors.textPrimary,
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                )
            }
            val details = if (line == null) {
                stringResource(R.string.quote_enter_quantity)
            } else {
                buildList {
                    add(stringResource(R.string.quote_price_line, QuoteCalculator.formatMoney(line.unitPrice), product.unit))
                    if (line.discountPercent.signum() > 0) add(stringResource(R.string.quote_discount, YarnCalculator.formatCompact(line.discountPercent, 2)))
                    if (line.setupFee.signum() > 0) add(stringResource(R.string.quote_setup, QuoteCalculator.formatMoney(line.setupFee)))
                }.joinToString(" · ")
            }
            Text(details, color = colors.textSecondary, fontSize = 14.sp)
            if (line?.belowMinimum == true) {
                Text(
                    stringResource(R.string.quote_below_min, QuoteCalculator.formatQuantity(product.minOrder), product.unit),
                    color = colors.textPrimary,
                    fontSize = 14.sp,
                    fontWeight = FontWeight.SemiBold,
                )
            }
        }
    }
}

@Composable
private fun OptionSelector(group: OptionGroup, selectedId: Long?, onSelect: (Long) -> Unit) {
    val colors = LocalKnitColors.current
    var expanded by remember { mutableStateOf(false) }
    val selected = group.choices.firstOrNull { it.id == selectedId } ?: group.choices.firstOrNull() ?: return
    Box(Modifier.padding(end = 8.dp)) {
        Surface(
            onClick = { expanded = true },
            shape = RoundedCornerShape(14.dp),
            color = colors.background,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Row(Modifier.padding(horizontal = 14.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(group.name, color = colors.textSecondary, fontSize = 12.sp)
                    Text(choiceLabel(selected.name, selected.priceAdd), color = colors.textPrimary, fontSize = 16.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                Icon(painterResource(R.drawable.ic_dropdown), null, tint = colors.textSecondary)
            }
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }, containerColor = colors.panel) {
            group.choices.forEach { choice ->
                DropdownMenuItem(
                    text = {
                        Text(
                            choiceLabel(choice.name, choice.priceAdd),
                            color = colors.textPrimary,
                            fontWeight = if (choice.id == selected.id) FontWeight.SemiBold else FontWeight.Normal,
                        )
                    },
                    onClick = {
                        expanded = false
                        onSelect(choice.id)
                    },
                )
            }
        }
    }
}

private fun choiceLabel(name: String, add: java.math.BigDecimal): String = when {
    add.signum() > 0 -> "$name  (+${QuoteCalculator.formatMoney(add)} ₽)"
    add.signum() < 0 -> "$name  (${QuoteCalculator.formatMoney(add)} ₽)"
    else -> name
}

@Composable
private fun AddItemButton(catalog: List<Product>, onAdd: (Product) -> Unit, onOpenCatalog: () -> Unit) {
    val colors = LocalKnitColors.current
    var expanded by remember { mutableStateOf(false) }
    Box {
        OutlinedButton(onClick = { if (catalog.isEmpty()) onOpenCatalog() else expanded = true }) {
            Icon(painterResource(R.drawable.ic_add), null, Modifier.size(18.dp), tint = colors.textPrimary)
            Spacer(Modifier.width(6.dp))
            Text(stringResource(R.string.quote_add_item), color = colors.textPrimary)
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }, containerColor = colors.panel) {
            catalog.forEach { product ->
                DropdownMenuItem(
                    text = {
                        Column {
                            Text(product.name, color = colors.textPrimary, fontWeight = FontWeight.Medium)
                            Text(
                                stringResource(R.string.quote_price_line, QuoteCalculator.formatMoney(product.basePrice), product.unit),
                                color = colors.textSecondary,
                                fontSize = 13.sp,
                            )
                        }
                    },
                    onClick = {
                        expanded = false
                        onAdd(product)
                    },
                )
            }
        }
    }
}

@Composable
private fun TotalsCard(totals: QuoteTotals, settings: CompanySettings) {
    val colors = LocalKnitColors.current
    val rate = YarnCalculator.formatCompact(settings.vat().ratePercent, 2)
    Surface(shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            if (!settings.vatIncluded) {
                TotalRow(stringResource(R.string.quote_sum_without_vat), QuoteCalculator.formatMoney(totals.subtotal))
                TotalRow(stringResource(R.string.quote_vat, rate), QuoteCalculator.formatMoney(totals.vat))
            }
            Text(stringResource(R.string.quote_total), color = colors.textSecondary, fontSize = 14.sp)
            Text(
                stringResource(R.string.quote_money, QuoteCalculator.formatMoney(totals.total)),
                color = if (colors.isDark) colors.accent else colors.textPrimary,
                fontSize = 30.sp,
                fontWeight = FontWeight.Bold,
            )
            if (settings.vatIncluded) {
                TotalRow(stringResource(R.string.quote_vat_included, rate), QuoteCalculator.formatMoney(totals.vat))
            }
        }
    }
}

@Composable
private fun TotalRow(label: String, value: String) {
    val colors = LocalKnitColors.current
    Row {
        Text(label, color = colors.textSecondary, fontSize = 15.sp, modifier = Modifier.weight(1f))
        Text(stringResource(R.string.quote_money, value), color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Medium)
    }
}
