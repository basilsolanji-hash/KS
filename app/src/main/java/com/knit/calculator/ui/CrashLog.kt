package com.knit.calculator.ui

import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.sp
import com.knit.calculator.R
import com.knit.calculator.ui.theme.LocalKnitColors
import java.io.File

/**
 * Если приложение закрылось с ошибкой, причина сохраняется на телефоне (без данных клиентов —
 * только место ошибки), а при следующем запуске предлагается отправить её разработчику.
 */
object CrashLog {
    private fun file(context: Context) = File(context.filesDir, "last_crash.txt")

    fun install(context: Context) {
        val app = context.applicationContext
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, error ->
            runCatching {
                val version = runCatching { app.packageManager.getPackageInfo(app.packageName, 0).versionName }.getOrNull()
                file(app).writeText(
                    "Версия: $version\nТелефон: ${Build.MANUFACTURER} ${Build.MODEL}, Android ${Build.VERSION.RELEASE}\n\n" +
                        error.stackTraceToString().take(6000),
                )
            }
            previous?.uncaughtException(thread, error)
        }
    }

    fun take(context: Context): String? = file(context).takeIf { it.exists() }?.let { f -> runCatching { f.readText() }.getOrNull().also { f.delete() } }
}

/** Окно после сбоя: что случилось и «Отправить» (в мессенджер или почту). */
@Composable
fun CrashReportDialog() {
    val context = LocalContext.current
    var text by remember { mutableStateOf(CrashLog.take(context)) }
    val report = text ?: return
    val colors = LocalKnitColors.current
    AlertDialog(
        onDismissRequest = { text = null },
        title = { Text(stringResource(R.string.crash_title)) },
        text = { Text(stringResource(R.string.crash_text), color = colors.textPrimary, fontSize = 14.sp) },
        confirmButton = {
            TextButton(onClick = {
                val send = Intent(Intent.ACTION_SEND).setType("text/plain")
                    .putExtra(Intent.EXTRA_SUBJECT, "ФАБРИКА: ошибка приложения")
                    .putExtra(Intent.EXTRA_TEXT, report)
                runCatching { context.startActivity(Intent.createChooser(send, context.getString(R.string.crash_send))) }
                text = null
            }) { Text(stringResource(R.string.crash_send), color = colors.textPrimary) }
        },
        dismissButton = { TextButton(onClick = { text = null }) { Text(stringResource(R.string.products_close), color = colors.textSecondary) } },
        containerColor = colors.panel,
    )
}
