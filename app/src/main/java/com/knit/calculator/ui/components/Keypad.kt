package com.knit.calculator.ui.components

import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.CalcKey
import com.knit.calculator.R
import com.knit.calculator.core.Symbols
import com.knit.calculator.ui.theme.LocalKnitColors

enum class KeyStyle { DIGIT, OPERATOR, FUNCTION, EQUALS }

data class KeySpec(
    val label: String,
    val key: CalcKey,
    val style: KeyStyle,
    @StringRes val description: Int? = null,
)

private fun digit(c: Char) = KeySpec(c.toString(), CalcKey.Digit(c), KeyStyle.DIGIT)
private fun op(c: Char, @StringRes d: Int) = KeySpec(c.toString(), CalcKey.Operator(c), KeyStyle.OPERATOR, d)

val KeypadLayout: List<List<KeySpec>> = listOf(
    listOf(
        KeySpec("AC", CalcKey.Clear, KeyStyle.FUNCTION, R.string.key_clear),
        KeySpec("( )", CalcKey.Parentheses, KeyStyle.FUNCTION, R.string.key_parentheses),
        KeySpec("%", CalcKey.Percent, KeyStyle.FUNCTION, R.string.key_percent),
        op(Symbols.DIVIDE, R.string.key_divide),
    ),
    listOf(digit('7'), digit('8'), digit('9'), op(Symbols.TIMES, R.string.key_multiply)),
    listOf(digit('4'), digit('5'), digit('6'), op(Symbols.MINUS, R.string.key_minus)),
    listOf(digit('1'), digit('2'), digit('3'), op(Symbols.PLUS, R.string.key_plus)),
    listOf(
        KeySpec("±", CalcKey.Sign, KeyStyle.FUNCTION, R.string.key_sign),
        digit('0'),
        KeySpec(",", CalcKey.Dot, KeyStyle.DIGIT, R.string.key_decimal),
        KeySpec("=", CalcKey.Equals, KeyStyle.EQUALS, R.string.key_equals),
    ),
)

@Composable
fun Keypad(onKey: (CalcKey) -> Unit, modifier: Modifier = Modifier) {
    val spacing = 10.dp
    Column(modifier, verticalArrangement = Arrangement.spacedBy(spacing)) {
        KeypadLayout.forEach { row ->
            Row(
                Modifier.fillMaxWidth().weight(1f),
                horizontalArrangement = Arrangement.spacedBy(spacing),
            ) {
                row.forEach { spec ->
                    CalculatorKey(spec, onKey, Modifier.weight(1f).fillMaxHeight())
                }
            }
        }
    }
}

@Composable
private fun CalculatorKey(spec: KeySpec, onKey: (CalcKey) -> Unit, modifier: Modifier) {
    val colors = LocalKnitColors.current
    val haptics = LocalHapticFeedback.current
    val (container, content) = when (spec.style) {
        KeyStyle.DIGIT -> colors.digitKey to colors.digitKeyText
        KeyStyle.OPERATOR -> colors.operatorKey to colors.operatorKeyText
        KeyStyle.FUNCTION -> colors.functionKey to colors.functionKeyText
        KeyStyle.EQUALS -> colors.equalsKey to colors.equalsKeyText
    }
    val description = spec.description?.let { stringResource(it) }
    Surface(
        onClick = {
            haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
            onKey(spec.key)
        },
        modifier = modifier.semantics {
            role = Role.Button
            if (description != null) contentDescription = description
        },
        shape = RoundedCornerShape(22.dp),
        color = container,
        contentColor = content,
    ) {
        Box(contentAlignment = Alignment.Center) {
            Text(
                text = spec.label,
                fontSize = if (spec.label.length > 2) 22.sp else 30.sp,
                fontWeight = if (spec.style == KeyStyle.DIGIT) FontWeight.Normal else FontWeight.Medium,
                maxLines = 1,
            )
        }
    }
}
