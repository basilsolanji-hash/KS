package com.knit.calculator.quote

import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.R
import com.knit.calculator.core.QuoteCalculator
import com.knit.calculator.ui.components.KnitIconButton
import com.knit.calculator.ui.components.ScreenTopBar
import com.knit.calculator.ui.theme.LocalKnitColors
import java.math.BigDecimal

/** КП, сохранённые в Google Таблице, — со всех телефонов. Нажатие открывает КП для изменения или повторной отправки. */
@Composable
fun QuoteHistoryScreen(viewModel: QuoteViewModel, onBack: () -> Unit, onOpened: () -> Unit) {
    val history by viewModel.history.collectAsStateWithLifecycle()
    val error by viewModel.historyError.collectAsStateWithLifecycle()
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val openError = stringResource(R.string.history_open_error)

    BackHandler(onBack = onBack)
    LaunchedEffect(Unit) { viewModel.loadHistory() }

    Column(Modifier.fillMaxSize().background(colors.background).safeDrawingPadding()) {
        ScreenTopBar(stringResource(R.string.history_quotes), onBack) {
            KnitIconButton(R.drawable.ic_arrow_down, stringResource(R.string.sync_refresh), viewModel::loadHistory)
        }
        val list = history
        when {
            error != null -> Message(stringResource(R.string.history_error, error.orEmpty()))
            list == null -> Message(stringResource(R.string.sync_loading))
            list.isEmpty() -> Message(stringResource(R.string.history_quotes_empty))
            else -> LazyColumn(
                Modifier.fillMaxSize().padding(horizontal = 16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                items(list, key = { it.id.ifBlank { it.number.toString() } }) { q ->
                    Surface(
                        onClick = {
                            if (viewModel.openQuote(q)) onOpened() else Toast.makeText(context, openError, Toast.LENGTH_LONG).show()
                        },
                        shape = RoundedCornerShape(18.dp),
                        color = colors.panel,
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                            Column(Modifier.weight(1f)) {
                                Text(
                                    stringResource(R.string.history_quote_title, q.number, q.date),
                                    color = colors.textPrimary,
                                    fontWeight = FontWeight.SemiBold,
                                    fontSize = 16.sp,
                                )
                                if (q.client.isNotBlank()) Text(q.client, color = colors.textSecondary, fontSize = 14.sp)
                                if (q.author.isNotBlank()) Text(q.author, color = colors.textSecondary, fontSize = 12.sp)
                            }
                            Text(
                                stringResource(R.string.quote_money, QuoteCalculator.formatMoney(BigDecimal.valueOf(q.total))),
                                color = if (colors.isDark) colors.accent else colors.textPrimary,
                                fontWeight = FontWeight.Bold,
                                fontSize = 16.sp,
                            )
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun Message(text: String) {
    Text(text, color = LocalKnitColors.current.textSecondary, fontSize = 15.sp, modifier = Modifier.padding(24.dp))
}
