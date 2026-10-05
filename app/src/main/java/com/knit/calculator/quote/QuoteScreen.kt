package com.knit.calculator.quote

import android.widget.Toast
import androidx.activity.compose.BackHandler
import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.core.content.ContextCompat
import com.knit.calculator.core.Client
import com.knit.calculator.core.CostCalculator
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
import com.knit.calculator.core.Coefficients
import com.knit.calculator.core.OptionGroup
import com.knit.calculator.core.PriceChoice
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
import java.math.BigDecimal
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

private enum class QuoteAction { SHARE, EMAIL, PRINT, SAVE }

@Composable
fun QuoteScreen(
    viewModel: QuoteViewModel,
    onBack: () -> Unit,
    onOpenCatalog: () -> Unit,
    onOpenCompany: () -> Unit,
    onOpenHistory: () -> Unit,
    onOpenOrderYarn: () -> Unit,
) {
    val draft by viewModel.draft.collectAsStateWithLifecycle()
    val sync by viewModel.sync.collectAsStateWithLifecycle()
    val catalog by viewModel.catalog.collectAsStateWithLifecycle()
    val settings by viewModel.settings.collectAsStateWithLifecycle()
    val clients by viewModel.clients.collectAsStateWithLifecycle()
    val msProducts by viewModel.msProducts.collectAsStateWithLifecycle()
    val views = remember(draft, catalog, settings, msProducts) { viewModel.lineViews(draft, catalog, settings) }
    var showPicker by rememberSaveable { mutableStateOf(false) }
    val msFilters by viewModel.msFilters.collectAsStateWithLifecycle()
    val totals = remember(views, settings) { viewModel.totals(views, settings) }
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val clipboard = LocalClipboardManager.current
    val scope = rememberCoroutineScope()
    val copiedMessage = stringResource(R.string.quote_copied)
    var showEconomics by rememberSaveable { mutableStateOf(false) }
    val director by viewModel.director.collectAsStateWithLifecycle()
    var photoLineId by rememberSaveable { mutableStateOf<Long?>(null) }
    val photoError = stringResource(R.string.quote_photo_error)

    fun attachPhoto(uri: android.net.Uri?) {
        val lineId = photoLineId ?: return
        if (uri == null) return
        scope.launch(Dispatchers.Main) {
            val path = withContext(Dispatchers.IO) { PhotoStore.import(context, uri, lineId) }
            if (path == null) {
                Toast.makeText(context, photoError, Toast.LENGTH_LONG).show()
            } else {
                viewModel.updateLine(lineId) { l -> PhotoStore.delete(l.photoPath); l.copy(photoPath = path) }
            }
        }
    }
    val galleryLauncher = rememberLauncherForActivityResult(ActivityResultContracts.PickVisualMedia()) { attachPhoto(it) }
    val cameraLauncher = rememberLauncherForActivityResult(ActivityResultContracts.TakePicture()) { ok ->
        if (ok) attachPhoto(PhotoStore.cameraTarget(context).second)
    }
    // Android 13+: разрешение на напоминания спрашиваем при первом КП.
    val notificationLauncher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { }
    var confirmNew by rememberSaveable { mutableStateOf(false) }
    var confirmEmail by rememberSaveable { mutableStateOf(false) }
    var saving by remember { mutableStateOf(false) }
    var saveError by remember { mutableStateOf<Pair<QuoteAction, String>?>(null) }

    BackHandler(onBack = onBack)

    fun document() = QuoteDocument(settings, draft, totals)

    fun makePdf(action: QuoteAction) {
        val doc = QuoteDocument(settings, viewModel.draft.value, totals, photoPaths = views.filter { it.line != null }.map { it.draft.photoPath })
        scope.launch(Dispatchers.Main) { // тосты и письма — только с главного потока
            val file: File? = try {
                withContext(Dispatchers.IO) { QuotePdf.create(context, doc) }
            } catch (e: IOException) {
                null
            }
            if (file == null) {
                Toast.makeText(context, R.string.yarn_pdf_error, Toast.LENGTH_LONG).show()
                return@launch
            }
            if (action == QuoteAction.SAVE) {
                val error = viewModel.uploadPdfNow(file)
                val message = when {
                    error != null -> context.getString(R.string.cloud_saved_pdf_error, error)
                    sync.connected -> context.getString(R.string.cloud_saved, viewModel.draft.value.number)
                    else -> context.getString(R.string.cloud_saved_local, viewModel.draft.value.number)
                }
                Toast.makeText(context, message, Toast.LENGTH_LONG).show()
                return@launch
            }
            val subject = QuotePdf.subject(context, doc)
            val text = QuotePdf.emailText(context, doc)
            val clientEmail = doc.draft.clientEmail.trim()
            if (action == QuoteAction.EMAIL && sync.connected && clientEmail.isNotEmpty()) {
                // Письмо уходит сразу с аккаунта Google фабрики; PDF сохраняется на Диск там же.
                val error = viewModel.sendEmailNow(file, clientEmail, subject, text)
                if (error == null) {
                    Toast.makeText(context, context.getString(R.string.email_sent, viewModel.draft.value.number, clientEmail), Toast.LENGTH_LONG).show()
                    return@launch
                }
                Toast.makeText(context, context.getString(R.string.email_send_failed, error), Toast.LENGTH_LONG).show()
            } else {
                viewModel.uploadPdf(file)
            }
            when (action) {
                QuoteAction.SHARE -> ReportSharing.share(context, file, subject, text)
                QuoteAction.EMAIL -> ReportSharing.email(
                    context, file, subject, text,
                    listOf(doc.draft.clientEmail.trim()).filter { it.isNotEmpty() }.toTypedArray(),
                )
                QuoteAction.PRINT -> ReportSharing.print(context, file, subject)
                QuoteAction.SAVE -> Unit
            }
        }
    }

    // С таблицей: сначала сохраняем КП и получаем номер, затем формируем PDF.
    fun run(action: QuoteAction) {
        if (saving) return
        saving = true
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            notificationLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        scope.launch {
            val result = viewModel.saveQuote(views, totals)
            saving = false
            when (result) {
                is SaveResult.Failed -> saveError = action to result.message
                else -> makePdf(action)
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
        val title = if ((sync.connected && !draft.saved) || draft.number <= 0) stringResource(R.string.quote_title_new)
        else stringResource(R.string.quote_title, draft.number)
        ScreenTopBar(title, onBack) {
            KnitIconButton(R.drawable.ic_history, stringResource(R.string.history_quotes), onOpenHistory)
            KnitIconButton(R.drawable.ic_list, stringResource(R.string.quote_catalog), onOpenCatalog)
            KnitIconButton(R.drawable.ic_settings, stringResource(R.string.quote_company), onOpenCompany)
        }
        SyncBar(sync, onRefresh = viewModel::refresh, onSetup = onOpenCompany)

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
                if (!sync.connected) KnitField(
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
            ClientSuggestions(draft.clientCompany, clients, onPick = viewModel::applyClient)
            KnitField(draft.clientContact, { v -> viewModel.updateDraft { it.copy(clientContact = v) } }, R.string.quote_client_contact, text = true, maxLength = 120)
            KnitField(
                draft.clientEmail, { v -> viewModel.updateDraft { it.copy(clientEmail = v.trim()) } }, R.string.quote_client_email,
                text = true, keyboardType = KeyboardType.Email, maxLength = 120,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                KnitField(
                    draft.clientPhone, { v -> viewModel.updateDraft { it.copy(clientPhone = v) } }, R.string.quote_client_phone,
                    text = true, keyboardType = KeyboardType.Phone, maxLength = 30, modifier = Modifier.weight(1f),
                )
                KnitField(
                    draft.clientInn, { v -> viewModel.updateDraft { it.copy(clientInn = v.filter(Char::isDigit).take(12)) } }, R.string.quote_client_inn,
                    text = true, keyboardType = KeyboardType.Number, maxLength = 12, modifier = Modifier.weight(1f),
                )
            }

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
                    onRemove = {
                        PhotoStore.delete(view.draft.photoPath)
                        viewModel.removeLine(view.draft.id)
                    },
                    onDiscount = { d -> viewModel.updateLine(view.draft.id) { it.copy(discount = d) } },
                    maxDiscount = settings.maxDiscount,
                    showEconomics = showEconomics,
                    targetPercent = settings.targetMarginPercent,
                    onGallery = {
                        photoLineId = view.draft.id
                        galleryLauncher.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
                    },
                    onCamera = {
                        photoLineId = view.draft.id
                        cameraLauncher.launch(PhotoStore.cameraTarget(context).second)
                    },
                    onRemovePhoto = {
                        PhotoStore.delete(view.draft.photoPath)
                        viewModel.updateLine(view.draft.id) { it.copy(photoPath = null) }
                    },
                    msStore = sync.msStore,
                )
            }
            if (sync.msEnabled || msProducts.isNotEmpty()) {
                // Остатки свежие: при выборе товара обновляем, если данным больше часа.
                OutlinedButton(onClick = { showPicker = true; viewModel.refreshMs(maxAgeMs = 3_600_000L) }) {
                    Icon(painterResource(R.drawable.ic_add), null, Modifier.size(18.dp), tint = colors.textPrimary)
                    Spacer(Modifier.width(6.dp))
                    Text(stringResource(R.string.quote_add_item), color = colors.textPrimary)
                }
            } else {
                AddItemButton(catalog, onAdd = viewModel::addLine, onOpenCatalog = onOpenCatalog)
            }

            KnitField(
                draft.comment, { v -> viewModel.updateDraft { it.copy(comment = v) } }, R.string.quote_comment,
                text = true, singleLine = false, maxLength = 500,
            )

            SectionTitle(R.string.quote_section_total)
            TotalsCard(totals, settings)
            if (views.any { it.line != null }) {
                // Себестоимость и прибыль — только в режиме директора.
                if (director) EconomicsToggle(showEconomics, views, settings) { showEconomics = it }
                OutlinedButton(onClick = onOpenOrderYarn, modifier = Modifier.fillMaxWidth()) {
                    Icon(painterResource(R.drawable.ic_yarn), null, Modifier.size(18.dp), tint = colors.textPrimary)
                    Spacer(Modifier.width(8.dp))
                    Text(stringResource(R.string.quote_order_yarn), color = colors.textPrimary)
                }
            }

            if (saving) {
                Text(stringResource(R.string.sync_saving), color = colors.textSecondary, fontSize = 14.sp)
            }
            if (totals.lines.isNotEmpty()) {
                ActionButton(
                    if (sync.connected) R.string.cloud_save else R.string.cloud_save_local,
                    R.drawable.ic_cloud, primary = true, Modifier.fillMaxWidth(),
                ) { run(QuoteAction.SAVE) }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    ActionButton(R.string.yarn_pdf, R.drawable.ic_share, primary = false, Modifier.weight(1f)) { run(QuoteAction.SHARE) }
                    ActionButton(R.string.yarn_email, R.drawable.ic_email, primary = false, Modifier.weight(1f)) {
                        if (sync.connected && draft.clientEmail.isNotBlank()) confirmEmail = true else run(QuoteAction.EMAIL)
                    }
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

    saveError?.let { (action, message) ->
        val colors2 = LocalKnitColors.current
        AlertDialog(
            onDismissRequest = { saveError = null },
            title = { Text(stringResource(R.string.sync_save_failed_title)) },
            text = { Text(stringResource(R.string.sync_save_failed_text, message)) },
            confirmButton = {
                TextButton(onClick = { saveError = null; run(action) }) {
                    Text(stringResource(R.string.sync_retry), color = colors2.textPrimary, fontWeight = FontWeight.SemiBold)
                }
            },
            dismissButton = {
                TextButton(onClick = { saveError = null; makePdf(action) }) {
                    Text(stringResource(R.string.sync_without_saving), color = colors2.textSecondary)
                }
            },
            containerColor = colors2.panel,
            titleContentColor = colors2.textPrimary,
            textContentColor = colors2.textSecondary,
        )
    }

    if (showPicker) {
        ProductPicker(
            calculator = catalog,
            msProducts = msProducts,
            sync = sync,
            filterNames = msFilters,
            onRefresh = { viewModel.refreshMs(force = true) },
            onPick = { viewModel.addLine(it); showPicker = false },
            onDismiss = { showPicker = false },
        )
    }

    if (confirmEmail) {
        ConfirmDialog(
            title = stringResource(R.string.email_confirm_title),
            text = stringResource(R.string.email_confirm_text, draft.clientEmail.trim(), settings.email),
            confirm = stringResource(R.string.email_confirm_send),
            onConfirm = { confirmEmail = false; run(QuoteAction.EMAIL) },
            onDismiss = { confirmEmail = false },
        )
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
private fun ClientSuggestions(query: String, clients: List<Client>, onPick: (Client) -> Unit) {
    val colors = LocalKnitColors.current
    val q = query.trim()
    if (q.length < 2) return
    val matches = clients.filter { it.company.contains(q, ignoreCase = true) && !it.company.equals(q, ignoreCase = true) }.take(4)
    if (matches.isEmpty()) return
    Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(vertical = 4.dp)) {
            Text(stringResource(R.string.quote_client_suggestions), color = colors.textSecondary, fontSize = 12.sp, modifier = Modifier.padding(horizontal = 14.dp, vertical = 4.dp))
            matches.forEach { c ->
                Text(
                    listOf(c.company, c.contact).filter { it.isNotBlank() }.joinToString(" · "),
                    color = colors.textPrimary,
                    fontSize = 15.sp,
                    modifier = Modifier.fillMaxWidth().clickable { onPick(c) }.padding(horizontal = 14.dp, vertical = 10.dp),
                )
            }
        }
    }
}

@Composable
private fun PhotoButton(path: String?, onGallery: () -> Unit, onCamera: () -> Unit, onRemove: () -> Unit) {
    val colors = LocalKnitColors.current
    var menu by remember { mutableStateOf(false) }
    val bitmap = remember(path) { PhotoStore.load(path)?.asImageBitmap() }
    Box {
        if (bitmap != null) {
            Image(
                bitmap = bitmap,
                contentDescription = stringResource(R.string.quote_photo_add),
                contentScale = ContentScale.Crop,
                modifier = Modifier.size(56.dp).clip(RoundedCornerShape(12.dp)).clickable { menu = true },
            )
        } else {
            OutlinedButton(onClick = { menu = true }) {
                Icon(painterResource(R.drawable.ic_add), null, Modifier.size(18.dp), tint = colors.textPrimary)
                Spacer(Modifier.width(6.dp))
                Text(stringResource(R.string.quote_photo_add), color = colors.textPrimary)
            }
        }
        DropdownMenu(expanded = menu, onDismissRequest = { menu = false }, containerColor = colors.panel) {
            DropdownMenuItem(text = { Text(stringResource(R.string.quote_photo_camera), color = colors.textPrimary) }, onClick = { menu = false; onCamera() })
            DropdownMenuItem(text = { Text(stringResource(R.string.quote_photo_gallery), color = colors.textPrimary) }, onClick = { menu = false; onGallery() })
            if (path != null) {
                DropdownMenuItem(text = { Text(stringResource(R.string.quote_photo_remove), color = colors.textPrimary) }, onClick = { menu = false; onRemove() })
            }
        }
    }
}

@Composable
private fun EconomicsLine(view: DraftLineView, targetPercent: String) {
    val colors = LocalKnitColors.current
    val e = view.economics
    if (e == null) {
        Text(stringResource(R.string.quote_economics_none), color = colors.textSecondary, fontSize = 13.sp)
        return
    }
    Column(Modifier.padding(end = 8.dp)) {
        Text(
            stringResource(
                R.string.quote_economics_line,
                QuoteCalculator.formatMoney(e.unitCost.total),
                QuoteCalculator.formatMoney(e.unitProfit),
                YarnCalculator.formatCompact(e.marginPercent, 1),
            ),
            color = colors.textSecondary,
            fontSize = 13.sp,
        )
        Text(
            stringResource(
                R.string.quote_economics_prices,
                QuoteCalculator.formatQuantity(e.breakEvenPrice),
                targetPercent,
                QuoteCalculator.formatQuantity(e.targetPrice),
            ),
            color = colors.textSecondary,
            fontSize = 13.sp,
        )
        if (e.unitProfit.signum() < 0) {
            Text(stringResource(R.string.quote_economics_loss), color = colors.textPrimary, fontSize = 13.sp, fontWeight = FontWeight.Bold)
        }
    }
}

@Composable
private fun EconomicsToggle(shown: Boolean, views: List<DraftLineView>, settings: CompanySettings, onToggle: (Boolean) -> Unit) {
    val colors = LocalKnitColors.current
    Surface(shape = RoundedCornerShape(14.dp), color = colors.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 14.dp, vertical = 8.dp)) {
            Row(Modifier.fillMaxWidth().clickable { onToggle(!shown) }, verticalAlignment = Alignment.CenterVertically) {
                Text(stringResource(R.string.quote_economics_show), color = colors.textPrimary, fontSize = 14.sp, modifier = Modifier.weight(1f))
                Switch(
                    checked = shown,
                    onCheckedChange = onToggle,
                    colors = SwitchDefaults.colors(checkedTrackColor = colors.equalsKey, checkedThumbColor = colors.equalsKeyText),
                )
            }
            if (shown) {
                val economics = CostCalculator.quote(views.map { it.economics })
                if (views.any { it.economics != null }) {
                    Text(
                        stringResource(
                            R.string.quote_economics_total,
                            QuoteCalculator.formatMoney(economics.totalCost),
                            QuoteCalculator.formatMoney(economics.totalProfit),
                            YarnCalculator.formatCompact(economics.marginPercent, 1),
                        ),
                        color = if (economics.totalProfit.signum() < 0) colors.textPrimary else colors.textSecondary,
                        fontWeight = if (economics.totalProfit.signum() < 0) FontWeight.SemiBold else FontWeight.Normal,
                        fontSize = 14.sp,
                    )
                } else {
                    Text(stringResource(R.string.quote_economics_none), color = colors.textSecondary, fontSize = 13.sp)
                }
            }
        }
    }
}

@Composable
private fun SyncBar(sync: SyncStatus, onRefresh: () -> Unit, onSetup: () -> Unit) {
    val colors = LocalKnitColors.current
    val time = sync.lastSync?.let { remember(it) { SimpleDateFormat("dd.MM HH:mm", Locale.getDefault()).format(Date(it)) } }
    val text = when {
        !sync.connected -> stringResource(R.string.sync_local)
        sync.loading -> stringResource(R.string.sync_loading)
        sync.error != null -> stringResource(R.string.sync_error, sync.error, time ?: "—")
        else -> stringResource(R.string.sync_ok, time ?: "—")
    }
    Surface(
        onClick = if (sync.connected) onRefresh else onSetup,
        color = colors.panel,
        shape = RoundedCornerShape(14.dp),
        modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
    ) {
        Column(Modifier.padding(horizontal = 14.dp, vertical = 10.dp)) {
            Text(text, color = if (sync.error != null) colors.textPrimary else colors.textSecondary, fontSize = 13.sp)
            sync.warnings.take(3).forEach { Text("• $it", color = colors.textSecondary, fontSize = 12.sp) }
        }
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
    onDiscount: (String) -> Unit,
    maxDiscount: BigDecimal,
    showEconomics: Boolean,
    targetPercent: String,
    onGallery: () -> Unit,
    onCamera: () -> Unit,
    onRemovePhoto: () -> Unit,
    msStore: String = "",
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
                    // У товаров МойСклад цена по тиражу — готовая, коэффициент не показываем.
                    if (product.externalId.isBlank() && line.volumeFactor.compareTo(BigDecimal.ONE) != 0) add(stringResource(R.string.quote_volume, QuoteCalculator.formatFactor(line.volumeFactor)))
                    if (line.discountPercent.signum() > 0) add(stringResource(R.string.kp_discount, YarnCalculator.formatCompact(line.discountPercent, 2)))
                    if (line.setupFee.signum() > 0) add(stringResource(R.string.quote_setup, QuoteCalculator.formatMoney(line.setupFee)))
                }.joinToString(" · ")
            }
            Text(details, color = colors.textSecondary, fontSize = 14.sp)
            stockText(product, msStore)?.let { Text(it, color = colors.textSecondary, fontSize = 14.sp) }
            if (product.badges.isNotEmpty()) Badges(product.badges)
            // Характеристики МойСклад, которых нет в названии (для менеджера, в КП не печатаются).
            product.attributes.filter { (_, v) -> v.isNotBlank() && v != "-" && !product.name.contains(v) }
                .map { (k, v) -> "$k: $v" }.joinToString(" · ").takeIf { it.isNotBlank() }
                ?.let { Text(it, color = colors.textSecondary, fontSize = 13.sp) }
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.padding(end = 8.dp)) {
                KnitField(
                    value = view.draft.discount,
                    onChange = onDiscount,
                    label = R.string.quote_discount_field,
                    modifier = Modifier.width(130.dp),
                )
                PhotoButton(view.draft.photoPath, onGallery, onCamera, onRemovePhoto)
            }
            if (view.discountTooHigh) {
                val max = YarnCalculator.formatCompact(maxDiscount, 2)
                Text(stringResource(R.string.quote_discount_max, max), color = colors.textPrimary, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
            }
            if (showEconomics && line != null) EconomicsLine(view, targetPercent)
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
                    Text(choiceLabel(selected), color = colors.textPrimary, fontSize = 16.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                Icon(painterResource(R.drawable.ic_dropdown), null, tint = colors.textSecondary)
            }
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }, containerColor = colors.panel) {
            group.choices.forEach { choice ->
                DropdownMenuItem(
                    text = {
                        Text(
                            choiceLabel(choice),
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

private fun choiceLabel(choice: PriceChoice): String {
    val parts = buildList {
        if (choice.factors.isNotEmpty()) add(QuoteCalculator.formatFactor(Coefficients.product(choice.factors).value))
        if (choice.priceAdd.signum() > 0) add("+${QuoteCalculator.formatMoney(choice.priceAdd)} ₽")
        if (choice.priceAdd.signum() < 0) add("${QuoteCalculator.formatMoney(choice.priceAdd)} ₽")
    }
    return if (parts.isEmpty()) choice.name else "${choice.name}  (${parts.joinToString(", ")})"
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
            val delivery = deliveryText(totals, settings)
            if (delivery.isNotBlank() && totals.lines.isNotEmpty()) {
                Text(stringResource(R.string.quote_delivery, delivery.replaceFirstChar(Char::lowercaseChar)), color = colors.textSecondary, fontSize = 15.sp)
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
