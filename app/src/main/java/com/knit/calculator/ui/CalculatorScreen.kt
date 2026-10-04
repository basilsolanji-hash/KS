package com.knit.calculator.ui

import android.os.Build
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.focusable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEvent
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.isAltPressed
import androidx.compose.ui.input.key.isCtrlPressed
import androidx.compose.ui.input.key.isMetaPressed
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.input.key.utf16CodePoint
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.min
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.CalcKey
import com.knit.calculator.CalculatorViewModel
import com.knit.calculator.R
import com.knit.calculator.core.CalculatorState
import com.knit.calculator.core.NumberFormatter
import com.knit.calculator.core.Symbols
import com.knit.calculator.data.ThemeMode
import com.knit.calculator.ui.components.CalculatorDisplay
import com.knit.calculator.ui.components.HistorySheet
import com.knit.calculator.ui.components.Keypad
import com.knit.calculator.ui.theme.LocalKnitColors

@Composable
fun CalculatorScreen(viewModel: CalculatorViewModel, themeMode: ThemeMode) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val history by viewModel.history.collectAsStateWithLifecycle()
    var showHistory by rememberSaveable { mutableStateOf(false) }
    val colors = LocalKnitColors.current
    val context = LocalContext.current
    val clipboard = LocalClipboardManager.current
    val copiedMessage = stringResource(R.string.copied)
    val focusRequester = remember { FocusRequester() }

    fun copy(raw: String) {
        if (raw.isEmpty()) return
        clipboard.setText(AnnotatedString(NumberFormatter.toClipboard(raw)))
        // Начиная с Android 13 система сама показывает уведомление о копировании.
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
            Toast.makeText(context, copiedMessage, Toast.LENGTH_SHORT).show()
        }
    }

    LaunchedEffect(showHistory) {
        if (!showHistory) runCatching { focusRequester.requestFocus() }
    }

    BoxWithConstraints(
        Modifier
            .fillMaxSize()
            .background(colors.background)
            .focusRequester(focusRequester)
            .onPreviewKeyEvent { event ->
                val key = event.toCalcKey() ?: return@onPreviewKeyEvent false
                viewModel.onKey(key)
                true
            }
            .focusable()
            .safeDrawingPadding(),
    ) {
        val landscape = maxWidth > maxHeight
        val topBar = @Composable {
            TopBar(
                themeMode = themeMode,
                onThemeClick = viewModel::cycleTheme,
                onHistoryClick = { showHistory = true },
            )
        }
        val display = @Composable { modifier: Modifier ->
            Column(modifier) {
                CalculatorDisplay(
                    state = state,
                    onCopy = { copy(state.copyableValue()) },
                    modifier = Modifier.fillMaxWidth().weight(1f),
                )
                BackspaceRow(onBackspace = { viewModel.onKey(CalcKey.Backspace) })
            }
        }

        if (landscape) {
            Row(Modifier.fillMaxSize().padding(12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Column(Modifier.weight(1f).fillMaxHeight()) {
                    topBar()
                    display(Modifier.fillMaxWidth().weight(1f))
                }
                Keypad(viewModel::onKey, Modifier.weight(1.1f).fillMaxHeight())
            }
        } else {
            // Клавиатура внизу экрана — удобно для управления одной рукой.
            val keypadHeight = min(maxWidth * 1.2f, maxHeight * 0.64f)
            Column(Modifier.fillMaxSize().padding(horizontal = 16.dp, vertical = 8.dp)) {
                topBar()
                display(Modifier.fillMaxWidth().weight(1f))
                Keypad(viewModel::onKey, Modifier.fillMaxWidth().height(keypadHeight).padding(bottom = 8.dp))
            }
        }
    }

    if (showHistory) {
        HistorySheet(
            history = history,
            onDismiss = { showHistory = false },
            onRestore = { entry ->
                viewModel.restoreFromHistory(entry)
                showHistory = false
            },
            onCopy = { copy(it.result) },
            onClear = viewModel::clearHistory,
        )
    }
}

private fun CalculatorState.copyableValue(): String =
    if (evaluated) expression else preview ?: expression

@Composable
private fun TopBar(themeMode: ThemeMode, onThemeClick: () -> Unit, onHistoryClick: () -> Unit) {
    val colors = LocalKnitColors.current
    val themeName = stringResource(
        when (themeMode) {
            ThemeMode.SYSTEM -> R.string.theme_system
            ThemeMode.LIGHT -> R.string.theme_light
            ThemeMode.DARK -> R.string.theme_dark
        },
    )
    Row(Modifier.fillMaxWidth().height(56.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(
            text = stringResource(R.string.app_name),
            color = colors.textSecondary,
            fontSize = 18.sp,
            fontWeight = FontWeight.Medium,
            modifier = Modifier.weight(1f).padding(start = 8.dp).semantics { heading() },
        )
        IconButton(onClick = onThemeClick) {
            Icon(
                painter = painterResource(
                    when (themeMode) {
                        ThemeMode.SYSTEM -> R.drawable.ic_theme_auto
                        ThemeMode.LIGHT -> R.drawable.ic_theme_light
                        ThemeMode.DARK -> R.drawable.ic_theme_dark
                    },
                ),
                contentDescription = stringResource(R.string.theme_toggle, themeName),
                tint = colors.textSecondary,
            )
        }
        IconButton(onClick = onHistoryClick) {
            Icon(
                painter = painterResource(R.drawable.ic_history),
                contentDescription = stringResource(R.string.history_open),
                tint = colors.accent.takeIf { colors.isDark } ?: colors.textPrimary,
            )
        }
    }
}

@Composable
private fun BackspaceRow(onBackspace: () -> Unit) {
    val colors = LocalKnitColors.current
    Row(Modifier.fillMaxWidth().padding(bottom = 8.dp), horizontalArrangement = Arrangement.End) {
        IconButton(onClick = onBackspace, modifier = Modifier.padding(end = 4.dp)) {
            Icon(
                painter = painterResource(R.drawable.ic_backspace),
                contentDescription = stringResource(R.string.key_backspace),
                tint = colors.accent.takeIf { colors.isDark } ?: colors.textPrimary,
            )
        }
    }
}

/** Аппаратная клавиатура: цифры, + − * /, %, скобки, Enter/=, Backspace, Esc/Delete. */
private fun KeyEvent.toCalcKey(): CalcKey? {
    if (type != KeyEventType.KeyDown) return null
    if (isCtrlPressed || isAltPressed || isMetaPressed) return null
    when (key) {
        Key.Enter, Key.NumPadEnter -> return CalcKey.Equals
        Key.Backspace -> return CalcKey.Backspace
        Key.Escape, Key.Delete -> return CalcKey.Clear
    }
    return when (val c = utf16CodePoint.toChar()) {
        in '0'..'9' -> CalcKey.Digit(c)
        '.', ',' -> CalcKey.Dot
        '+' -> CalcKey.Operator(Symbols.PLUS)
        '-' -> CalcKey.Operator(Symbols.MINUS)
        '*', 'x', 'X' -> CalcKey.Operator(Symbols.TIMES)
        '/', ':' -> CalcKey.Operator(Symbols.DIVIDE)
        '%' -> CalcKey.Percent
        '(' -> CalcKey.OpenParen
        ')' -> CalcKey.CloseParen
        '=' -> CalcKey.Equals
        else -> null
    }
}
