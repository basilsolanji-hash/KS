package com.knit.calculator.yarn

import android.content.Context
import android.os.Build
import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.OrderUnit
import com.knit.calculator.core.YarnCalculator
import com.knit.calculator.core.YarnIssue
import com.knit.calculator.core.YarnOutcome
import com.knit.calculator.core.YarnResult
import com.knit.calculator.report.ReportSharing
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.components.toastIfNeeded
import com.knit.calculator.ui.theme.LocalKnitColors
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.io.IOException
import java.math.BigDecimal

private enum class ReportAction { SHARE, EMAIL, PRINT }

@Composable
fun YarnScreen(viewModel: YarnViewModel, onBack: () -> Unit) {
    val form by viewModel.form.collectAsStateWithLifecycle()
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val clipboard = LocalClipboardManager.current
    val scope = rememberCoroutineScope()
    val defaultMain = stringResource(R.string.yarn_main)
    val outcome = remember(form, defaultMain) { viewModel.calculate(form, defaultMain) }
    val result = (outcome as? YarnOutcome.Success)?.result
    val copiedMessage = stringResource(R.string.yarn_copied)

    BackHandler(onBack = onBack)

    fun runReport(action: ReportAction, result: YarnResult) {
        val data = YarnReportData(form, result)
        scope.launch {
            val file: File? = try {
                withContext(Dispatchers.IO) { YarnReport.createPdf(context, data) }
            } catch (e: IOException) {
                null
            }
            if (file == null) {
                Toast.makeText(context, R.string.yarn_pdf_error, Toast.LENGTH_LONG).show()
                return@launch
            }
            val subject = YarnReport.subject(context, data)
            val text = YarnReport.summaryText(context, data)
            when (action) {
                ReportAction.SHARE -> ReportSharing.share(context, file, subject, text)
                ReportAction.EMAIL -> ReportSharing.email(context, file, subject, text, arrayOf(context.getString(R.string.report_email)))
                ReportAction.PRINT -> ReportSharing.print(context, file, subject)
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
        Row(
            Modifier.fillMaxWidth().height(56.dp).padding(horizontal = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = onBack) {
                Icon(painterResource(R.drawable.ic_arrow_back), stringResource(R.string.back), tint = colors.textPrimary)
            }
            Text(
                stringResource(R.string.yarn_title),
                color = colors.textPrimary,
                fontSize = 20.sp,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.weight(1f).semantics { heading() },
            )
            TextButton(onClick = viewModel::resetOrder) {
                Text(stringResource(R.string.yarn_new_order), color = colors.textSecondary)
            }
        }

        Column(
            Modifier
                .fillMaxWidth()
                .weight(1f)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            SectionTitle(R.string.yarn_section_order)
            KnitField(form.productName, { v -> viewModel.update { it.copy(productName = v) } }, R.string.yarn_product, text = true)
            KnitField(form.orderNumber, { v -> viewModel.update { it.copy(orderNumber = v) } }, R.string.yarn_order_number, text = true)
            KnitField(form.itemWeight, { v -> viewModel.update { it.copy(itemWeight = v) } }, R.string.yarn_item_weight_field)
            UnitSelector(form.orderUnit) { unit -> viewModel.update { it.copy(orderUnit = unit) } }
            KnitField(
                form.orderAmount,
                { v -> viewModel.update { it.copy(orderAmount = v) } },
                if (form.orderUnit == OrderUnit.PIECES) R.string.yarn_order_pieces_field else R.string.yarn_order_kg_field,
            )
            KnitField(form.waste, { v -> viewModel.update { it.copy(waste = v) } }, R.string.yarn_waste_field)

            SectionTitle(R.string.yarn_section_yarns)
            val extrasSum = form.extras.fold(BigDecimal.ZERO) { acc, e -> acc + (YarnCalculator.parseDecimal(e.percent) ?: BigDecimal.ZERO) }
            KnitField(
                value = form.mainName,
                onChange = { v -> viewModel.update { it.copy(mainName = v) } },
                label = R.string.yarn_name_field,
                text = true,
                placeholder = defaultMain,
            )
            Text(
                stringResource(R.string.yarn_main_hint, YarnCalculator.formatCompact(BigDecimal(100) - extrasSum, 2)),
                color = colors.textSecondary,
                fontSize = 14.sp,
            )
            form.extras.forEach { extra ->
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    KnitField(
                        value = extra.name,
                        onChange = { v -> viewModel.updateExtra(extra.id) { it.copy(name = v) } },
                        label = R.string.yarn_name_field,
                        text = true,
                        modifier = Modifier.weight(1f),
                    )
                    KnitField(
                        value = extra.percent,
                        onChange = { v -> viewModel.updateExtra(extra.id) { it.copy(percent = v) } },
                        label = R.string.yarn_percent_field,
                        modifier = Modifier.width(104.dp),
                    )
                    IconButton(onClick = { viewModel.removeExtra(extra.id) }) {
                        Icon(
                            painterResource(R.drawable.ic_close),
                            stringResource(R.string.yarn_remove, extra.name),
                            tint = colors.textSecondary,
                        )
                    }
                }
            }
            if (form.extras.isEmpty()) {
                Text(stringResource(R.string.yarn_single_color), color = colors.textSecondary, fontSize = 14.sp)
            }
            val extraName = stringResource(R.string.yarn_extra_default, form.extras.size + 1)
            val spandexName = stringResource(R.string.yarn_spandex_default)
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = { viewModel.addExtra(extraName) }) {
                    Icon(painterResource(R.drawable.ic_add), null, Modifier.size(18.dp), tint = colors.textPrimary)
                    Spacer(Modifier.width(6.dp))
                    Text(stringResource(R.string.yarn_add_extra), color = colors.textPrimary)
                }
                OutlinedButton(onClick = { viewModel.addExtra(spandexName, "5") }) {
                    Icon(painterResource(R.drawable.ic_add), null, Modifier.size(18.dp), tint = colors.textPrimary)
                    Spacer(Modifier.width(6.dp))
                    Text(stringResource(R.string.yarn_add_spandex), color = colors.textPrimary)
                }
            }

            SectionTitle(R.string.yarn_section_result)
            ResultCard(form, outcome)

            if (result != null) {
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    ActionButton(R.string.yarn_pdf, R.drawable.ic_share, primary = true, Modifier.weight(1f)) { runReport(ReportAction.SHARE, result) }
                    ActionButton(R.string.yarn_email, R.drawable.ic_email, primary = false, Modifier.weight(1f)) { runReport(ReportAction.EMAIL, result) }
                }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    ActionButton(R.string.yarn_print, R.drawable.ic_print, primary = false, Modifier.weight(1f)) { runReport(ReportAction.PRINT, result) }
                    ActionButton(R.string.yarn_copy, R.drawable.ic_copy, primary = false, Modifier.weight(1f)) {
                        clipboard.setText(AnnotatedString(YarnReport.summaryText(context, YarnReportData(form, result))))
                        toastIfNeeded(context, copiedMessage)
                    }
                }
            }
            Spacer(Modifier.height(24.dp))
        }
    }
}




@Composable
private fun UnitSelector(selected: OrderUnit, onSelect: (OrderUnit) -> Unit) {
    val colors = LocalKnitColors.current
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        listOf(OrderUnit.PIECES to R.string.yarn_unit_pieces, OrderUnit.KILOGRAMS to R.string.yarn_unit_kg).forEach { (unit, label) ->
            val isSelected = unit == selected
            Surface(
                onClick = { onSelect(unit) },
                selected = isSelected,
                shape = RoundedCornerShape(14.dp),
                color = if (isSelected) colors.equalsKey else colors.panel,
                contentColor = if (isSelected) colors.equalsKeyText else colors.textPrimary,
                modifier = Modifier.weight(1f).height(44.dp),
            ) {
                Row(horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                    Text(stringResource(label), fontWeight = if (isSelected) FontWeight.SemiBold else FontWeight.Normal)
                }
            }
        }
    }
}

@Composable
private fun ResultCard(form: YarnForm, outcome: YarnOutcome?) {
    val colors = LocalKnitColors.current
    Surface(shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            when {
                outcome == null -> Hint(R.string.yarn_invalid_number)
                form.itemWeight.isBlank() || form.orderAmount.isBlank() -> Hint(R.string.yarn_need_input)
                outcome is YarnOutcome.Invalid -> outcome.issues.sortedBy { it.ordinal }.forEach { Hint(it.message()) }
                outcome is YarnOutcome.Success -> ResultContent(outcome.result)
            }
        }
    }
}

@StringRes
private fun YarnIssue.message(): Int = when (this) {
    YarnIssue.WEIGHT_REQUIRED -> R.string.yarn_issue_weight
    YarnIssue.ORDER_REQUIRED -> R.string.yarn_issue_order
    YarnIssue.WASTE_OUT_OF_RANGE -> R.string.yarn_issue_waste
    YarnIssue.EXTRA_PERCENT_INVALID -> R.string.yarn_issue_extra
    YarnIssue.PERCENT_SUM_EXCEEDED -> R.string.yarn_issue_sum
}

@Composable
private fun Hint(@StringRes text: Int) {
    Text(stringResource(text), color = LocalKnitColors.current.textSecondary, fontSize = 15.sp)
}

@Composable
private fun ResultContent(result: YarnResult) {
    val colors = LocalKnitColors.current
    result.lines.forEach { line ->
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(line.name, color = colors.textPrimary, fontSize = 16.sp, fontWeight = FontWeight.Medium)
                Text(
                    "${YarnCalculator.formatCompact(line.percent, 2)} % · " +
                        stringResource(R.string.yarn_per_item, YarnCalculator.format(line.gramsPerItem, 1)),
                    color = colors.textSecondary,
                    fontSize = 14.sp,
                )
            }
            Text(
                stringResource(R.string.yarn_per_order, YarnCalculator.format(line.orderKg, 3)),
                color = colors.textPrimary,
                fontSize = 18.sp,
                fontWeight = FontWeight.SemiBold,
            )
        }
    }
    Spacer(Modifier.height(2.dp).fillMaxWidth().background(colors.textSecondary.copy(alpha = 0.25f)))
    Text(stringResource(R.string.yarn_total_order), color = colors.textSecondary, fontSize = 14.sp)
    Text(
        stringResource(R.string.yarn_per_order, YarnCalculator.format(result.totalKg, 3)),
        color = if (colors.isDark) colors.accent else colors.textPrimary,
        fontSize = 32.sp,
        fontWeight = FontWeight.Bold,
    )
    Text(
        stringResource(
            R.string.yarn_total_details,
            YarnCalculator.format(result.totalKgNet, 3),
            YarnCalculator.format(result.wasteKg, 3),
            YarnCalculator.formatCompact(result.pieces, 1),
        ),
        color = colors.textSecondary,
        fontSize = 14.sp,
    )
}

