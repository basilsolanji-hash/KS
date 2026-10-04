package com.knit.calculator.ui

import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.ColorFilter
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.R
import com.knit.calculator.data.ThemeMode
import com.knit.calculator.quote.SyncStatus
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/** Разделы, доступные с главного экрана. */
enum class HomeAction { QUOTE, YARN, CALCULATOR, HISTORY, REPORT, SHOP, SETTINGS }

/** Главный экран: логотип и крупные кнопки разделов. */
@Composable
fun HomeScreen(sync: SyncStatus, themeMode: ThemeMode, onTheme: () -> Unit, onAction: (HomeAction) -> Unit) {
    val colors = LocalKnitColors.current
    Column(
        Modifier
            .fillMaxSize()
            .background(colors.background)
            .safeDrawingPadding()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
            Spacer(Modifier.weight(1f))
            KnitIconButton(
                when (themeMode) {
                    ThemeMode.SYSTEM -> R.drawable.ic_theme_auto
                    ThemeMode.LIGHT -> R.drawable.ic_theme_light
                    ThemeMode.DARK -> R.drawable.ic_theme_dark
                },
                stringResource(R.string.theme_toggle, ""),
                onTheme,
            )
        }
        Image(
            painter = painterResource(R.drawable.ic_ks_logo),
            contentDescription = null,
            colorFilter = ColorFilter.tint(colors.textPrimary),
            modifier = Modifier.height(72.dp).align(Alignment.CenterHorizontally),
        )
        Text(
            stringResource(R.string.app_name),
            color = colors.textPrimary,
            fontSize = 26.sp,
            fontWeight = FontWeight.Bold,
            textAlign = TextAlign.Center,
            modifier = Modifier.fillMaxWidth().semantics { heading() },
        )
        Spacer(Modifier.height(4.dp))

        Surface(
            onClick = { onAction(HomeAction.QUOTE) },
            shape = RoundedCornerShape(24.dp),
            color = colors.equalsKey,
            contentColor = colors.equalsKeyText,
            modifier = Modifier.fillMaxWidth().height(120.dp),
        ) {
            Row(Modifier.padding(20.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(painterResource(R.drawable.ic_quote), null, Modifier.size(44.dp))
                Column(Modifier.padding(start = 16.dp)) {
                    Text(stringResource(R.string.home_quote), fontSize = 22.sp, fontWeight = FontWeight.Bold)
                    Text(stringResource(R.string.home_quote_hint), fontSize = 14.sp)
                }
            }
        }
        val tiles = listOf(
            Triple(HomeAction.YARN, R.string.home_yarn, R.drawable.ic_yarn),
            Triple(HomeAction.CALCULATOR, R.string.home_calculator, R.drawable.ic_calculator),
            Triple(HomeAction.HISTORY, R.string.home_history, R.drawable.ic_history),
            Triple(HomeAction.REPORT, R.string.home_report, R.drawable.ic_report),
            Triple(HomeAction.SHOP, R.string.home_shop, R.drawable.ic_web),
            Triple(HomeAction.SETTINGS, R.string.home_settings, R.drawable.ic_settings),
        )
        tiles.chunked(2).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                row.forEach { (action, label, icon) ->
                    Tile(label, icon, Modifier.weight(1f)) { onAction(action) }
                }
            }
        }
        SyncLine(sync)
    }
}

@Composable
private fun Tile(@StringRes label: Int, @DrawableRes icon: Int, modifier: Modifier, onClick: () -> Unit) {
    val colors = LocalKnitColors.current
    Surface(onClick = onClick, shape = RoundedCornerShape(20.dp), color = colors.panel, modifier = modifier.height(104.dp)) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.SpaceBetween) {
            Icon(painterResource(icon), null, Modifier.size(30.dp), tint = if (colors.isDark) colors.accent else colors.textPrimary)
            Text(stringResource(label), color = colors.textPrimary, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
        }
    }
}

@Composable
private fun SyncLine(sync: SyncStatus) {
    val colors = LocalKnitColors.current
    val time = sync.lastSync?.let { SimpleDateFormat("dd.MM HH:mm", Locale.getDefault()).format(Date(it)) } ?: "—"
    val text = when {
        !sync.connected -> stringResource(R.string.home_sync_off)
        sync.error != null -> stringResource(R.string.home_sync_error, time)
        else -> stringResource(R.string.home_sync_on, time)
    }
    Text(text, color = colors.textSecondary, fontSize = 13.sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(top = 4.dp))
}
