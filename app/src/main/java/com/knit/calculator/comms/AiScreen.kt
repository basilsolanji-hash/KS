package com.knit.calculator.comms

import android.net.Uri
import android.webkit.ValueCallback
import android.webkit.WebView
import androidx.activity.compose.BackHandler
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyRow
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.quote.QuoteViewModel
import com.knit.calculator.quote.openUrl
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.theme.LocalKnitColors
import kotlinx.coroutines.launch

/** ИИ-помощники (веб-версии). Названия совпадают со значениями строки «ИИ для менеджеров». */
val AI_CHANNELS = listOf(
    Channel("ai-chatgpt", "ChatGPT", "https://chatgpt.com", "ai"),
    Channel("ai-claude", "Claude", "https://claude.ai", "ai"),
    Channel("ai-deepseek", "DeepSeek", "https://chat.deepseek.com", "ai"),
)

const val AI_SETTING = "ИИ для менеджеров"

/** Какие ИИ доступны менеджерам по строке «ИИ для менеджеров» («ChatGPT, Claude»); пусто — ни одного. */
fun allowedAi(setting: String?): List<Channel> {
    val names = setting.orEmpty().split(',', ';').map { it.trim().lowercase() }.filter { it.isNotEmpty() }
    return AI_CHANNELS.filter { it.name.lowercase() in names }
}

/**
 * «ИИ»: ChatGPT, Claude, DeepSeek внутри приложения. Директор видит все и выбирает, какие доступны менеджерам
 * (сохраняется в таблице, строка «ИИ для менеджеров»); менеджер видит только разрешённые.
 */
@Composable
fun AiScreen(quoteVm: QuoteViewModel, director: Boolean, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val sync by quoteVm.sync.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    var setting by remember(sync.lastSync) { mutableStateOf(quoteVm.sheetSetting(AI_SETTING)) }
    val channels = if (director) AI_CHANNELS else allowedAi(setting)
    var selectedId by remember { mutableStateOf(channels.firstOrNull()?.id) }
    val views = remember { mutableStateMapOf<String, WebView>() }
    val opened = remember { mutableStateMapOf<String, Boolean>() }
    var progress by remember { mutableIntStateOf(0) }
    var managing by remember { mutableStateOf(false) }
    var pendingFiles by remember { mutableStateOf<ValueCallback<Array<Uri>>?>(null) }
    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetMultipleContents()) { uris ->
        pendingFiles?.onReceiveValue(uris.toTypedArray())
        pendingFiles = null
    }
    LaunchedEffect(selectedId) { selectedId?.let { opened[it] = true } }
    BackHandler {
        val w = selectedId?.let { views[it] }
        if (w != null && w.canGoBack()) w.goBack() else onBack()
    }

    Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
        ScreenTopBar(stringResource(R.string.ai_title), onBack) {
            KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.comms_reload), { selectedId?.let { views[it]?.reload() } })
            KnitIconButton(R.drawable.ic_web, stringResource(R.string.shop_open_browser), {
                openUrl(context, selectedId?.let { views[it]?.url } ?: channels.firstOrNull { it.id == selectedId }?.url.orEmpty())
            })
            if (director) KnitIconButton(R.drawable.ic_settings, stringResource(R.string.ai_manage), { managing = true })
        }
        if (channels.isEmpty()) {
            Text(stringResource(R.string.ai_none), color = colors.textSecondary, fontSize = 15.sp, modifier = Modifier.padding(24.dp))
            return@Column
        }
        LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp)) {
            items(channels, key = { it.id }) { c ->
                FilterChip(
                    selected = c.id == selectedId,
                    onClick = { selectedId = c.id },
                    label = { Text(c.name) },
                    colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                )
            }
        }
        if (progress in 1..99) LinearProgressIndicator(progress = { progress / 100f }, modifier = Modifier.fillMaxWidth(), color = colors.accent)
        Box(Modifier.fillMaxWidth().weight(1f)) {
            channels.filter { opened[it.id] == true }.forEach { c ->
                androidx.compose.runtime.key(c.id) {
                    AndroidView(
                        factory = { ctx ->
                            createWebView(ctx, c,
                                onProgress = { if (c.id == selectedId) progress = it },
                                onFiles = { cb, mime ->
                                    pendingFiles?.onReceiveValue(null)
                                    pendingFiles = cb
                                    runCatching { filePicker.launch(mime) }.onFailure { cb.onReceiveValue(null); pendingFiles = null }
                                },
                            ).also { views[c.id] = it }
                        },
                        modifier = if (c.id == selectedId) Modifier.fillMaxSize() else Modifier.size(0.dp),
                    )
                }
            }
        }
    }

    if (managing) {
        var chosen by remember { mutableStateOf(allowedAi(setting).map { it.name }.toSet()) }
        var error by remember { mutableStateOf<String?>(null) }
        var saving by remember { mutableStateOf(false) }
        AlertDialog(
            onDismissRequest = { if (!saving) managing = false },
            title = { Text(stringResource(R.string.ai_manage)) },
            text = {
                Column {
                    Text(stringResource(R.string.ai_manage_hint), color = colors.textSecondary, fontSize = 13.sp)
                    AI_CHANNELS.forEach { c ->
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Checkbox(checked = c.name in chosen, onCheckedChange = { v -> chosen = if (v) chosen + c.name else chosen - c.name })
                            Text(c.name, color = colors.textPrimary, fontSize = 16.sp)
                        }
                    }
                    error?.let { Text(it, color = androidx.compose.ui.graphics.Color(0xFFD32F2F), fontSize = 13.sp) }
                }
            },
            confirmButton = {
                TextButton(enabled = !saving, onClick = {
                    val value = AI_CHANNELS.filter { it.name in chosen }.joinToString(", ") { it.name }
                    saving = true
                    scope.launch {
                        val e = quoteVm.setSheetSetting(AI_SETTING, value)
                        saving = false
                        if (e == null) { setting = value; managing = false } else error = e
                    }
                }) { Text(stringResource(if (saving) R.string.sync_loading else R.string.shortcuts_save), color = colors.textPrimary, fontWeight = FontWeight.SemiBold) }
            },
            dismissButton = { TextButton(enabled = !saving, onClick = { managing = false }) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
            containerColor = colors.panel,
        )
    }
}
