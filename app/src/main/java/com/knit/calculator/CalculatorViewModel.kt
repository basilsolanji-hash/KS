package com.knit.calculator

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.SavedStateHandle
import com.knit.calculator.core.CalcError
import com.knit.calculator.core.CalculatorEngine
import com.knit.calculator.core.CalculatorState
import com.knit.calculator.core.HistoryEntry
import com.knit.calculator.data.HistoryRepository
import com.knit.calculator.data.SettingsRepository
import com.knit.calculator.data.ThemeMode
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update

/** Клавиши калькулятора — общие для экранных кнопок и аппаратной клавиатуры. */
sealed interface CalcKey {
    data class Digit(val digit: Char) : CalcKey
    data class Operator(val symbol: Char) : CalcKey
    data object Dot : CalcKey
    data object Percent : CalcKey
    data object Parentheses : CalcKey
    data object OpenParen : CalcKey
    data object CloseParen : CalcKey
    data object Sign : CalcKey
    data object Backspace : CalcKey
    data object Clear : CalcKey
    data object Equals : CalcKey
}

class CalculatorViewModel(
    application: Application,
    private val savedState: SavedStateHandle,
) : AndroidViewModel(application) {

    private val historyRepository = HistoryRepository(application)
    private val settingsRepository = SettingsRepository(application)

    private val _state = MutableStateFlow(restoreState())
    val state: StateFlow<CalculatorState> = _state.asStateFlow()

    // История небольшая (до 100 записей), поэтому читается сразу — так нет гонок с первыми вычислениями.
    private val _history = MutableStateFlow(historyRepository.load())
    val history: StateFlow<List<HistoryEntry>> = _history.asStateFlow()

    private val _themeMode = MutableStateFlow(settingsRepository.themeMode)
    val themeMode: StateFlow<ThemeMode> = _themeMode.asStateFlow()

    fun onKey(key: CalcKey) {
        val current = _state.value
        val next = when (key) {
            is CalcKey.Digit -> CalculatorEngine.digit(current, key.digit)
            is CalcKey.Operator -> CalculatorEngine.operator(current, key.symbol)
            CalcKey.Dot -> CalculatorEngine.dot(current)
            CalcKey.Percent -> CalculatorEngine.percent(current)
            CalcKey.Parentheses -> CalculatorEngine.parentheses(current)
            CalcKey.OpenParen -> CalculatorEngine.openParen(current)
            CalcKey.CloseParen -> CalculatorEngine.closeParen(current)
            CalcKey.Sign -> CalculatorEngine.toggleSign(current)
            CalcKey.Backspace -> CalculatorEngine.backspace(current)
            CalcKey.Clear -> CalculatorEngine.clear()
            CalcKey.Equals -> {
                val outcome = CalculatorEngine.evaluate(current)
                outcome.historyEntry?.let(::addToHistory)
                outcome.state
            }
        }
        setState(next)
    }

    fun restoreFromHistory(entry: HistoryEntry) = setState(CalculatorEngine.restore(entry))

    fun clearHistory() {
        _history.value = emptyList()
        historyRepository.save(emptyList())
    }

    fun cycleTheme() {
        val next = _themeMode.value.next()
        _themeMode.value = next
        settingsRepository.themeMode = next
    }

    private fun addToHistory(entry: HistoryEntry) {
        _history.update { (listOf(entry) + it).take(HistoryRepository.MAX_ENTRIES) }
        historyRepository.save(_history.value)
    }

    private fun setState(state: CalculatorState) {
        _state.value = state
        savedState[KEY_EXPRESSION] = state.expression
        savedState[KEY_EVALUATED] = state.evaluated
        savedState[KEY_PREVIOUS] = state.previousExpression
        savedState[KEY_ERROR] = state.error?.name
    }

    private fun restoreState(): CalculatorState {
        val expression = savedState.get<String>(KEY_EXPRESSION) ?: return CalculatorState()
        val restored = CalculatorEngine.fromExpression(
            expression = expression,
            evaluated = savedState.get<Boolean>(KEY_EVALUATED) ?: false,
            previousExpression = savedState.get<String>(KEY_PREVIOUS),
        )
        val error = savedState.get<String>(KEY_ERROR)
            ?.let { name -> CalcError.entries.firstOrNull { it.name == name } }
        return if (error != null) restored.copy(error = error, preview = null) else restored
    }

    private companion object {
        const val KEY_EXPRESSION = "expression"
        const val KEY_EVALUATED = "evaluated"
        const val KEY_PREVIOUS = "previous"
        const val KEY_ERROR = "error"
    }
}
