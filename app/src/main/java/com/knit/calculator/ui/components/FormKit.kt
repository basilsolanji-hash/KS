package com.knit.calculator.ui.components

import android.content.Context
import android.os.Build
import android.widget.Toast
import androidx.annotation.DrawableRes
import androidx.annotation.StringRes
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.RowScope
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.knit.calculator.R
import com.knit.calculator.ui.theme.LocalKnitColors

// Общие элементы форм для экранов «Расход пряжи» и «Коммерческое предложение».

fun toastIfNeeded(context: Context, message: String) {
    // Начиная с Android 13 система сама сообщает о копировании.
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) {
        Toast.makeText(context, message, Toast.LENGTH_SHORT).show()
    }
}

@Composable
fun SectionTitle(@StringRes title: Int) {
    Text(
        stringResource(title),
        color = LocalKnitColors.current.textSecondary,
        fontSize = 15.sp,
        fontWeight = FontWeight.SemiBold,
        modifier = Modifier.padding(top = 8.dp).semantics { heading() },
    )
}

@Composable
fun KnitField(
    value: String,
    onChange: (String) -> Unit,
    @StringRes label: Int,
    modifier: Modifier = Modifier.fillMaxWidth(),
    text: Boolean = false,
    placeholder: String? = null,
    keyboardType: KeyboardType? = null,
    singleLine: Boolean = true,
    maxLength: Int = 60,
    suffix: String? = null,
    enabled: Boolean = true,
    /** Текст ошибки под полем (неверный ИНН, e-mail…); `null` — ошибки нет. */
    error: String? = null,
) {
    val colors = LocalKnitColors.current
    val focus = if (colors.isDark) colors.accent else colors.textPrimary
    OutlinedTextField(
        value = value,
        isError = error != null,
        supportingText = if (error != null) { { Text(error) } } else null,
        onValueChange = { v ->
            onChange(if (text) v.take(maxLength) else v.filter { it.isDigit() || it == ',' || it == '.' }.take(12))
        },
        label = { Text(stringResource(label)) },
        enabled = enabled,
        placeholder = if (placeholder != null) { { Text(placeholder) } } else null,
        suffix = if (suffix != null) { { Text(suffix) } } else null,
        singleLine = singleLine,
        minLines = if (singleLine) 1 else 2,
        keyboardOptions = KeyboardOptions(
            keyboardType = keyboardType ?: if (text) KeyboardType.Text else KeyboardType.Decimal,
            capitalization = if (text && keyboardType == null) KeyboardCapitalization.Sentences else KeyboardCapitalization.None,
            imeAction = if (singleLine) ImeAction.Next else ImeAction.Default,
        ),
        colors = OutlinedTextFieldDefaults.colors(
            focusedTextColor = colors.textPrimary,
            unfocusedTextColor = colors.textPrimary,
            focusedBorderColor = focus,
            unfocusedBorderColor = colors.textSecondary.copy(alpha = 0.5f),
            focusedLabelColor = focus,
            unfocusedLabelColor = colors.textSecondary,
            cursorColor = focus,
            focusedPlaceholderColor = colors.textSecondary,
            unfocusedPlaceholderColor = colors.textSecondary,
            focusedSuffixColor = colors.textSecondary,
            unfocusedSuffixColor = colors.textSecondary,
            disabledTextColor = colors.textPrimary,
            disabledBorderColor = colors.textSecondary.copy(alpha = 0.2f),
            disabledLabelColor = colors.textSecondary,
            disabledSuffixColor = colors.textSecondary,
        ),
        modifier = modifier,
    )
}

@Composable
fun ActionButton(
    @StringRes label: Int,
    @DrawableRes icon: Int,
    primary: Boolean,
    modifier: Modifier,
    onClick: () -> Unit,
) {
    val colors = LocalKnitColors.current
    FilledTonalButton(
        onClick = onClick,
        modifier = modifier.height(52.dp),
        shape = RoundedCornerShape(16.dp),
        colors = ButtonDefaults.filledTonalButtonColors(
            containerColor = if (primary) colors.equalsKey else colors.panel,
            contentColor = if (primary) colors.equalsKeyText else colors.textPrimary,
        ),
    ) {
        Icon(painterResource(icon), null, Modifier.size(20.dp))
        Spacer(Modifier.width(8.dp))
        Text(stringResource(label), fontWeight = FontWeight.SemiBold)
    }
}

/** Верхняя панель вложенного экрана: «назад», заголовок и дополнительные действия справа. */
@Composable
fun ScreenTopBar(
    title: String,
    onBack: () -> Unit,
    actions: @Composable RowScope.() -> Unit = {},
) {
    val colors = LocalKnitColors.current
    Row(
        Modifier.fillMaxWidth().height(56.dp).padding(horizontal = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        IconButton(onClick = onBack) {
            Icon(painterResource(R.drawable.ic_arrow_back), stringResource(R.string.back), tint = colors.textPrimary)
        }
        Text(
            title,
            color = colors.textPrimary,
            fontSize = 20.sp,
            fontWeight = FontWeight.SemiBold,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f).semantics { heading() },
        )
        actions()
    }
}

/** Иконка-кнопка в цветах темы. */
@Composable
fun KnitIconButton(@DrawableRes icon: Int, description: String, onClick: () -> Unit, enabled: Boolean = true) {
    val colors = LocalKnitColors.current
    IconButton(onClick = onClick, enabled = enabled) {
        Icon(
            painterResource(icon),
            description,
            tint = if (enabled) colors.textSecondary else colors.textSecondary.copy(alpha = 0.35f),
        )
    }
}
