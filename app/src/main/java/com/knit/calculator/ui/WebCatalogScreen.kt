package com.knit.calculator.ui

import android.annotation.SuppressLint
import android.content.Intent
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.viewinterop.AndroidView
import com.knit.calculator.R
import com.knit.calculator.quote.openUrl
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.theme.LocalKnitColors

/**
 * Каталог на сайте фабрики внутри приложения: найти товар и отправить ссылку клиенту
 * («Поделиться» отправляет адрес открытой страницы).
 */
@SuppressLint("SetJavaScriptEnabled")
@Composable
fun WebCatalogScreen(startUrl: String, onBack: () -> Unit) {
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    var webView by remember { mutableStateOf<WebView?>(null) }
    var title by remember { mutableStateOf("") }
    var progress by remember { mutableIntStateOf(0) }
    val shareTitle = stringResource(R.string.shop_share_title)
    var qrUrl by remember { mutableStateOf<String?>(null) }
    qrUrl?.let { com.knit.calculator.quote.QrDialog(it) { qrUrl = null } }

    BackHandler {
        val w = webView
        if (w != null && w.canGoBack()) w.goBack() else onBack()
    }

    Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
        ScreenTopBar(title.ifBlank { stringResource(R.string.home_shop) }, onBack) {
            KnitIconButton(R.drawable.ic_share, stringResource(R.string.shop_share), {
                val url = webView?.url ?: startUrl
                val text = listOf(webView?.title.orEmpty(), url).filter { it.isNotBlank() }.joinToString("\n")
                val send = Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text)
                context.startActivity(Intent.createChooser(send, shareTitle))
            })
            KnitIconButton(R.drawable.ic_qr, stringResource(R.string.qr_title), { qrUrl = webView?.url ?: startUrl })
            KnitIconButton(R.drawable.ic_web, stringResource(R.string.shop_open_browser), { openUrl(context, webView?.url ?: startUrl) })
        }
        if (progress in 1..99) LinearProgressIndicator(progress = { progress / 100f }, modifier = Modifier.fillMaxWidth(), color = colors.accent)
        AndroidView(
            factory = { ctx ->
                WebView(ctx).apply {
                    settings.javaScriptEnabled = true
                    settings.domStorageEnabled = true
                    webViewClient = object : WebViewClient() {
                        // Ссылки сайта фабрики — внутри приложения, остальные (WhatsApp, телефон…) — в системе.
                        override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                            val uri = request.url
                            // Точное совпадение сайта (или его поддомен): «evilfabrika-ks.ru» — уже чужой сайт.
                            val base = android.net.Uri.parse(startUrl).host.orEmpty().removePrefix("www.")
                            val host = uri.host.orEmpty().removePrefix("www.")
                            val internal = (uri.scheme == "http" || uri.scheme == "https") && base.isNotEmpty() &&
                                (host == base || host.endsWith(".$base"))
                            if (internal) return false
                            runCatching { ctx.startActivity(Intent(Intent.ACTION_VIEW, uri)) }
                            return true
                        }

                        override fun onPageFinished(view: WebView, url: String?) {
                            title = view.title.orEmpty()
                        }
                    }
                    webChromeClient = object : android.webkit.WebChromeClient() {
                        override fun onProgressChanged(view: WebView, newProgress: Int) {
                            progress = newProgress
                        }
                    }
                    loadUrl(startUrl)
                    webView = this
                }
            },
            modifier = Modifier.fillMaxSize(),
        )
    }
}
