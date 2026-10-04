package com.knit.calculator.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.Immutable
import androidx.compose.runtime.staticCompositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver

/** Фирменная палитра Knit ERP. Другие оттенки получаются только прозрачностью этих цветов. */
object KnitPalette {
    val Dark = Color(0xFF1E1E20)
    val Panel = Color(0xFF2A2A2E)
    val Turquoise = Color(0xFF30D5C8)
    val White = Color(0xFFFFFFFF)
    val Gray = Color(0xFFD1D5DB)
}

@Immutable
data class KnitColors(
    val isDark: Boolean,
    val background: Color,
    val panel: Color,
    val textPrimary: Color,
    val textSecondary: Color,
    val result: Color,
    val digitKey: Color,
    val digitKeyText: Color,
    val operatorKey: Color,
    val operatorKeyText: Color,
    val functionKey: Color,
    val functionKeyText: Color,
    val equalsKey: Color,
    val equalsKeyText: Color,
    val accent: Color,
)

private val DarkKnitColors = KnitColors(
    isDark = true,
    background = KnitPalette.Dark,
    panel = KnitPalette.Panel,
    textPrimary = KnitPalette.White,
    textSecondary = KnitPalette.Gray,
    result = KnitPalette.Turquoise,
    digitKey = KnitPalette.Panel,
    digitKeyText = KnitPalette.White,
    operatorKey = KnitPalette.Turquoise.copy(alpha = 0.16f).compositeOver(KnitPalette.Panel),
    operatorKeyText = KnitPalette.Turquoise,
    functionKey = KnitPalette.Gray.copy(alpha = 0.14f).compositeOver(KnitPalette.Panel),
    functionKeyText = KnitPalette.Gray,
    equalsKey = KnitPalette.Turquoise,
    equalsKeyText = KnitPalette.Dark,
    accent = KnitPalette.Turquoise,
)

// В светлой теме бирюзовый используется как заливка с тёмным текстом:
// бирюзовый текст на белом фоне был бы недостаточно контрастным.
private val LightKnitColors = KnitColors(
    isDark = false,
    background = KnitPalette.White,
    panel = KnitPalette.Gray.copy(alpha = 0.35f).compositeOver(KnitPalette.White),
    textPrimary = KnitPalette.Dark,
    textSecondary = KnitPalette.Dark.copy(alpha = 0.68f),
    result = KnitPalette.Dark,
    digitKey = KnitPalette.Gray.copy(alpha = 0.35f).compositeOver(KnitPalette.White),
    digitKeyText = KnitPalette.Dark,
    operatorKey = KnitPalette.Turquoise.copy(alpha = 0.28f).compositeOver(KnitPalette.White),
    operatorKeyText = KnitPalette.Dark,
    functionKey = KnitPalette.Gray.copy(alpha = 0.75f).compositeOver(KnitPalette.White),
    functionKeyText = KnitPalette.Dark,
    equalsKey = KnitPalette.Turquoise,
    equalsKeyText = KnitPalette.Dark,
    accent = KnitPalette.Turquoise,
)

val LocalKnitColors = staticCompositionLocalOf { DarkKnitColors }

@Composable
fun KnitTheme(darkTheme: Boolean, content: @Composable () -> Unit) {
    val colors = if (darkTheme) DarkKnitColors else LightKnitColors
    val scheme = if (darkTheme) {
        darkColorScheme(
            primary = KnitPalette.Turquoise,
            onPrimary = KnitPalette.Dark,
            background = KnitPalette.Dark,
            onBackground = KnitPalette.White,
            surface = KnitPalette.Dark,
            onSurface = KnitPalette.White,
            surfaceVariant = KnitPalette.Panel,
            onSurfaceVariant = KnitPalette.Gray,
            surfaceContainerLow = KnitPalette.Panel,
            surfaceContainerHigh = KnitPalette.Panel,
            outlineVariant = KnitPalette.Gray.copy(alpha = 0.2f),
        )
    } else {
        lightColorScheme(
            primary = KnitPalette.Dark,
            onPrimary = KnitPalette.White,
            background = KnitPalette.White,
            onBackground = KnitPalette.Dark,
            surface = KnitPalette.White,
            onSurface = KnitPalette.Dark,
            surfaceVariant = colors.panel,
            onSurfaceVariant = colors.textSecondary,
            surfaceContainerLow = KnitPalette.White,
            surfaceContainerHigh = KnitPalette.White,
            outlineVariant = KnitPalette.Gray,
        )
    }
    CompositionLocalProvider(LocalKnitColors provides colors) {
        MaterialTheme(colorScheme = scheme, content = content)
    }
}
