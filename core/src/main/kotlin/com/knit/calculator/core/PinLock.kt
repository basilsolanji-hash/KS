package com.knit.calculator.core

import java.security.MessageDigest

/**
 * Свой PIN-код приложения (4 цифры) на телефоне: хранится только хэш с солью,
 * после [MAX_FAILS] ошибок ввод блокируется на [PAUSE_MS].
 */
object PinLock {
    const val LENGTH = 4
    const val MAX_FAILS = 5
    const val PAUSE_MS = 30_000L

    /** Причина, по которой PIN не подходит; `null` — подходит. */
    fun problem(pin: String): String? = when {
        pin.length != LENGTH || !pin.all { it in '0'..'9' } -> "Нужно ровно $LENGTH цифры"
        pin.toSet().size == 1 -> "Слишком простой PIN: все цифры одинаковые"
        pin in "0123456789" || pin in "9876543210" -> "Слишком простой PIN: цифры подряд"
        else -> null
    }

    fun hash(pin: String, salt: String): String =
        MessageDigest.getInstance("SHA-256").digest("$salt:$pin".toByteArray()).joinToString("") { "%02x".format(it) }

    /** Состояние попыток: сколько ошибок подряд и до какого времени ввод закрыт. */
    data class Attempts(val fails: Int = 0, val blockedUntil: Long = 0L) {
        fun blocked(now: Long): Boolean = now < blockedUntil

        /** Ошибка ввода: на пятой — пауза, счётчик сначала. */
        fun failed(now: Long): Attempts =
            if (fails + 1 >= MAX_FAILS) Attempts(0, now + PAUSE_MS) else copy(fails = fails + 1)

        fun left(): Int = MAX_FAILS - fails
    }
}
