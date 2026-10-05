package com.knit.calculator.label

import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
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
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.Ean13
import com.knit.calculator.core.LabelPrinter
import com.knit.calculator.core.Labels
import com.knit.calculator.core.PrinterLanguage
import com.knit.calculator.quote.CompanySettings
import com.knit.calculator.quote.FormScreen
import com.knit.calculator.quote.ProductPicker
import com.knit.calculator.quote.QuoteViewModel
import com.knit.calculator.ui.components.ActionButton
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Текст этикетки по товару и реквизитам фабрики. */
fun labelContent(job: LabelJob, settings: CompanySettings, madeDate: String) = Labels.content(
    product = job.product,
    brand = settings.brand,
    website = settings.website,
    packQuantity = job.pack,
    madeDate = madeDate,
    makerName = settings.legalName,
    makerInn = settings.inn,
    makerAddress = settings.factAddress,
)

/**
 * Этикетки со штрихкодом EAN-13 для товаров МойСклад: очередь (с экрана или из заказа), предпросмотр 120×75 мм,
 * печать на термопринтер по Bluetooth (TSPL / ZPL).
 */
@Composable
fun LabelScreen(quoteVm: QuoteViewModel, labelVm: LabelViewModel, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val jobs by labelVm.jobs.collectAsStateWithLifecycle()
    val printer by labelVm.printer.collectAsStateWithLifecycle()
    val madeDate by labelVm.madeDate.collectAsStateWithLifecycle()
    val settings by quoteVm.settings.collectAsStateWithLifecycle()
    val sync by quoteVm.sync.collectAsStateWithLifecycle()
    val msProducts by quoteVm.msProducts.collectAsStateWithLifecycle()
    val msFilters by quoteVm.msFilters.collectAsStateWithLifecycle()
    var picker by remember { mutableStateOf(false) }
    var choosePrinter by remember { mutableStateOf(false) }
    var previewIndex by remember { mutableIntStateOf(0) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) choosePrinter = true else error = context.getString(R.string.label_bt_permission)
    }
    LaunchedEffect(Unit) { quoteVm.refreshMs(maxAgeMs = 3_600_000L) }

    fun openPrinters() {
        val p = BluetoothPrinter.permission
        if (p != null && !BluetoothPrinter.hasPermission(context)) permission.launch(p) else choosePrinter = true
    }

    FormScreen(stringResource(R.string.label_title), onBack) {
        if (!sync.msEnabled && msProducts.isEmpty()) {
            Text(stringResource(R.string.label_need_ms), color = colors.textSecondary, fontSize = 14.sp)
        }
        ActionButton(R.string.label_add, R.drawable.ic_add, primary = jobs.isEmpty(), Modifier.fillMaxWidth()) { picker = true }

        // Предпросмотр выбранной этикетки — точно так, как уйдёт на принтер.
        val shown = jobs.getOrNull(previewIndex) ?: jobs.firstOrNull()
        if (shown != null) {
            val bitmap = remember(shown, settings, madeDate, printer.spec) {
                LabelRenderer.render(labelContent(shown, settings, madeDate), printer.spec)
            }
            Surface(shape = RoundedCornerShape(8.dp), color = Color.White, modifier = Modifier.fillMaxWidth()) {
                Image(
                    bitmap.asImageBitmap(),
                    contentDescription = stringResource(R.string.label_preview),
                    modifier = Modifier.fillMaxWidth().aspectRatio(bitmap.width.toFloat() / bitmap.height).border(1.dp, colors.textSecondary).testTag("labelPreview"),
                )
            }
        }

        jobs.forEachIndexed { index, job ->
            JobCard(
                job = job,
                selected = job == shown,
                onSelect = { previewIndex = index },
                onChange = { labelVm.update(index, it) },
                onRemove = { labelVm.remove(index); previewIndex = 0 },
                onCreateBarcode = {
                    busy = true
                    scope.launch(Dispatchers.Main) {
                        quoteVm.ensureBarcode(job.product)
                            .onSuccess { labelVm.barcodeCreated(job.product.id, it) }
                            .onFailure { error = it.message ?: context.getString(R.string.label_barcode_error) }
                        busy = false
                    }
                },
                busy = busy,
            )
        }
        if (jobs.isNotEmpty()) {
            KnitField(madeDate, labelVm::setMadeDate, R.string.label_made_date, text = true, maxLength = 10)
        }

        SectionTitle(R.string.label_printer)
        PrinterCard(printer, onChoose = ::openPrinters, onChange = labelVm::setPrinter)

        val total = jobs.sumOf { it.copies }
        ActionButton(R.string.label_print, R.drawable.ic_print, primary = true, Modifier.fillMaxWidth()) {
            when {
                jobs.isEmpty() -> error = context.getString(R.string.label_empty)
                printer.address.isBlank() -> openPrinters()
                jobs.any { !Ean13.isValid(it.product.barcode) } -> error = context.getString(R.string.label_no_barcode_block)
                busy -> Unit
                else -> {
                    busy = true
                    scope.launch(Dispatchers.Main) {
                        val result = runCatching {
                            val data = withContext(Dispatchers.Default) {
                                jobs.fold(ByteArray(0)) { acc, job ->
                                    val mono = LabelRenderer.toMono(LabelRenderer.render(labelContent(job, settings, madeDate), printer.spec))
                                    acc + LabelPrinter.commands(printer.spec, mono, job.copies)
                                }
                            }
                            BluetoothPrinter.send(context, printer.address, data)
                        }
                        busy = false
                        result.onSuccess { Toast.makeText(context, context.getString(R.string.label_printed, total), Toast.LENGTH_LONG).show() }
                        result.onFailure { error = it.message ?: context.getString(R.string.label_print_error) }
                    }
                }
            }
        }
        if (busy) Text(stringResource(R.string.label_busy), color = colors.textSecondary, fontSize = 14.sp)
        Text(stringResource(R.string.label_hint), color = colors.textSecondary, fontSize = 13.sp)
    }

    if (picker) {
        ProductPicker(
            calculator = emptyList(),
            msProducts = msProducts,
            sync = sync,
            filterNames = msFilters,
            onRefresh = { quoteVm.refreshMs(force = true) },
            onPick = { p ->
                labelVm.add(p)
                previewIndex = labelVm.jobs.value.indexOfFirst { it.product.id == p.id }.coerceAtLeast(0)
                picker = false
            },
            onDismiss = { picker = false },
        )
    }
    if (choosePrinter) {
        val devices = remember { BluetoothPrinter.bonded(context) }
        AlertDialog(
            onDismissRequest = { choosePrinter = false },
            title = { Text(stringResource(R.string.label_choose_printer)) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    if (!BluetoothPrinter.isEnabled(context)) Text(stringResource(R.string.label_bt_off), color = colors.textSecondary)
                    if (devices.isEmpty()) Text(stringResource(R.string.label_no_devices), color = colors.textSecondary)
                    devices.forEach { d ->
                        Text(
                            "${d.name}\n${d.address}",
                            color = colors.textPrimary,
                            fontWeight = if (d.address == printer.address) FontWeight.Bold else FontWeight.Normal,
                            modifier = Modifier.fillMaxWidth().clickable {
                                labelVm.setPrinter(printer.copy(address = d.address, name = d.name))
                                choosePrinter = false
                            }.padding(vertical = 8.dp),
                        )
                    }
                }
            },
            confirmButton = { TextButton(onClick = { choosePrinter = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
    error?.let { text ->
        AlertDialog(
            onDismissRequest = { error = null },
            title = { Text(stringResource(R.string.notice_title)) },
            text = { Text(text) },
            confirmButton = { TextButton(onClick = { error = null }) { Text(stringResource(R.string.ok), color = colors.textPrimary) } },
            containerColor = colors.panel,
        )
    }
}

@Composable
private fun JobCard(
    job: LabelJob,
    selected: Boolean,
    onSelect: () -> Unit,
    onChange: (LabelJob) -> Unit,
    onRemove: () -> Unit,
    onCreateBarcode: () -> Unit,
    busy: Boolean,
) {
    val colors = LocalKnitColors.current
    Surface(
        shape = RoundedCornerShape(18.dp),
        color = colors.panel,
        modifier = Modifier.fillMaxWidth().then(if (selected) Modifier.border(2.dp, colors.equalsKey, RoundedCornerShape(18.dp)) else Modifier).clickable(onClick = onSelect),
    ) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(job.product.name, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                KnitIconButton(R.drawable.ic_close, stringResource(R.string.label_remove), onRemove)
            }
            if (Ean13.isValid(job.product.barcode)) {
                Text(stringResource(R.string.label_barcode, Ean13.human(job.product.barcode)), color = colors.textSecondary, fontSize = 13.sp)
            } else {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(stringResource(R.string.label_no_barcode), color = colors.textSecondary, fontSize = 13.sp, modifier = Modifier.weight(1f))
                    TextButton(onClick = onCreateBarcode, enabled = !busy) {
                        Text(stringResource(R.string.label_create_barcode), color = colors.textPrimary, fontWeight = FontWeight.SemiBold)
                    }
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                NumberField(job.copies, 1..999, R.string.label_copies, Modifier.weight(1f)) { onChange(job.copy(copies = it)) }
                KnitField(
                    job.pack, { v -> onChange(job.copy(pack = v.filter { it.isDigit() }.take(5))) },
                    R.string.label_pack, Modifier.weight(1f), keyboardType = KeyboardType.Number, maxLength = 5, suffix = job.product.unit,
                )
            }
        }
    }
}

@Composable
private fun PrinterCard(printer: PrinterSettings, onChoose: () -> Unit, onChange: (PrinterSettings) -> Unit) {
    val colors = LocalKnitColors.current
    val spec = printer.spec
    Surface(shape = RoundedCornerShape(18.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    if (printer.address.isBlank()) stringResource(R.string.label_printer_none) else printer.name,
                    color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f),
                )
                TextButton(onClick = onChoose) { Text(stringResource(R.string.label_printer_choose), color = colors.textPrimary) }
            }
            Chips(PrinterLanguage.entries.map { it.name }, PrinterLanguage.entries.indexOf(spec.language)) {
                onChange(printer.copy(spec = spec.copy(language = PrinterLanguage.entries[it])))
            }
            Chips(listOf("203 dpi", "300 dpi"), if (spec.dpi == 300) 1 else 0) {
                onChange(printer.copy(spec = spec.copy(dpi = if (it == 1) 300 else 203)))
            }
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.label_rotated), color = colors.textPrimary, fontSize = 14.sp, modifier = Modifier.weight(1f))
                Switch(
                    checked = spec.rotated,
                    onCheckedChange = { onChange(printer.copy(spec = spec.copy(rotated = it))) },
                    colors = SwitchDefaults.colors(checkedTrackColor = colors.equalsKey, checkedThumbColor = colors.equalsKeyText),
                )
            }
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                NumberField(spec.density, 1..15, R.string.label_density, Modifier.weight(1f)) { onChange(printer.copy(spec = spec.copy(density = it))) }
                NumberField(spec.gapMm, 0..10, R.string.label_gap, Modifier.weight(1f), suffix = "мм") { onChange(printer.copy(spec = spec.copy(gapMm = it))) }
            }
        }
    }
}

/** Число в поле ввода: пока поле пустое или вне диапазона, прежнее значение не меняется. */
@Composable
private fun NumberField(value: Int, range: IntRange, label: Int, modifier: Modifier, suffix: String? = null, onValue: (Int) -> Unit) {
    var text by remember(value) { mutableStateOf(value.toString()) }
    KnitField(
        text,
        { v ->
            text = v.filter { it.isDigit() }.take(3)
            text.toIntOrNull()?.takeIf { it in range }?.let { if (it != value) onValue(it) }
        },
        label, modifier, keyboardType = KeyboardType.Number, maxLength = 3, suffix = suffix,
    )
}

@Composable
private fun Chips(options: List<String>, selected: Int, onSelect: (Int) -> Unit) {
    val colors = LocalKnitColors.current
    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        options.forEachIndexed { i, label ->
            FilterChip(
                selected = i == selected,
                onClick = { onSelect(i) },
                label = { Text(label, fontSize = 13.sp) },
                colors = FilterChipDefaults.filterChipColors(
                    selectedContainerColor = colors.equalsKey,
                    selectedLabelColor = colors.equalsKeyText,
                    labelColor = colors.textPrimary,
                ),
            )
        }
    }
}
