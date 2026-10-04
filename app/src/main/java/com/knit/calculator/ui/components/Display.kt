package com.knit.calculator.ui.components

import androidx.annotation.StringRes
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.R
import com.knit.calculator.core.CalcError
import com.knit.calculator.core.CalculatorState
import com.knit.calculator.core.NumberFormatter
import com.knit.calculator.ui.theme.LocalKnitColors

@StringRes
fun CalcError.messageRes(): Int = when (this) {
    CalcError.DIVISION_BY_ZERO -> R.string.error_division_by_zero
    CalcError.INVALID_EXPRESSION -> R.string.error_invalid
    CalcError.OVERFLOW -> R.string.error_overflow
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
fun CalculatorDisplay(
    state: CalculatorState,
    onCopy: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val colors = LocalKnitColors.current
    val main = NumberFormatter.toDisplay(state.expression).ifEmpty { "0" }
    val error = state.error
    val preview = state.preview
    val secondary: String? = when {
        error != null -> stringResource(error.messageRes())
        state.evaluated -> null
        preview != null -> "= " + NumberFormatter.toDisplay(preview)
        else -> null
    }
    val copyLabel = stringResource(R.string.copy_result)
    val resultLabel = stringResource(if (state.evaluated) R.string.display_result else R.string.display_expression)

    Column(
        modifier = modifier
            .combinedClickable(
                interactionSource = remember { MutableInteractionSource() },
                indication = null,
                onClick = {},
                onLongClick = onCopy,
                onLongClickLabel = copyLabel,
            )
            .padding(horizontal = 24.dp, vertical = 8.dp),
        verticalArrangement = Arrangement.Bottom,
        horizontalAlignment = Alignment.End,
    ) {
        // Выражение, давшее результат («2+3 =»).
        Text(
            text = state.previousExpression?.let { NumberFormatter.toDisplay(it) + " =" } ?: "",
            color = colors.textSecondary,
            fontSize = 22.sp,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            textAlign = TextAlign.End,
            modifier = Modifier.fillMaxWidth(),
        )

        BoxWithConstraints(Modifier.fillMaxWidth()) {
            val density = LocalDensity.current
            // Подбираем размер шрифта так, чтобы выражение помещалось; длинное — прокручивается.
            val fitted = with(density) { (maxWidth.toPx() / (main.length.coerceAtLeast(1) * 0.6f)).toSp() }
            val fontSize = fitted.value.coerceIn(34f, 68f).sp
            Text(
                text = main,
                color = if (state.evaluated) colors.result else colors.textPrimary,
                fontSize = fontSize,
                lineHeight = fontSize * 1.15f,
                fontWeight = if (state.evaluated) FontWeight.SemiBold else FontWeight.Normal,
                maxLines = 1,
                softWrap = false,
                textAlign = TextAlign.End,
                modifier = Modifier
                    .fillMaxWidth()
                    .horizontalScroll(rememberScrollState(), reverseScrolling = true)
                    .semantics {
                        contentDescription = "$resultLabel: $main"
                        liveRegion = LiveRegionMode.Polite
                    },
            )
        }

        Text(
            text = secondary ?: "",
            color = colors.textSecondary,
            fontSize = 26.sp,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            textAlign = TextAlign.End,
            modifier = Modifier
                .fillMaxWidth()
                .heightIn(min = 34.dp)
                .semantics { if (error != null) liveRegion = LiveRegionMode.Assertive },
        )
    }
}
