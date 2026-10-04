package com.knit.calculator.data

import android.content.Context

enum class ThemeMode {
    SYSTEM, LIGHT, DARK;

    fun next(): ThemeMode = entries[(ordinal + 1) % entries.size]
}

class SettingsRepository(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    var themeMode: ThemeMode
        get() = prefs.getString(KEY_THEME, null)
            ?.let { name -> ThemeMode.entries.firstOrNull { it.name == name } }
            ?: ThemeMode.SYSTEM
        set(value) = prefs.edit().putString(KEY_THEME, value.name).apply()

    private companion object {
        const val PREFS = "calculator_settings"
        const val KEY_THEME = "theme"
    }
}
