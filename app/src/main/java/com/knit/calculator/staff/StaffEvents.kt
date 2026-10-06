package com.knit.calculator.staff

/**
 * Действия для отчёта «Активность»: экраны сообщают «создал КП», «правил товар», «отгрузил»…
 * Отправку делает StaffViewModel (только для офиса, если телефон подключён к серверу фабрики).
 */
object StaffEvents {
    @Volatile
    var sink: ((kind: String, detail: String) -> Unit)? = null

    fun log(kind: String, detail: String = "") {
        runCatching { sink?.invoke(kind, detail) }
    }
}
