package com.knit.calculator.ui.components

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.R
import com.knit.calculator.core.HistoryEntry
import com.knit.calculator.core.NumberFormatter
import com.knit.calculator.ui.theme.LocalKnitColors
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun HistorySheet(
    history: List<HistoryEntry>,
    onDismiss: () -> Unit,
    onRestore: (HistoryEntry) -> Unit,
    onCopy: (HistoryEntry) -> Unit,
    onClear: () -> Unit,
) {
    val colors = LocalKnitColors.current
    var confirmClear by rememberSaveable { mutableStateOf(false) }
    val sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = false)

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = sheetState,
        containerColor = colors.panel,
        contentColor = colors.textPrimary,
    ) {
        Row(
            Modifier.fillMaxWidth().padding(start = 24.dp, end = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(R.string.history),
                fontSize = 22.sp,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = { confirmClear = true }, enabled = history.isNotEmpty()) {
                Icon(painterResource(R.drawable.ic_delete), contentDescription = null)
                Spacer(Modifier.width(6.dp))
                Text(stringResource(R.string.history_clear), color = if (history.isNotEmpty()) colors.textPrimary else colors.textSecondary)
            }
        }

        if (history.isEmpty()) {
            Box(Modifier.fillMaxWidth().heightIn(min = 200.dp).padding(24.dp), contentAlignment = Alignment.Center) {
                Text(
                    text = stringResource(R.string.history_empty),
                    color = colors.textSecondary,
                    textAlign = TextAlign.Center,
                    fontSize = 16.sp,
                )
            }
        } else {
            val timeFormat = remember { SimpleDateFormat("dd.MM.yyyy, HH:mm", Locale.getDefault()) }
            LazyColumn(Modifier.fillMaxWidth()) {
                itemsIndexed(history) { index, entry ->
                    if (index > 0) HorizontalDivider(Modifier.padding(horizontal = 24.dp), color = colors.textSecondary.copy(alpha = 0.2f))
                    HistoryRow(entry, timeFormat.format(Date(entry.timestamp)), onRestore, onCopy)
                }
            }
        }
        Spacer(Modifier.height(16.dp))
    }

    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            title = { Text(stringResource(R.string.history_clear_title)) },
            text = { Text(stringResource(R.string.history_clear_text)) },
            confirmButton = {
                TextButton(onClick = { confirmClear = false; onClear() }) {
                    Text(stringResource(R.string.history_clear), color = colors.textPrimary, fontWeight = FontWeight.SemiBold)
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmClear = false }) {
                    Text(stringResource(R.string.cancel), color = colors.textSecondary)
                }
            },
            containerColor = colors.panel,
            titleContentColor = colors.textPrimary,
            textContentColor = colors.textSecondary,
        )
    }
}

@Composable
private fun HistoryRow(
    entry: HistoryEntry,
    time: String,
    onRestore: (HistoryEntry) -> Unit,
    onCopy: (HistoryEntry) -> Unit,
) {
    val colors = LocalKnitColors.current
    val restoreLabel = stringResource(R.string.history_restore)
    Row(
        Modifier
            .fillMaxWidth()
            .clickable(onClickLabel = restoreLabel) { onRestore(entry) }
            .padding(start = 24.dp, end = 8.dp, top = 12.dp, bottom = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(
                text = NumberFormatter.toDisplay(entry.expression),
                color = colors.textSecondary,
                fontSize = 16.sp,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = "= " + NumberFormatter.toDisplay(entry.result),
                color = if (colors.isDark) colors.accent else colors.textPrimary,
                fontSize = 24.sp,
                fontWeight = FontWeight.SemiBold,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(text = time, color = colors.textSecondary, fontSize = 12.sp)
        }
        IconButton(onClick = { onCopy(entry) }) {
            Icon(
                painterResource(R.drawable.ic_copy),
                contentDescription = stringResource(R.string.copy_result),
                tint = colors.textSecondary,
            )
        }
    }
}
