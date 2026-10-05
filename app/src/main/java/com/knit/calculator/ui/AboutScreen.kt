package com.knit.calculator.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.R
import com.knit.calculator.quote.FormScreen
import com.knit.calculator.quote.SyncStatus
import com.knit.calculator.ui.components.SectionTitle
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** «О версии»: номер сборки, состояние подключений и что нового. */
@Composable
fun AboutScreen(
    version: String,
    sync: SyncStatus,
    msCount: Int,
    director: Boolean,
    manager: String,
    printer: String,
    onBack: () -> Unit,
) {
    val colors = LocalKnitColors.current
    fun time(t: Long?) = t?.let { SimpleDateFormat("dd.MM.yyyy HH:mm", Locale.getDefault()).format(Date(it)) } ?: "—"
    FormScreen(stringResource(R.string.about_title), onBack) {
        Text(stringResource(R.string.app_name), color = colors.textPrimary, fontSize = 24.sp, fontWeight = FontWeight.Bold)
        Text(stringResource(R.string.about_version, version), color = colors.textSecondary, fontSize = 15.sp)

        SectionTitle(R.string.about_connections)
        Card {
            Line(
                stringResource(R.string.about_sheet),
                when {
                    !sync.connected -> stringResource(R.string.about_off)
                    sync.error != null -> stringResource(R.string.about_error, sync.error)
                    else -> stringResource(R.string.about_on, time(sync.lastSync))
                },
            )
            Line(
                stringResource(R.string.about_moysklad),
                when {
                    !sync.msEnabled -> stringResource(R.string.about_off)
                    sync.msError != null -> stringResource(R.string.about_error, sync.msError)
                    else -> stringResource(R.string.about_ms_on, msCount, sync.msStore.ifBlank { "—" }, time(sync.msLoadedAt))
                },
            )
            Line(stringResource(R.string.about_role), stringResource(if (director) R.string.role_director else R.string.role_manager) + manager.takeIf { it.isNotBlank() }?.let { " · $it" }.orEmpty())
            Line(stringResource(R.string.about_printer), printer.ifBlank { stringResource(R.string.about_off) })
        }

        SectionTitle(R.string.about_news)
        Card {
            listOf(
                R.string.about_news_181, R.string.about_news_180, R.string.about_news_170, R.string.about_news_162, R.string.about_news_161, R.string.about_news_160, R.string.about_news_151, R.string.about_news_150, R.string.about_news_140, R.string.about_news_130,
            ).forEach { Text(stringResource(it), color = colors.textPrimary, fontSize = 14.sp, modifier = Modifier.padding(vertical = 4.dp)) }
        }
        Text(stringResource(R.string.about_support), color = colors.textSecondary, fontSize = 13.sp)
    }
}

@Composable
private fun Card(content: @Composable () -> Unit) {
    Surface(shape = RoundedCornerShape(18.dp), color = LocalKnitColors.current.panel, modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) { content() }
    }
}

@Composable
private fun Line(title: String, value: String) {
    val colors = LocalKnitColors.current
    Column {
        Text(title, color = colors.textSecondary, fontSize = 13.sp)
        Text(value, color = colors.textPrimary, fontSize = 15.sp, fontWeight = FontWeight.Medium)
    }
}
