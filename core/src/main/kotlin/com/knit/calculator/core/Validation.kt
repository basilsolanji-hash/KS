package com.knit.calculator.core

/** Проверка реквизитов клиента перед сохранением КП. */
object Validation {
    private val W10 = intArrayOf(2, 4, 10, 3, 5, 9, 4, 6, 8)
    private val W11 = intArrayOf(7, 2, 4, 10, 3, 5, 9, 4, 6, 8)
    private val W12 = intArrayOf(3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8)

    private fun check(digits: String, weights: IntArray): Int =
        weights.indices.sumOf { (digits[it] - '0') * weights[it] } % 11 % 10

    /** ИНН организации (10 цифр) или ИП (12 цифр) с контрольными цифрами. */
    fun inn(value: String): Boolean {
        val s = value.trim()
        if (!s.all { it.isDigit() }) return false
        return when (s.length) {
            10 -> check(s, W10) == s[9] - '0'
            12 -> check(s, W11) == s[10] - '0' && check(s, W12) == s[11] - '0'
            else -> false
        }
    }

    private val EMAIL = Regex("^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\\.[A-Za-z0-9-]+)*\\.[A-Za-z]{2,}$")

    fun email(value: String): Boolean = EMAIL.matches(value.trim())

    /** Телефон: 10–15 цифр (+7, 8, пробелы, скобки и дефисы допускаются). */
    fun phone(value: String): Boolean {
        val s = value.trim()
        if (s.any { !(it.isDigit() || it in "+-() ") }) return false
        return s.count { it.isDigit() } in 10..15
    }

    /** КПП: 9 символов (цифры, в 5–6 позиции допускаются латинские буквы). */
    fun kpp(value: String): Boolean = Regex("^\\d{4}[\\dA-Z]{2}\\d{3}$").matches(value.trim())
}
