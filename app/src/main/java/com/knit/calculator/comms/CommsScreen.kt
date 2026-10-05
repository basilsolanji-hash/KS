package com.knit.calculator.comms

import android.annotation.SuppressLint
import android.app.DownloadManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Environment
import android.webkit.CookieManager
import android.webkit.URLUtil
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Toast
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
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.webkit.ProfileStore
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import com.knit.calculator.R
import com.knit.calculator.quote.openUrl
import com.knit.calculator.ui.components.KnitField
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.theme.LocalKnitColors

private const val DESKTOP_UA =
    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"

/**
 * «Связь»: почтовые ящики, Макс и Telegram (веб-версии) внутри приложения.
 * У каждого ящика свой вход (отдельный профиль браузера, если телефон поддерживает), вкладки не перезагружаются
 * при переключении. Уведомления о новых сообщениях веб-версии не присылают — для них нужны родные приложения.
 */
@Composable
fun CommsScreen(onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val store = remember { ChannelStore(context) }
    var channels by remember { mutableStateOf(store.load()) }
    var selectedId by remember { mutableStateOf(store.selected?.takeIf { id -> channels.any { it.id == id } } ?: channels.firstOrNull()?.id) }
    val views = remember { mutableStateMapOf<String, WebView>() }
    val opened = remember { mutableStateMapOf<String, Boolean>() }
    var progress by remember { mutableIntStateOf(0) }
    var editing by remember { mutableStateOf(false) }
    var adding by remember { mutableStateOf(false) }

    // Вложения: выбор файлов для формы на странице (письмо, сообщение).
    var pendingFiles by remember { mutableStateOf<ValueCallback<Array<Uri>>?>(null) }
    val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.GetMultipleContents()) { uris ->
        pendingFiles?.onReceiveValue(uris.toTypedArray())
        pendingFiles = null
    }

    val selected = channels.firstOrNull { it.id == selectedId }
    androidx.compose.runtime.LaunchedEffect(selectedId) { selectedId?.let { opened[it] = true } }

    BackHandler {
        val w = selectedId?.let { views[it] }
        if (w != null && w.canGoBack()) w.goBack() else onBack()
    }

    Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
        ScreenTopBar(stringResource(R.string.comms_title), onBack) {
            KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.comms_reload), { selectedId?.let { views[it]?.reload() } })
            KnitIconButton(R.drawable.ic_web, stringResource(R.string.shop_open_browser), {
                openUrl(context, selectedId?.let { views[it]?.url } ?: selected?.url.orEmpty())
            })
            KnitIconButton(R.drawable.ic_settings, stringResource(R.string.comms_manage), { editing = true })
        }
        LazyRow(
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
        ) {
            items(channels, key = { it.id }) { c ->
                FilterChip(
                    selected = c.id == selectedId,
                    onClick = { selectedId = c.id; store.selected = c.id },
                    label = { Text((if (c.kind == Channel.MAIL) "✉ " else "") + c.name) },
                    colors = FilterChipDefaults.filterChipColors(
                        selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary,
                    ),
                )
            }
            item(key = "add") {
                FilterChip(selected = false, onClick = { adding = true }, label = { Text(stringResource(R.string.comms_add_mail)) },
                    colors = FilterChipDefaults.filterChipColors(labelColor = colors.textSecondary))
            }
        }
        if (progress in 1..99) LinearProgressIndicator(progress = { progress / 100f }, modifier = Modifier.fillMaxWidth(), color = colors.accent)
        Box(Modifier.fillMaxWidth().weight(1f)) {
            if (selected == null) {
                Text(stringResource(R.string.comms_empty), color = colors.textSecondary, fontSize = 15.sp, modifier = Modifier.padding(24.dp))
            }
            // Открытые вкладки живут, пока открыт экран: переключение не сбрасывает письмо или чат.
            channels.filter { opened[it.id] == true }.forEach { c ->
                androidx.compose.runtime.key(c.id, c.desktop) {
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

    if (adding) {
        AddMailDialog(onDismiss = { adding = false }) { c ->
            adding = false
            channels = listOf(c) + channels
            store.save(channels)
            selectedId = c.id
            store.selected = c.id
        }
    }
    if (editing) {
        ManageDialog(
            channels,
            onChange = { list ->
                channels = list
                store.save(list)
                if (channels.none { it.id == selectedId }) selectedId = channels.firstOrNull()?.id
            },
            onDismiss = { editing = false },
        )
    }
}

@SuppressLint("SetJavaScriptEnabled")
private fun createWebView(
    ctx: Context,
    c: Channel,
    onProgress: (Int) -> Unit,
    onFiles: (ValueCallback<Array<Uri>>, String) -> Unit,
): WebView = WebView(ctx).apply {
    // Свой профиль на каждый ящик — можно войти в два ящика одного сервиса.
    val profileName = "ks-" + c.id
    val profile = if (WebViewFeature.isFeatureSupported(WebViewFeature.MULTI_PROFILE)) {
        runCatching {
            ProfileStore.getInstance().getOrCreateProfile(profileName)
            WebViewCompat.setProfile(this, profileName)
            ProfileStore.getInstance().getProfile(profileName)
        }.getOrNull()
    } else null
    val cookies = profile?.cookieManager ?: CookieManager.getInstance()
    cookies.setAcceptCookie(true)
    cookies.setAcceptThirdPartyCookies(this, true)
    settings.javaScriptEnabled = true
    settings.domStorageEnabled = true
    settings.databaseEnabled = true
    settings.loadWithOverviewMode = true
    settings.useWideViewPort = true
    settings.builtInZoomControls = true
    settings.displayZoomControls = false
    if (c.desktop) settings.userAgentString = DESKTOP_UA
    webViewClient = object : WebViewClient() {
        override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
            val uri = request.url
            if (uri.scheme == "http" || uri.scheme == "https") return false
            // tel:, mailto:, tg:, intent: — в приложения телефона.
            runCatching {
                val intent = if (uri.scheme == "intent") Intent.parseUri(uri.toString(), Intent.URI_INTENT_SCHEME) else Intent(Intent.ACTION_VIEW, uri)
                intent.addCategory(Intent.CATEGORY_BROWSABLE).setComponent(null).setSelector(null)
                ctx.startActivity(intent)
            }
            return true
        }
    }
    webChromeClient = object : WebChromeClient() {
        override fun onProgressChanged(view: WebView, newProgress: Int) = onProgress(newProgress)

        override fun onShowFileChooser(view: WebView, callback: ValueCallback<Array<Uri>>, params: FileChooserParams): Boolean {
            onFiles(callback, params.acceptTypes.firstOrNull { it.isNotBlank() && !it.startsWith(".") } ?: "*/*")
            return true
        }
    }
    setDownloadListener { url, userAgent, disposition, mime, _ ->
        if (!url.startsWith("http")) {
            Toast.makeText(ctx, R.string.comms_download_browser, Toast.LENGTH_LONG).show()
            return@setDownloadListener
        }
        runCatching {
            val name = URLUtil.guessFileName(url, disposition, mime)
            val request = DownloadManager.Request(Uri.parse(url))
                .addRequestHeader("User-Agent", userAgent)
                .setTitle(name)
                .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                .setDestinationInExternalFilesDir(ctx, Environment.DIRECTORY_DOWNLOADS, name)
            cookies.getCookie(url)?.let { request.addRequestHeader("Cookie", it) }
            (ctx.getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager).enqueue(request)
            Toast.makeText(ctx, ctx.getString(R.string.comms_downloading, name), Toast.LENGTH_SHORT).show()
        }.onFailure { Toast.makeText(ctx, R.string.comms_download_browser, Toast.LENGTH_LONG).show() }
    }
    loadUrl(c.url)
}

/** Новый почтовый ящик: сервис или свой адрес веб-почты, название вкладки. */
@Composable
private fun AddMailDialog(onDismiss: () -> Unit, onAdd: (Channel) -> Unit) {
    val colors = LocalKnitColors.current
    var name by remember { mutableStateOf("") }
    var url by remember { mutableStateOf("") }
    var note by remember { mutableStateOf<String?>(null) }
    val valid = ChannelStore.normalizeUrl(url)
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.comms_add_mail_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(stringResource(R.string.comms_service), color = colors.textSecondary, fontSize = 13.sp)
                LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    items(MAIL_PRESETS) { p ->
                        FilterChip(
                            selected = url == p.url,
                            onClick = { url = p.url; note = p.note; if (name.isBlank()) name = p.title },
                            label = { Text(p.title) },
                            colors = FilterChipDefaults.filterChipColors(selectedContainerColor = colors.equalsKey, selectedLabelColor = colors.equalsKeyText, labelColor = colors.textPrimary),
                        )
                    }
                }
                KnitField(url, { url = it; note = null }, R.string.comms_mail_url, text = true, maxLength = 200,
                    keyboardType = androidx.compose.ui.text.input.KeyboardType.Uri)
                KnitField(name, { name = it }, R.string.comms_mail_name, text = true, maxLength = 40)
                note?.let { Text(it, color = colors.textSecondary, fontSize = 12.sp) }
                Text(stringResource(R.string.comms_mail_hint), color = colors.textSecondary, fontSize = 12.sp)
            }
        },
        confirmButton = {
            TextButton(enabled = valid != null && name.isNotBlank(), onClick = { onAdd(ChannelStore.newMail(name.trim(), valid!!)) }) {
                Text(stringResource(R.string.comms_add), color = colors.textPrimary, fontWeight = FontWeight.SemiBold)
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel), color = colors.textSecondary) } },
        containerColor = colors.panel,
    )
}

/** Вкладки: «версия для ПК», порядок и удаление (с выходом из ящика на этом телефоне). */
@Composable
private fun ManageDialog(channels: List<Channel>, onChange: (List<Channel>) -> Unit, onDismiss: () -> Unit) {
    val colors = LocalKnitColors.current
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.comms_manage)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                channels.forEachIndexed { i, c ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Column(Modifier.weight(1f)) {
                            Text(c.name, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.SemiBold)
                            Text(stringResource(R.string.comms_desktop), color = colors.textSecondary, fontSize = 12.sp)
                        }
                        Switch(
                            checked = c.desktop,
                            onCheckedChange = { v -> onChange(channels.map { if (it.id == c.id) it.copy(desktop = v) else it }) },
                            colors = SwitchDefaults.colors(checkedTrackColor = colors.equalsKey, checkedThumbColor = colors.equalsKeyText),
                        )
                        if (i > 0) {
                            KnitIconButton(R.drawable.ic_arrow_up, stringResource(R.string.comms_up), {
                                onChange(channels.toMutableList().apply { add(i - 1, removeAt(i)) })
                            })
                        }
                        KnitIconButton(R.drawable.ic_delete, stringResource(R.string.comms_delete, c.name), {
                            onChange(channels.filterNot { it.id == c.id })
                        })
                    }
                }
                Text(stringResource(R.string.comms_manage_hint), color = colors.textSecondary, fontSize = 12.sp)
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.products_close), color = colors.textPrimary) } },
        dismissButton = {
            TextButton(onClick = { onChange(ChannelStore.DEFAULTS.filter { d -> channels.none { it.id == d.id } } + channels) }) {
                Text(stringResource(R.string.comms_restore), color = colors.textSecondary)
            }
        },
        containerColor = colors.panel,
    )
}
