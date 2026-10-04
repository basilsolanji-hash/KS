package com.knit.calculator

import android.graphics.Color
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.knit.calculator.data.ThemeMode
import com.knit.calculator.ui.CalculatorScreen
import com.knit.calculator.ui.theme.KnitTheme
import com.knit.calculator.yarn.YarnScreen
import com.knit.calculator.yarn.YarnViewModel

class MainActivity : ComponentActivity() {

    private val viewModel: CalculatorViewModel by viewModels()
    private val yarnViewModel: YarnViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            val themeMode by viewModel.themeMode.collectAsStateWithLifecycle()
            val darkTheme = when (themeMode) {
                ThemeMode.SYSTEM -> isSystemInDarkTheme()
                ThemeMode.LIGHT -> false
                ThemeMode.DARK -> true
            }
            DisposableEffect(darkTheme) {
                val style = SystemBarStyle.auto(Color.TRANSPARENT, Color.TRANSPARENT) { darkTheme }
                enableEdgeToEdge(statusBarStyle = style, navigationBarStyle = style)
                onDispose {}
            }
            var showYarn by rememberSaveable { mutableStateOf(false) }
            KnitTheme(darkTheme = darkTheme) {
                if (showYarn) {
                    YarnScreen(viewModel = yarnViewModel, onBack = { showYarn = false })
                } else {
                    CalculatorScreen(viewModel = viewModel, themeMode = themeMode, onOpenYarn = { showYarn = true })
                }
            }
        }
    }
}
